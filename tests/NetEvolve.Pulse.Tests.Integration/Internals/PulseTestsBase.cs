namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

public abstract class PulseTestsBase
{
    protected IServiceFixture DatabaseServiceFixture { get; }
    protected IDatabaseInitializer DatabaseInitializer { get; }

    protected static DateTimeOffset TestDateTime { get; } = new DateTimeOffset(2025, 1, 1, 12, 0, 0, 0, TimeSpan.Zero);

    protected PulseTestsBase(IServiceFixture databaseServiceFixture, IDatabaseInitializer databaseInitializer)
    {
        DatabaseServiceFixture = databaseServiceFixture;
        DatabaseInitializer = databaseInitializer;
    }

    protected async ValueTask RunAndVerify(
        Func<IServiceProvider, CancellationToken, Task> testableCode,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null,
        [CallerMemberName] string tableName = null!
    )
    {
        ArgumentNullException.ThrowIfNull(testableCode);

        using var host = new HostBuilder()
            .ConfigureAppConfiguration((hostContext, configBuilder) => { })
            .ConfigureServices(services =>
            {
                DatabaseInitializer.Initialize(services, DatabaseServiceFixture);
                configureServices?.Invoke(services);
                _ = services
                    .AddPulse(mediatorBuilder => DatabaseInitializer.Configure(mediatorBuilder, DatabaseServiceFixture))
                    .Configure<OutboxOptions>(options =>
                    {
                        options.TableName = tableName;
                        options.Schema = TestHelper.TargetFramework;
                    });
            })
            .ConfigureWebHost(webBuilder => _ = webBuilder.UseTestServer().Configure(applicationBuilder => { }))
            .Build();

        await DatabaseInitializer.CreateDatabaseAsync(host.Services, cancellationToken).ConfigureAwait(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        using var server = host.GetTestServer();

        using (Assert.Multiple())
        {
            await using var scope = server.Services.CreateAsyncScope();
            await testableCode.Invoke(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    protected static Task[] PublishEvents<TEvent>(IMediator mediator, int count, Func<int, TEvent> eventFactory)
        where TEvent : IEvent => [.. Enumerable.Range(0, count).Select(x => mediator.PublishAsync(eventFactory(x)))];

    protected static async Task PublishEventsAsync<TEvent>(
        IMediator mediator,
        int count,
        Func<int, TEvent> eventFactory,
        CancellationToken cancellationToken = default
    )
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(eventFactory);

        for (var i = 0; i < count; i++)
        {
            await mediator.PublishAsync(eventFactory(i), cancellationToken).ConfigureAwait(false);
        }
    }
}
