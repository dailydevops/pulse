namespace NetEvolve.Pulse.Tests.Integration.DeadLetter;

using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.DeadLetter;
using Npgsql;
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
        Action<IMediatorBuilder>? configureMediator = null,
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
                    .AddPulse(mediatorBuilder =>
                    {
                        DatabaseInitializer.Configure(mediatorBuilder, DatabaseServiceFixture);
                        configureMediator?.Invoke(mediatorBuilder);
                    })
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

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);

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

                    var pending = await management.GetPendingAsync(2, 0, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(2);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetPendingAsync_With_skip_pages_in_OccurredAt_order(CancellationToken cancellationToken) =>
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

                        // Keep OccurredAt distinct so the order is decided by OccurredAt, not the Id tie-break.
                        await Task.Delay(TimeSpan.FromMilliseconds(20), token).ConfigureAwait(false);
                    }

                    var all = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    var firstPage = await management.GetPendingAsync(2, 0, token).ConfigureAwait(false);
                    var secondPage = await management.GetPendingAsync(2, 2, token).ConfigureAwait(false);

                    _ = await Assert.That(all.Count).IsEqualTo(3);
                    _ = await Assert.That(all[0].ExceptionMessage).IsEqualTo("failure-0");
                    _ = await Assert.That(all[1].ExceptionMessage).IsEqualTo("failure-1");
                    _ = await Assert.That(all[2].ExceptionMessage).IsEqualTo("failure-2");
                    _ = await Assert.That(all[0].OccurredAt).IsLessThan(all[1].OccurredAt);
                    _ = await Assert.That(all[1].OccurredAt).IsLessThan(all[2].OccurredAt);
                    _ = await Assert.That(firstPage.Count).IsEqualTo(2);
                    _ = await Assert.That(firstPage[0].Id).IsEqualTo(all[0].Id);
                    _ = await Assert.That(firstPage[1].Id).IsEqualTo(all[1].Id);
                    _ = await Assert.That(secondPage).HasSingleItem();
                    _ = await Assert.That(secondPage[0].Id).IsEqualTo(all[2].Id);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetPendingAsync_With_negative_skip_throws(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();

                    _ = await Assert
                        .That(async () => await management.GetPendingAsync(10, -1, token).ConfigureAwait(false))
                        .Throws<ArgumentOutOfRangeException>();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetEntryAsync_Returns_stored_entry(CancellationToken cancellationToken) =>
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

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    var entryId = pending.Single().Id;

                    var entry = await management.GetEntryAsync(entryId, token).ConfigureAwait(false);

                    _ = await Assert.That(entry).IsNotNull();
                    _ = await Assert.That(entry!.Id).IsEqualTo(entryId);
                    _ = await Assert.That(entry.Status).IsEqualTo(CommandDeadLetterStatus.New);
                    _ = await Assert.That(entry.ExceptionMessage).IsEqualTo("boom");
                    _ = await Assert.That(entry.CommandType).IsEqualTo(typeof(TestReplayCommand).AssemblyQualifiedName);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetEntryAsync_When_id_not_found_returns_null(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();

                    var entry = await management.GetEntryAsync(Guid.NewGuid(), token).ConfigureAwait(false);

                    _ = await Assert.That(entry).IsNull();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetPendingAsync_With_negative_count_throws(CancellationToken cancellationToken) =>
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

                    _ = await Assert
                        .That(async () => await management.GetPendingAsync(-1, 0, token).ConfigureAwait(false))
                        .Throws<ArgumentOutOfRangeException>();
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

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    var entryId = pending.Single().Id;

                    await management.ReplayAsync(entryId, token).ConfigureAwait(false);

                    var stillPending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
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
    public async Task ReplayAsync_When_id_not_found_throws_EntryNotFound(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = Guid.NewGuid();

                    var exception = await Assert
                        .That(() => management.ReplayAsync(entryId, token))
                        .Throws<CommandDeadLetterEntryNotFoundException>();
                    _ = await Assert.That(exception!.EntryId).IsEqualTo(entryId);
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

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    var entryId = pending.Single().Id;

                    await management.DismissAsync(entryId, token).ConfigureAwait(false);

                    var stillPending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    _ = await Assert.That(stillPending).IsEmpty();

                    var stats = await management.GetStatisticsAsync(token).ConfigureAwait(false);
                    _ = await Assert.That(stats.DismissedCount).IsEqualTo(1);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task DismissAsync_When_id_not_found_throws_EntryNotFound(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = Guid.NewGuid();

                    var exception = await Assert
                        .That(() => management.DismissAsync(entryId, token))
                        .Throws<CommandDeadLetterEntryNotFoundException>();
                    _ = await Assert.That(exception!.EntryId).IsEqualTo(entryId);
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

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
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

    [Test]
    public async Task ReplayAsync_When_entry_dismissed_throws_Dismissed(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = await StoreReplayableEntryAsync(services, token).ConfigureAwait(false);
                    await management.DismissAsync(entryId, token).ConfigureAwait(false);

                    var exception = await Assert
                        .That(() => management.ReplayAsync(entryId, token))
                        .Throws<CommandDeadLetterEntryDismissedException>();
                    _ = await Assert.That(exception!.EntryId).IsEqualTo(entryId);

                    var entry = await management.GetEntryAsync(entryId, token).ConfigureAwait(false);
                    _ = await Assert.That(entry!.Status).IsEqualTo(CommandDeadLetterStatus.Dismissed);
                },
                cancellationToken,
                configureServices: services =>
                    services.AddSingleton<ICommandHandler<TestReplayCommand, Void>, FailingReplayCommandHandler>()
            )
            .ConfigureAwait(false);

    [Test]
    public async Task ReplayAsync_When_entry_resolved_replays_again(CancellationToken cancellationToken)
    {
        var handler = new CountingReplayCommandHandler();

        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = await StoreReplayableEntryAsync(services, token).ConfigureAwait(false);
                    await management.ReplayAsync(entryId, token).ConfigureAwait(false);

                    await management.ReplayAsync(entryId, token).ConfigureAwait(false);

                    var entry = await management.GetEntryAsync(entryId, token).ConfigureAwait(false);
                    _ = await Assert.That(entry!.Status).IsEqualTo(CommandDeadLetterStatus.Resolved);
                    _ = await Assert.That(handler.HandledCount).IsEqualTo(2);
                },
                cancellationToken,
                configureServices: services => services.AddSingleton<ICommandHandler<TestReplayCommand, Void>>(handler)
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task ReplayAsync_When_handler_throws_resets_entry_to_New(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = await StoreReplayableEntryAsync(services, token).ConfigureAwait(false);

                    _ = await Assert
                        .That(() => management.ReplayAsync(entryId, token))
                        .Throws<InvalidOperationException>();

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    _ = await Assert.That(pending).HasSingleItem();
                    _ = await Assert.That(pending[0].Id).IsEqualTo(entryId);

                    var stats = await management.GetStatisticsAsync(token).ConfigureAwait(false);
                    _ = await Assert.That(stats.ReplayingCount).IsEqualTo(0);
                },
                cancellationToken,
                configureServices: services =>
                    services.AddSingleton<ICommandHandler<TestReplayCommand, Void>, FailingReplayCommandHandler>()
            )
            .ConfigureAwait(false);

    [Test]
    public async Task ReplayAsync_When_cancelled_resets_entry_to_New(CancellationToken cancellationToken)
    {
        using var replayCancellation = new CancellationTokenSource();

        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = await StoreReplayableEntryAsync(services, token).ConfigureAwait(false);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, replayCancellation.Token);

                    _ = await Assert
                        .That(() => management.ReplayAsync(entryId, linked.Token))
                        .Throws<OperationCanceledException>();

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    _ = await Assert.That(pending).HasSingleItem();
                    _ = await Assert.That(pending[0].Id).IsEqualTo(entryId);

                    var stats = await management.GetStatisticsAsync(token).ConfigureAwait(false);
                    _ = await Assert.That(stats.ReplayingCount).IsEqualTo(0);
                },
                cancellationToken,
                configureServices: services =>
                    services.AddSingleton<ICommandHandler<TestReplayCommand, Void>>(
                        new CancellingReplayCommandHandler(replayCancellation)
                    )
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task ReplayAsync_When_reset_fails_rethrows_handler_exception(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = await StoreReplayableEntryAsync(services, token).ConfigureAwait(false);

                    _ = await Assert
                        .That(() => management.ReplayAsync(entryId, token))
                        .Throws<InvalidOperationException>()
                        .WithMessage("replay failed", StringComparison.Ordinal);
                },
                cancellationToken,
                configureServices: services =>
                    services.AddScoped<ICommandHandler<TestReplayCommand, Void>>(
                        sp => new TableDroppingReplayCommandHandler(token =>
                            DropDeadLetterTableAsync(
                                sp,
                                nameof(ReplayAsync_When_reset_fails_rethrows_handler_exception),
                                token
                            )
                        )
                    )
            )
            .ConfigureAwait(false);

    [Test]
    public async Task ReplayAsync_When_handler_throws_updates_existing_entry(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = await StoreReplayableEntryAsync(services, token).ConfigureAwait(false);
                    var original = await management.GetEntryAsync(entryId, token).ConfigureAwait(false);

                    // Keep OccurredAt of the replay failure distinct from the stored failure.
                    await Task.Delay(TimeSpan.FromMilliseconds(20), token).ConfigureAwait(false);

                    _ = await Assert
                        .That(() => management.ReplayAsync(entryId, token))
                        .Throws<InvalidOperationException>()
                        .WithMessage("replay failed", StringComparison.Ordinal);

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    _ = await Assert.That(pending).HasSingleItem();
                    _ = await Assert.That(pending[0].Id).IsEqualTo(entryId);
                    _ = await Assert.That(pending[0].AttemptCount).IsEqualTo(2);
                    _ = await Assert.That(pending[0].ExceptionMessage).IsEqualTo("replay failed");
                    _ = await Assert
                        .That(pending[0].ExceptionType)
                        .IsEqualTo(typeof(InvalidOperationException).AssemblyQualifiedName);
                    _ = await Assert.That(pending[0].OccurredAt).IsNotEqualTo(original!.OccurredAt);
                },
                cancellationToken,
                configureServices: services =>
                    services.AddSingleton<ICommandHandler<TestReplayCommand, Void>, FailingReplayCommandHandler>(),
                configureMediator: mediatorBuilder => mediatorBuilder.AddCommandDeadLetter()
            )
            .ConfigureAwait(false);

    [Test]
    public async Task ReplayAsync_When_exception_type_is_too_long_truncates_it(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var exceptionTypeName = LongNameFailingReplayCommandHandler.ExceptionTypeName;
                    _ = await Assert
                        .That(exceptionTypeName.Length)
                        .IsGreaterThan(CommandDeadLetterSchema.MaxLengths.ExceptionType);

                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = await StoreReplayableEntryAsync(services, token).ConfigureAwait(false);

                    _ = await Assert.That(() => management.ReplayAsync(entryId, token)).Throws<Exception>();

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    _ = await Assert.That(pending).HasSingleItem();
                    _ = await Assert.That(pending[0].AttemptCount).IsEqualTo(2);
                    _ = await Assert
                        .That(pending[0].ExceptionType)
                        .IsEqualTo(exceptionTypeName[..CommandDeadLetterSchema.MaxLengths.ExceptionType]);
                },
                cancellationToken,
                configureServices: services =>
                    services.AddSingleton<
                        ICommandHandler<TestReplayCommand, Void>,
                        LongNameFailingReplayCommandHandler
                    >()
            )
            .ConfigureAwait(false);

    [Test]
    public async Task ReplayAsync_When_replayed_twice_counts_every_attempt(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();
                    var entryId = await StoreReplayableEntryAsync(services, token).ConfigureAwait(false);

                    for (var i = 0; i < 2; i++)
                    {
                        _ = await Assert
                            .That(() => management.ReplayAsync(entryId, token))
                            .Throws<InvalidOperationException>();
                    }

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    _ = await Assert.That(pending).HasSingleItem();
                    _ = await Assert.That(pending[0].Id).IsEqualTo(entryId);
                    _ = await Assert.That(pending[0].AttemptCount).IsEqualTo(3);

                    var stats = await management.GetStatisticsAsync(token).ConfigureAwait(false);
                    _ = await Assert.That(stats.TotalCount).IsEqualTo(1);
                },
                cancellationToken,
                configureServices: services =>
                    services.AddSingleton<ICommandHandler<TestReplayCommand, Void>, FailingReplayCommandHandler>(),
                configureMediator: mediatorBuilder => mediatorBuilder.AddCommandDeadLetter()
            )
            .ConfigureAwait(false);

    [Test]
    public async Task SendAsync_When_handler_throws_stores_entry_with_one_attempt(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    var management = services.GetRequiredService<ICommandDeadLetterManagement>();

                    _ = await Assert
                        .That(() => mediator.SendAsync<TestReplayCommand, Void>(new TestReplayCommand("sent"), token))
                        .Throws<InvalidOperationException>();

                    var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
                    _ = await Assert.That(pending).HasSingleItem();
                    _ = await Assert.That(pending[0].AttemptCount).IsEqualTo(1);
                    _ = await Assert.That(pending[0].ExceptionMessage).IsEqualTo("replay failed");
                    _ = await Assert
                        .That(pending[0].CommandType)
                        .IsEqualTo(typeof(TestReplayCommand).AssemblyQualifiedName);
                },
                cancellationToken,
                configureServices: services =>
                    services.AddSingleton<ICommandHandler<TestReplayCommand, Void>, FailingReplayCommandHandler>(),
                configureMediator: mediatorBuilder => mediatorBuilder.AddCommandDeadLetter()
            )
            .ConfigureAwait(false);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The table name is the test method name."
    )]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "S2077:Formatting SQL queries is security-sensitive",
        Justification = "The table name is the test method name."
    )]
    private async Task DropDeadLetterTableAsync(IServiceProvider services, string tableName, CancellationToken token)
    {
        var contextFactory = services.GetService<
            IDbContextFactory<EntityFrameworkCommandDeadLetterInitializer.TestCommandDeadLetterDbContext>
        >();
        if (contextFactory is not null)
        {
            var context = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var entityType = context.Model.FindEntityType(typeof(CommandDeadLetterEntry))!;
                var table = context
                    .GetService<ISqlGenerationHelper>()
                    .DelimitIdentifier(entityType.GetTableName()!, entityType.GetSchema());
                var dropSql = $"DROP TABLE {table}";
                _ = await context.Database.ExecuteSqlRawAsync(dropSql, token).ConfigureAwait(false);
            }

            return;
        }

        var schema = TestHelper.TargetFramework;
        DbConnection connection = DatabaseServiceFixture.ServiceType switch
        {
            ServiceType.SQLite => new SqliteConnection(DatabaseServiceFixture.ConnectionString),
            ServiceType.SqlServer => new SqlConnection(DatabaseServiceFixture.ConnectionString),
            ServiceType.PostgreSQL => new NpgsqlConnection(DatabaseServiceFixture.ConnectionString),
            ServiceType.MySql => new MySqlConnection(DatabaseServiceFixture.ConnectionString),
            _ => throw new NotSupportedException(
                $"Database type {DatabaseServiceFixture.ServiceType} is not supported."
            ),
        };
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = DatabaseServiceFixture.ServiceType switch
                {
                    ServiceType.SqlServer => $"DROP TABLE [{schema}].[{tableName}]",
                    ServiceType.PostgreSQL => $"DROP TABLE \"{schema}\".\"{tableName}\"",
                    ServiceType.MySql => $"DROP TABLE `{tableName}`",
                    _ => $"DROP TABLE \"{tableName}\"",
                };
                _ = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
    }

    private static async Task<Guid> StoreReplayableEntryAsync(IServiceProvider services, CancellationToken token)
    {
        var store = services.GetRequiredService<ICommandDeadLetterStore>();
        var management = services.GetRequiredService<ICommandDeadLetterManagement>();
        var serializer = services.GetRequiredService<IPayloadSerializer>();

        await store
            .StoreAsync(
                typeof(TestReplayCommand).AssemblyQualifiedName!,
                serializer.Serialize(new TestReplayCommand("replay-value")),
                new NotSupportedException("boom"),
                token
            )
            .ConfigureAwait(false);

        var pending = await management.GetPendingAsync(50, 0, token).ConfigureAwait(false);
        return pending.Single().Id;
    }

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

    private sealed class CountingReplayCommandHandler : ICommandHandler<TestReplayCommand, Void>
    {
        private int _handledCount;

        public int HandledCount => Volatile.Read(ref _handledCount);

        public Task<Void> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default)
        {
            _ = Interlocked.Increment(ref _handledCount);
            return Task.FromResult(Void.Completed);
        }
    }

    private sealed class FailingReplayCommandHandler : ICommandHandler<TestReplayCommand, Void>
    {
        public Task<Void> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("replay failed");
    }

    private sealed class LongNameFailingReplayCommandHandler : ICommandHandler<TestReplayCommand, Void>
    {
        public static string ExceptionTypeName =>
            typeof(ReplayException<
                Dictionary<Dictionary<string, Guid>, Dictionary<string, Guid>>
            >).AssemblyQualifiedName!;

        public Task<Void> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default) =>
            throw new ReplayException<Dictionary<Dictionary<string, Guid>, Dictionary<string, Guid>>>();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S2326:Unused type parameters should be removed",
        Justification = "The type argument only lengthens the assembly-qualified exception type name."
    )]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3871:Exception types should be \"public\"",
        Justification = "Test-only exception that never leaves the test."
    )]
    private sealed class ReplayException<T> : Exception
    {
        public ReplayException() { }

        public ReplayException(string message)
            : base(message) { }

        public ReplayException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    private sealed class TableDroppingReplayCommandHandler(Func<CancellationToken, Task> dropTable)
        : ICommandHandler<TestReplayCommand, Void>
    {
        public async Task<Void> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default)
        {
            await dropTable(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("replay failed");
        }
    }

    private sealed class CancellingReplayCommandHandler(CancellationTokenSource replayCancellation)
        : ICommandHandler<TestReplayCommand, Void>
    {
        public async Task<Void> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default)
        {
            await replayCancellation.CancelAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Void.Completed;
        }
    }
}
