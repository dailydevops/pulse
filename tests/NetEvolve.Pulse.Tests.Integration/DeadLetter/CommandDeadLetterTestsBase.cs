namespace NetEvolve.Pulse.Tests.Integration.DeadLetter;

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Tests.Integration.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("DeadLetter")]
[Timeout(300_000)] // Increased timeout to accommodate potential delays in CI environments.
public abstract class CommandDeadLetterTestsBase(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
)
{
    protected IServiceFixture DatabaseServiceFixture { get; } = databaseServiceFixture;
    protected IServiceInitializer DatabaseInitializer { get; } = databaseInitializer;

    protected async ValueTask RunAndVerify(
        Func<IServiceProvider, CancellationToken, Task> testableCode,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null,
        [CallerMemberName] string tableName = null!
    )
    {
        ArgumentNullException.ThrowIfNull(testableCode);

        cancellationToken.ThrowIfCancellationRequested();

        using var host = new HostBuilder()
            .ConfigureAppConfiguration((hostContext, configBuilder) => { })
            .ConfigureServices(services =>
            {
                DatabaseInitializer.Initialize(services, DatabaseServiceFixture);
                configureServices?.Invoke(services);
                _ = services
                    .AddPulse(mediatorBuilder => DatabaseInitializer.Configure(mediatorBuilder, DatabaseServiceFixture))
                    .Configure<CommandDeadLetterOptions>(options =>
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
            var scope = server.Services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await testableCode.Invoke(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task StoreAsync_Then_GetPendingAsync_Returns_entry_with_status_New(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<ICommandDeadLetterStore>();
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();

                    await store
                        .StoreAsync(
                            typeof(TestReplayCommand).AssemblyQualifiedName!,
                            """{"Value":"n/a"}""",
                            new InvalidOperationException("boom"),
                            token
                        )
                        .ConfigureAwait(false);

                    var pending = await management.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(pending).HasSingleItem();
                    _ = await Assert.That(pending[0].Status).IsEqualTo(CommandDeadLetterStatus.New);
                    _ = await Assert.That(pending[0].ExceptionMessage).IsEqualTo("boom");
                    _ = await Assert
                        .That(pending[0].CommandType)
                        .IsEqualTo(typeof(TestReplayCommand).AssemblyQualifiedName);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetPendingAsync_Respects_count_limit(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<ICommandDeadLetterStore>();
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();

                    for (var i = 0; i < 3; i++)
                    {
                        await store
                            .StoreAsync(
                                typeof(TestReplayCommand).AssemblyQualifiedName!,
                                """{"Value":"n/a"}""",
                                new InvalidOperationException($"failure-{i}"),
                                token
                            )
                            .ConfigureAwait(false);
                    }

                    var pending = await management.GetPendingAsync(2, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(2);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task ReplayAsync_Dispatches_command_and_sets_Resolved(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<ICommandDeadLetterStore>();
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var serializer = services.GetRequiredService<IPayloadSerializer>();

                    var command = new TestReplayCommand("replay-value");
                    var payload = serializer.Serialize(command);
                    await store
                        .StoreAsync(
                            typeof(TestReplayCommand).AssemblyQualifiedName!,
                            payload,
                            new InvalidOperationException("boom"),
                            token
                        )
                        .ConfigureAwait(false);

                    var pending = await management.GetPendingAsync(50, token).ConfigureAwait(false);
                    var entryId = pending.Single().Id;

                    await management.ReplayAsync(entryId, token).ConfigureAwait(false);

                    var stillPending = await management.GetPendingAsync(50, token).ConfigureAwait(false);
                    _ = await Assert.That(stillPending).IsEmpty();

                    var stats = await management.GetStatisticsAsync(token).ConfigureAwait(false);
                    _ = await Assert.That(stats.ResolvedCount).IsEqualTo(1);
                },
                cancellationToken,
                configureServices: services =>
                    services.AddSingleton<ICommandHandler<TestReplayCommand, Void>, TestReplayCommandHandler>()
            )
            .ConfigureAwait(false);

    [Test]
    public async Task ReplayAsync_When_id_not_found_throws_KeyNotFoundException(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();

                    _ = await Assert
                        .That(() => management.ReplayAsync(Guid.NewGuid(), token))
                        .Throws<KeyNotFoundException>();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task DismissAsync_Sets_status_Dismissed(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<ICommandDeadLetterStore>();
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();

                    await store
                        .StoreAsync(
                            typeof(TestReplayCommand).AssemblyQualifiedName!,
                            """{"Value":"n/a"}""",
                            new InvalidOperationException("boom"),
                            token
                        )
                        .ConfigureAwait(false);

                    var pending = await management.GetPendingAsync(50, token).ConfigureAwait(false);
                    var entryId = pending.Single().Id;

                    await management.DismissAsync(entryId, token).ConfigureAwait(false);

                    var stillPending = await management.GetPendingAsync(50, token).ConfigureAwait(false);
                    _ = await Assert.That(stillPending).IsEmpty();

                    var stats = await management.GetStatisticsAsync(token).ConfigureAwait(false);
                    _ = await Assert.That(stats.DismissedCount).IsEqualTo(1);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task DismissAsync_When_id_not_found_throws_KeyNotFoundException(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();

                    _ = await Assert
                        .That(() => management.DismissAsync(Guid.NewGuid(), token))
                        .Throws<KeyNotFoundException>();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetStatisticsAsync_Returns_correct_counts_per_status(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<ICommandDeadLetterStore>();
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();

                    // Two New entries, one of which is dismissed afterward.
                    await store
                        .StoreAsync(
                            typeof(TestReplayCommand).AssemblyQualifiedName!,
                            """{"Value":"n/a"}""",
                            new InvalidOperationException("boom-1"),
                            token
                        )
                        .ConfigureAwait(false);
                    await store
                        .StoreAsync(
                            typeof(TestReplayCommand).AssemblyQualifiedName!,
                            """{"Value":"n/a"}""",
                            new InvalidOperationException("boom-2"),
                            token
                        )
                        .ConfigureAwait(false);

                    var pending = await management.GetPendingAsync(50, token).ConfigureAwait(false);
                    await management.DismissAsync(pending[0].Id, token).ConfigureAwait(false);

                    var stats = await management.GetStatisticsAsync(token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(stats.NewCount).IsEqualTo(1);
                        _ = await Assert.That(stats.DismissedCount).IsEqualTo(1);
                        _ = await Assert.That(stats.ResolvedCount).IsEqualTo(0);
                        _ = await Assert.That(stats.ReplayingCount).IsEqualTo(0);
                        _ = await Assert.That(stats.TotalCount).IsEqualTo(2);
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    private sealed record TestReplayCommand(string Value) : ICommand<Void>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class TestReplayCommandHandler : ICommandHandler<TestReplayCommand, Void>
    {
        public Task<Void> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(Void.Completed);
    }
}
