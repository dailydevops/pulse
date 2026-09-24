using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Tests.Aot;

// NativeAOT smoke test: exercises AddPulse, the source-generated handler registrations, open-generic
// interceptors closed over reference and value types, and Send/Query/Publish. Exit code 0 means success.
var services = new ServiceCollection();
_ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
_ = services.AddLogging();
_ = services.AddSingleton<InvocationRecorder>();
_ = services.AddPulse(config => config.AddActivityAndMetrics().AddLogging());
_ = services.AddNetEvolvePulseTestsAotPulseHandlers();

var failures = new List<string>();

void Check(bool condition, string description)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {description}");
    if (!condition)
    {
        failures.Add(description);
    }
}

var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
await using (provider.ConfigureAwait(false))
{
    var scope = provider.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var recorder = scope.ServiceProvider.GetRequiredService<InvocationRecorder>();

        var order = await mediator
            .SendAsync<CreateOrderCommand, OrderResult>(new CreateOrderCommand("A-1"))
            .ConfigureAwait(false);
        Check(order == new OrderResult("A-1", Accepted: true), "command with reference-type response");

        var sum = await mediator.SendAsync<AddNumbersCommand, int>(new AddNumbersCommand(2, 3)).ConfigureAwait(false);
        Check(sum == 5, "command with value-type response");

        await mediator.SendAsync(new PingCommand()).ConfigureAwait(false);
        Check(recorder.Invocations.Contains("PingHandler"), "void command");

        var greeting = await mediator.QueryAsync<GreetingQuery, string>(new GreetingQuery("AOT")).ConfigureAwait(false);
        Check(string.Equals(greeting, "Hello, AOT!", StringComparison.Ordinal), "query");

        await mediator.PublishAsync(new OrderCreatedEvent("A-1")).ConfigureAwait(false);
        Check(recorder.Invocations.Contains("OrderCreatedHandler"), "event handler");
        Check(
            recorder.Invocations.Contains("AuditEventHandler<OrderCreatedEvent>"),
            "open-generic event handler closed by the DI container"
        );
    }
}

Console.WriteLine(failures.Count == 0 ? "Pulse NativeAOT smoke test succeeded." : "Pulse NativeAOT smoke test failed.");
return failures.Count == 0 ? 0 : 1;
