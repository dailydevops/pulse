using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Tests.Aot;

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
// value-type and void responses, a query, and closed as well as open-generic event handlers.
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

            await mediator.PublishAsync(new OrderCreatedEvent("A-1")).ConfigureAwait(false);
            Check(recorder.Invocations.Contains(nameof(OrderCreatedHandler)), "event handler");
            Check(
                recorder.Invocations.Contains("AuditEventHandler<OrderCreatedEvent>"),
                "open-generic event handler closed by the DI container"
            );
        }
    )
    .ConfigureAwait(false);

// Scenario 2: open-generic interceptors (activity/metrics and logging, with options bound through the
// configuration binding source generator). The DI container can only close open-generic services over
// reference types under NativeAOT, so this scenario uses reference-type responses only.
await RunAsync(
        config => config.AddActivityAndMetrics().AddLogging(),
        async (mediator, recorder) =>
        {
            var order = await mediator
                .SendAsync<CreateOrderCommand, OrderResult>(new CreateOrderCommand("B-1"))
                .ConfigureAwait(false);
            Check(order.OrderId == "B-1", "command through open-generic interceptors");

            var greeting = await mediator
                .QueryAsync<GreetingQuery, string>(new GreetingQuery("interceptors"))
                .ConfigureAwait(false);
            Check(
                string.Equals(greeting, "Hello, interceptors!", StringComparison.Ordinal),
                "query through open-generic interceptors"
            );

            await mediator.PublishAsync(new OrderCreatedEvent("B-1")).ConfigureAwait(false);
            Check(
                recorder.Invocations.Contains(nameof(OrderCreatedHandler)),
                "event through open-generic interceptors"
            );
        }
    )
    .ConfigureAwait(false);

Console.WriteLine(failures.Count == 0 ? "Pulse NativeAOT smoke test succeeded." : "Pulse NativeAOT smoke test failed.");
return failures.Count == 0 ? 0 : 1;

static async Task RunAsync(Action<IMediatorBuilder>? configure, Func<IMediator, InvocationRecorder, Task> scenario)
{
    var services = new ServiceCollection();
    _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    _ = services.AddLogging();
    _ = services.AddSingleton<InvocationRecorder>();
    _ = services.AddPulse(configure);
    _ = services.AddNetEvolvePulseTestsAotPulseHandlers();

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
