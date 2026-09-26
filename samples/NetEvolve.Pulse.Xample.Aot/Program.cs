using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Xample.Aot;

// NativeAOT smoke test for Pulse. Exit code 0 means every check passed.
var failures = new List<string>();

void Check(bool condition, string description)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {description}");
    if (!condition)
    {
        failures.Add(description);
    }
}

// Scenario 1: AddPulse with the source-generated handler registrations, covering reference-type,
// value-type and void responses, a query, a stream query, and closed as well as open-generic event handlers.
await RunAsync(
        configure: null,
        async (mediator, recorder) =>
        {
            var order = await mediator
                .SendAsync<CreateOrderCommand, OrderResult>(new CreateOrderCommand("A-1"))
                .ConfigureAwait(false);
            Check(order == new OrderResult("A-1", Accepted: true), "command with reference-type response");

            var sum = await mediator
                .SendAsync<AddNumbersCommand, int>(new AddNumbersCommand(2, 3))
                .ConfigureAwait(false);
            Check(sum == 5, "command with value-type response");

            await mediator.SendAsync(new PingCommand()).ConfigureAwait(false);
            Check(recorder.Invocations.Contains(nameof(PingHandler)), "void command");

            var greeting = await mediator
                .QueryAsync<GreetingQuery, string>(new GreetingQuery("AOT"))
                .ConfigureAwait(false);
            Check(string.Equals(greeting, "Hello, AOT!", StringComparison.Ordinal), "query");

            var countdown = await CollectAsync(
                    mediator.StreamQueryAsync<CountdownStreamQuery, string>(new CountdownStreamQuery(3))
                )
                .ConfigureAwait(false);
            Check(string.Join(',', countdown) == "3,2,1", "stream query");

            await mediator.PublishAsync(new OrderCreatedEvent("A-1")).ConfigureAwait(false);
            Check(recorder.Invocations.Contains(nameof(OrderCreatedHandler)), "event handler");
            Check(
                recorder.Invocations.Contains("AuditEventHandler<OrderCreatedEvent>"),
                "open-generic event handler closed by the DI container"
            );

            // The outbox providers persist ToOutboxEventTypeName() and rehydrate it with Type.GetType. Their IL2057
            // suppressions rely on this round-trip for event types that the application publishes. The event type is
            // never named through typeof, and the persisted name is computed at runtime, so the compiler cannot root
            // the type for the lookup.
            var shipment = new ShipmentDispatchedEvent("S-1");
            await mediator.PublishAsync(shipment).ConfigureAwait(false);
            Check(
                recorder.Invocations.Contains("AuditEventHandler<ShipmentDispatchedEvent>"),
                "event without a closed handler"
            );
            var persistedEventType = shipment.GetType().ToOutboxEventTypeName();
            Check(
                ResolveOutboxEventType(persistedEventType) == shipment.GetType(),
                "outbox event type name round-trip for a published event type"
            );
        }
    )
    .ConfigureAwait(false);

// Scenario 2: open-generic interceptors (activity/metrics and logging, with options bound through the
// configuration binding source generator, plus a recording interceptor that proves the pipeline runs). The DI
// container can only close open-generic services over reference types under NativeAOT, so this scenario uses
// reference-type responses only.
await RunAsync(
        config =>
        {
            _ = config.AddActivityAndMetrics().AddLogging();
            config.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(RecordingRequestInterceptor<,>))
            );
        },
        async (mediator, recorder) =>
        {
            var order = await mediator
                .SendAsync<CreateOrderCommand, OrderResult>(new CreateOrderCommand("B-1"))
                .ConfigureAwait(false);
            Check(order.OrderId == "B-1", "command through open-generic interceptors");
            Check(
                recorder.Invocations.Contains("RecordingRequestInterceptor<CreateOrderCommand>"),
                "open-generic interceptor invoked for the command"
            );

            var greeting = await mediator
                .QueryAsync<GreetingQuery, string>(new GreetingQuery("interceptors"))
                .ConfigureAwait(false);
            Check(
                string.Equals(greeting, "Hello, interceptors!", StringComparison.Ordinal),
                "query through open-generic interceptors"
            );
            Check(
                recorder.Invocations.Contains("RecordingRequestInterceptor<GreetingQuery>"),
                "open-generic interceptor invoked for the query"
            );

            var countdown = await CollectAsync(
                    mediator.StreamQueryAsync<CountdownStreamQuery, string>(new CountdownStreamQuery(2))
                )
                .ConfigureAwait(false);
            Check(string.Join(',', countdown) == "2,1", "stream query through open-generic interceptors");

            await mediator.PublishAsync(new OrderCreatedEvent("B-1")).ConfigureAwait(false);
            Check(
                recorder.Invocations.Contains(nameof(OrderCreatedHandler)),
                "event through open-generic interceptors"
            );
        }
    )
    .ConfigureAwait(false);

// Scenario 3: built-in open-generic interceptors for value-type and Void responses. The DI container cannot close
// open-generic services over value types under NativeAOT, so the generated handler registrations add closed
// interceptor registrations for these requests. This requires the generated method to run after AddPulse.
await RunAsync(
        config =>
        {
            _ = config
                .AddActivityAndMetrics()
                .AddLogging(options => options.LogLevel = LogLevel.Information)
                .AddConcurrentCommandGuard<ReserveStockCommand>();
            _ = config.Services.AddSingleton<ILoggerProvider, RecordingLoggerProvider>();
            config.Services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRequestInterceptor<AddNumbersCommand, int>, AddNumbersRecordingInterceptor>()
            );
        },
        async (mediator, recorder) =>
        {
            var sum = await mediator
                .SendAsync<AddNumbersCommand, int>(new AddNumbersCommand(4, 5))
                .ConfigureAwait(false);
            Check(sum == 9, "command with value-type response through built-in interceptors");
            Check(
                recorder.Invocations.Contains("Handling Command 'AddNumbersCommand' (CorrelationId: )"),
                "built-in logging interceptor invoked for the value-type response"
            );
            Check(
                recorder.Invocations.Contains(nameof(AddNumbersRecordingInterceptor)),
                "closed interceptor invoked for the value-type response"
            );

            await mediator.SendAsync(new PingCommand()).ConfigureAwait(false);
            Check(
                recorder.Invocations.Contains("Handling Command 'PingCommand' (CorrelationId: )"),
                "built-in logging interceptor invoked for the void command"
            );

            await mediator.SendAsync(new ReserveStockCommand("SKU-1")).ConfigureAwait(false);
            Check(
                recorder.Invocations.Contains(nameof(ReserveStockHandler))
                    && recorder.Invocations.Contains("Handling Command 'ReserveStockCommand' (CorrelationId: )"),
                "exclusive void command through the closed concurrent command guard"
            );

            var range = new List<int>();
            await foreach (
                var item in mediator
                    .StreamQueryAsync<RangeStreamQuery, int>(new RangeStreamQuery(3))
                    .ConfigureAwait(false)
            )
            {
                range.Add(item);
            }

            Check(range.SequenceEqual([1, 2, 3]), "stream query with value-type items through built-in interceptors");
            Check(
                recorder.Invocations.Contains("Streaming 'RangeStreamQuery' (CorrelationId: )"),
                "built-in logging interceptor invoked for the value-type stream query"
            );

            var order = await mediator
                .SendAsync<CreateOrderCommand, OrderResult>(new CreateOrderCommand("V-1"))
                .ConfigureAwait(false);
            Check(
                order.OrderId == "V-1"
                    && recorder.Invocations.Contains("Handling Command 'CreateOrderCommand' (CorrelationId: )"),
                "reference-type response keeps the open-generic interceptors"
            );
        }
    )
    .ConfigureAwait(false);

// Scenario 4: the registered IPayloadSerializer with an application JsonSerializerContext, configured as documented
// in the "Payload Serialization Under NativeAOT" section of the NetEvolve.Pulse README.
var serializerProvider = new ServiceCollection()
    .Configure<JsonSerializerOptions>(options => options.TypeInfoResolverChain.Insert(0, SmokeJsonContext.Default))
    .AddPulse()
    .BuildServiceProvider();
await using (serializerProvider.ConfigureAwait(false))
{
    var serializer = serializerProvider.GetRequiredService<IPayloadSerializer>();
    var payload = serializer.Serialize(new OrderCreatedEvent("C-1") { CorrelationId = "corr-1" });
    var restored = serializer.Deserialize<OrderCreatedEvent>(payload);
    Check(
        restored is { OrderId: "C-1", CorrelationId: "corr-1" },
        "payload round-trip through a source-generated context"
    );
    Check(
        Throws<NotSupportedException>(() => serializer.Serialize(new UnregisteredPayload(1))),
        "payload type missing from the source-generated context is rejected"
    );
}

// Without an application context the serializer must not fall back to reflection in trimmed and NativeAOT
// applications, where the JsonSerializer.IsReflectionEnabledByDefault feature switch is off.
if (!JsonSerializer.IsReflectionEnabledByDefault)
{
    var reflectionFreeProvider = new ServiceCollection().AddPulse().BuildServiceProvider();
    await using (reflectionFreeProvider.ConfigureAwait(false))
    {
        var serializer = reflectionFreeProvider.GetRequiredService<IPayloadSerializer>();
        Check(
            Throws<NotSupportedException>(() => serializer.Serialize(new OrderCreatedEvent("D-1"))),
            "payload serialization without a context does not fall back to reflection"
        );
    }
}

Console.WriteLine(failures.Count == 0 ? "Pulse NativeAOT smoke test succeeded." : "Pulse NativeAOT smoke test failed.");
return failures.Count == 0 ? 0 : 1;

[UnconditionalSuppressMessage(
    "Trimming",
    "IL2057:Unrecognized value passed to the parameter of method with 'DynamicallyAccessedMembersAttribute'",
    Justification = "Mirrors the outbox providers' event type lookup to verify it under NativeAOT."
)]
static Type? ResolveOutboxEventType(string persistedEventType) => Type.GetType(persistedEventType);

static bool Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
        return false;
    }
    catch (TException)
    {
        return true;
    }
}

static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> source)
{
    var items = new List<string>();
    await foreach (var item in source.ConfigureAwait(false))
    {
        items.Add(item);
    }

    return items;
}

static async Task RunAsync(Action<IMediatorBuilder>? configure, Func<IMediator, InvocationRecorder, Task> scenario)
{
    var services = new ServiceCollection();
    _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    _ = services.AddLogging();
    _ = services.AddSingleton<InvocationRecorder>();
    _ = services.AddPulse(configure);
    _ = services.AddNetEvolvePulseXampleAotPulseHandlers();

    var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    await using (provider.ConfigureAwait(false))
    {
        var scope = provider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await scenario(
                    scope.ServiceProvider.GetRequiredService<IMediator>(),
                    scope.ServiceProvider.GetRequiredService<InvocationRecorder>()
                )
                .ConfigureAwait(false);
        }
    }
}
