namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkCommandDeadLetterManagementTests
{
    private static TestCommandDeadLetterDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestCommandDeadLetterDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new TestCommandDeadLetterDbContext(options);
    }

    private static CommandDeadLetterEntry CreateEntry(
        CommandDeadLetterStatus status,
        DateTimeOffset occurredAt,
        string commandType = "Some.Command",
        string payload = "{}"
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            Payload = payload,
            OccurredAt = occurredAt,
            AttemptCount = 1,
            Status = status,
        };

    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException()
    {
        var mediator = new NoOpMediator();
        var serializer = new PassthroughPayloadSerializer();

        _ = await Assert
            .That(() =>
                new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                    null!,
                    mediator,
                    serializer
                )
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullMediator_ThrowsArgumentNullException()
    {
        var context = CreateContext(nameof(Constructor_WithNullMediator_ThrowsArgumentNullException));
        await using (context.ConfigureAwait(false))
        {
            var serializer = new PassthroughPayloadSerializer();

            _ = await Assert
                .That(() =>
                    new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                        context,
                        null!,
                        serializer
                    )
                )
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task Constructor_WithNullPayloadSerializer_ThrowsArgumentNullException()
    {
        var context = CreateContext(nameof(Constructor_WithNullPayloadSerializer_ThrowsArgumentNullException));
        await using (context.ConfigureAwait(false))
        {
            var mediator = new NoOpMediator();

            _ = await Assert
                .That(() =>
                    new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                        context,
                        mediator,
                        null!
                    )
                )
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task GetPendingAsync_ReturnsOnlyNewStatusEntries_OrderedByOccurredAt(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(nameof(GetPendingAsync_ReturnsOnlyNewStatusEntries_OrderedByOccurredAt));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var newest = CreateEntry(CommandDeadLetterStatus.New, now);
            var oldest = CreateEntry(CommandDeadLetterStatus.New, now.AddMinutes(-10));
            var resolved = CreateEntry(CommandDeadLetterStatus.Resolved, now.AddMinutes(-20));

            await context
                .CommandDeadLetterEntries.AddRangeAsync([newest, oldest, resolved], cancellationToken)
                .ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            var pending = await management.GetPendingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(pending).HasCount(2);
                _ = await Assert.That(pending[0].Id).IsEqualTo(oldest.Id);
                _ = await Assert.That(pending[1].Id).IsEqualTo(newest.Id);
            }
        }
    }

    [Test]
    public async Task GetPendingAsync_HonorsCountParameter(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetPendingAsync_HonorsCountParameter));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var entries = new List<CommandDeadLetterEntry>();
            for (var i = 0; i < 5; i++)
            {
                entries.Add(CreateEntry(CommandDeadLetterStatus.New, now.AddMinutes(-i)));
            }

            await context.CommandDeadLetterEntries.AddRangeAsync(entries, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            var pending = await management.GetPendingAsync(2, 0, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(pending).HasCount(2);
        }
    }

    [Test]
    public async Task GetPendingAsync_WithSkip_SkipsOldestEntries(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetPendingAsync_WithSkip_SkipsOldestEntries));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var oldest = CreateEntry(CommandDeadLetterStatus.New, now.AddMinutes(-30));
            var middle = CreateEntry(CommandDeadLetterStatus.New, now.AddMinutes(-20));
            var newest = CreateEntry(CommandDeadLetterStatus.New, now.AddMinutes(-10));

            await context
                .CommandDeadLetterEntries.AddRangeAsync([newest, oldest, middle], cancellationToken)
                .ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            var pending = await management.GetPendingAsync(1, 1, cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(pending).HasSingleItem();
                _ = await Assert.That(pending[0].Id).IsEqualTo(middle.Id);
            }
        }
    }

    [Test]
    public async Task GetPendingAsync_WithNegativeSkip_ThrowsArgumentOutOfRangeException(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(nameof(GetPendingAsync_WithNegativeSkip_ThrowsArgumentOutOfRangeException));
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            _ = await Assert
                .That(async () => await management.GetPendingAsync(10, -1, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task GetEntryAsync_WithExistingId_ReturnsEntry(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetEntryAsync_WithExistingId_ReturnsEntry));
        await using (context.ConfigureAwait(false))
        {
            var entry = CreateEntry(CommandDeadLetterStatus.Dismissed, DateTimeOffset.UtcNow);
            _ = await context.CommandDeadLetterEntries.AddAsync(entry, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            var result = await management.GetEntryAsync(entry.Id, cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsNotNull();
                _ = await Assert.That(result!.Id).IsEqualTo(entry.Id);
                _ = await Assert.That(result.Status).IsEqualTo(CommandDeadLetterStatus.Dismissed);
            }
        }
    }

    [Test]
    public async Task GetEntryAsync_WithUnknownId_ReturnsNull(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetEntryAsync_WithUnknownId_ReturnsNull));
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            var result = await management.GetEntryAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsNull();
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task GetPendingAsync_WithNonPositiveCount_ThrowsArgumentOutOfRangeException(
        int count,
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(
            $"{nameof(GetPendingAsync_WithNonPositiveCount_ThrowsArgumentOutOfRangeException)}{count}"
        );
        await using (context.ConfigureAwait(false))
        {
            _ = await context
                .CommandDeadLetterEntries.AddAsync(
                    CreateEntry(CommandDeadLetterStatus.New, DateTimeOffset.UtcNow),
                    cancellationToken
                )
                .ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            _ = await Assert
                .That(async () => await management.GetPendingAsync(count, 0, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task ReplayAsync_WithUnknownId_ThrowsEntryNotFoundException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(ReplayAsync_WithUnknownId_ThrowsEntryNotFoundException));
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            _ = await Assert
                .That(async () => await management.ReplayAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false))
                .Throws<CommandDeadLetterEntryNotFoundException>();
        }
    }

    [Test]
    public async Task ReplayAsync_ExecutesHandlerAndResolvesEntry(CancellationToken cancellationToken)
    {
        var handler = new TestReplayCommandHandler();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        _ = services.AddScoped<ICommandHandler<TestReplayCommand, string>>(_ => handler);
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var scope = provider.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();
                var payloadSerializer = scope.ServiceProvider.GetRequiredService<IPayloadSerializer>();

                var context = CreateContext(nameof(ReplayAsync_ExecutesHandlerAndResolvesEntry));
                await using (context.ConfigureAwait(false))
                {
                    var command = new TestReplayCommand { OrderId = 42 };
                    var payload = payloadSerializer.Serialize(command);
                    var entry = CreateEntry(
                        CommandDeadLetterStatus.New,
                        DateTimeOffset.UtcNow,
                        typeof(TestReplayCommand).AssemblyQualifiedName!,
                        payload
                    );
                    _ = await context.CommandDeadLetterEntries.AddAsync(entry, cancellationToken);
                    _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                        context,
                        mediator,
                        payloadSerializer
                    );

                    await management.ReplayAsync(entry.Id, cancellationToken).ConfigureAwait(false);

                    var reloaded = await context
                        .CommandDeadLetterEntries.SingleAsync(e => e.Id == entry.Id, cancellationToken)
                        .ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(reloaded.Status).IsEqualTo(CommandDeadLetterStatus.Resolved);
                        _ = await Assert.That(handler.HandledCommands).HasSingleItem();
                        _ = await Assert.That(handler.HandledCommands[0].OrderId).IsEqualTo(42);
                    }
                }
            }
        }
    }

    [Test]
    public async Task ReplayAsync_WithDismissedEntry_ThrowsDismissedExceptionAndKeepsStatus(
        CancellationToken cancellationToken
    )
    {
        var handler = new TestReplayCommandHandler();
        var databaseName = nameof(ReplayAsync_WithDismissedEntry_ThrowsDismissedExceptionAndKeepsStatus);

        var entryId = await ReplayWithHandlerAsync(
                databaseName,
                _ => handler,
                CommandDeadLetterStatus.Dismissed,
                async (management, id) =>
                {
                    var exception = await Assert
                        .That(async () => await management.ReplayAsync(id, cancellationToken).ConfigureAwait(false))
                        .Throws<CommandDeadLetterEntryDismissedException>();
                    _ = await Assert.That(exception!.EntryId).IsEqualTo(id);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(await GetStatusAsync(databaseName, entryId, cancellationToken).ConfigureAwait(false))
                .IsEqualTo(CommandDeadLetterStatus.Dismissed);
            _ = await Assert.That(handler.HandledCommands).IsEmpty();
        }
    }

    [Test]
    public async Task ReplayAsync_WithResolvedEntry_ReplaysAgain(CancellationToken cancellationToken)
    {
        var handler = new TestReplayCommandHandler();
        var databaseName = nameof(ReplayAsync_WithResolvedEntry_ReplaysAgain);

        var entryId = await ReplayWithHandlerAsync(
                databaseName,
                _ => handler,
                CommandDeadLetterStatus.Resolved,
                (management, id) => management.ReplayAsync(id, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(await GetStatusAsync(databaseName, entryId, cancellationToken).ConfigureAwait(false))
                .IsEqualTo(CommandDeadLetterStatus.Resolved);
            _ = await Assert.That(handler.HandledCommands).HasSingleItem();
        }
    }

    [Test]
    public async Task ReplayAsync_WhenHandlerThrows_ResetsEntryToNew(CancellationToken cancellationToken)
    {
        var databaseName = nameof(ReplayAsync_WhenHandlerThrows_ResetsEntryToNew);

        var entryId = await ReplayWithHandlerAsync(
                databaseName,
                _ => new FailingReplayCommandHandler(),
                CommandDeadLetterStatus.New,
                async (management, id) =>
                    _ = await Assert
                        .That(async () => await management.ReplayAsync(id, cancellationToken).ConfigureAwait(false))
                        .Throws<InvalidOperationException>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert
            .That(await GetStatusAsync(databaseName, entryId, cancellationToken).ConfigureAwait(false))
            .IsEqualTo(CommandDeadLetterStatus.New);
    }

    [Test]
    public async Task ReplayAsync_WhenCancelledDuringDispatch_ResetsEntryToNew(CancellationToken cancellationToken)
    {
        var databaseName = nameof(ReplayAsync_WhenCancelledDuringDispatch_ResetsEntryToNew);
        using var replayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var entryId = await ReplayWithHandlerAsync(
                databaseName,
                _ => new CancellingReplayCommandHandler(replayCancellation),
                CommandDeadLetterStatus.New,
                async (management, id) =>
                    _ = await Assert
                        .That(async () =>
                            await management.ReplayAsync(id, replayCancellation.Token).ConfigureAwait(false)
                        )
                        .Throws<OperationCanceledException>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert
            .That(await GetStatusAsync(databaseName, entryId, cancellationToken).ConfigureAwait(false))
            .IsEqualTo(CommandDeadLetterStatus.New);
    }

    [Test]
    public async Task ReplayAsync_WhenHandlerThrowsWithPendingChanges_DiscardsHandlerChanges(
        CancellationToken cancellationToken
    )
    {
        var databaseName = nameof(ReplayAsync_WhenHandlerThrowsWithPendingChanges_DiscardsHandlerChanges);
        var handlerEntry = CreateEntry(CommandDeadLetterStatus.New, DateTimeOffset.UtcNow);

        var entryId = await ReplayWithHandlerAsync(
                databaseName,
                context => new DirtyingFailingReplayCommandHandler(context, handlerEntry),
                CommandDeadLetterStatus.New,
                async (management, id) =>
                    _ = await Assert
                        .That(async () => await management.ReplayAsync(id, cancellationToken).ConfigureAwait(false))
                        .Throws<InvalidOperationException>()
                        .WithMessage("replay failed", StringComparison.Ordinal),
                cancellationToken
            )
            .ConfigureAwait(false);

        var context = CreateContext(databaseName);
        await using (context.ConfigureAwait(false))
        {
            var ids = await context
                .CommandDeadLetterEntries.AsNoTracking()
                .Select(e => e.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert
                    .That(await GetStatusAsync(databaseName, entryId, cancellationToken).ConfigureAwait(false))
                    .IsEqualTo(CommandDeadLetterStatus.New);
                _ = await Assert.That(ids).DoesNotContain(handlerEntry.Id);
            }
        }
    }

    [Test]
    public async Task ReplayAsync_WhenHandlerThrowsAndResetFails_RethrowsHandlerException(
        CancellationToken cancellationToken
    )
    {
        var databaseName = nameof(ReplayAsync_WhenHandlerThrowsAndResetFails_RethrowsHandlerException);

        _ = await ReplayWithHandlerAsync(
                databaseName,
                _ => new RowDeletingFailingReplayCommandHandler(databaseName),
                CommandDeadLetterStatus.New,
                async (management, id) =>
                    _ = await Assert
                        .That(async () => await management.ReplayAsync(id, cancellationToken).ConfigureAwait(false))
                        .Throws<InvalidOperationException>()
                        .WithMessage("replay failed", StringComparison.Ordinal),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task DismissAsync_WithUnknownId_ThrowsEntryNotFoundException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(DismissAsync_WithUnknownId_ThrowsEntryNotFoundException));
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            _ = await Assert
                .That(async () =>
                    await management.DismissAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false)
                )
                .Throws<CommandDeadLetterEntryNotFoundException>();
        }
    }

    [Test]
    public async Task DismissAsync_SetsStatusToDismissed(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(DismissAsync_SetsStatusToDismissed));
        await using (context.ConfigureAwait(false))
        {
            var entry = CreateEntry(CommandDeadLetterStatus.New, DateTimeOffset.UtcNow);
            _ = await context.CommandDeadLetterEntries.AddAsync(entry, cancellationToken);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            await management.DismissAsync(entry.Id, cancellationToken).ConfigureAwait(false);

            var reloaded = await context
                .CommandDeadLetterEntries.SingleAsync(e => e.Id == entry.Id, cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(reloaded.Status).IsEqualTo(CommandDeadLetterStatus.Dismissed);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_ReturnsCorrectCountsPerStatus(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetStatisticsAsync_ReturnsCorrectCountsPerStatus));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var entries = new[]
            {
                CreateEntry(CommandDeadLetterStatus.New, now),
                CreateEntry(CommandDeadLetterStatus.New, now),
                CreateEntry(CommandDeadLetterStatus.Replaying, now),
                CreateEntry(CommandDeadLetterStatus.Resolved, now),
                CreateEntry(CommandDeadLetterStatus.Resolved, now),
                CreateEntry(CommandDeadLetterStatus.Resolved, now),
                CreateEntry(CommandDeadLetterStatus.Dismissed, now),
            };

            await context.CommandDeadLetterEntries.AddRangeAsync(entries, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            var statistics = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(statistics.NewCount).IsEqualTo(2);
                _ = await Assert.That(statistics.ReplayingCount).IsEqualTo(1);
                _ = await Assert.That(statistics.ResolvedCount).IsEqualTo(3);
                _ = await Assert.That(statistics.DismissedCount).IsEqualTo(1);
                _ = await Assert.That(statistics.TotalCount).IsEqualTo(7);
            }
        }
    }

    [Test]
    public async Task GetStatisticsAsync_EmptyDatabase_ReturnsAllZero(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetStatisticsAsync_EmptyDatabase_ReturnsAllZero));
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            var statistics = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(statistics.TotalCount).IsEqualTo(0);
        }
    }

    private static async Task<Guid> ReplayWithHandlerAsync(
        string databaseName,
        Func<TestCommandDeadLetterDbContext, ICommandHandler<TestReplayCommand, string>> createHandler,
        CommandDeadLetterStatus initialStatus,
        Func<EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>, Guid, Task> replay,
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(databaseName);
        await using (context.ConfigureAwait(false))
        {
            var services = new ServiceCollection();
            _ = services.AddLogging();
            _ = services.AddPulse();
            _ = services.AddScoped(_ => createHandler(context));
            var provider = services.BuildServiceProvider();
            await using (provider.ConfigureAwait(false))
            {
                var scope = provider.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();
                    var payloadSerializer = scope.ServiceProvider.GetRequiredService<IPayloadSerializer>();

                    var entry = CreateEntry(
                        initialStatus,
                        DateTimeOffset.UtcNow,
                        typeof(TestReplayCommand).AssemblyQualifiedName!,
                        payloadSerializer.Serialize(new TestReplayCommand { OrderId = 42 })
                    );
                    _ = await context.CommandDeadLetterEntries.AddAsync(entry, cancellationToken);
                    _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                        context,
                        mediator,
                        payloadSerializer
                    );

                    await replay(management, entry.Id).ConfigureAwait(false);

                    return entry.Id;
                }
            }
        }
    }

    private static async Task<CommandDeadLetterStatus> GetStatusAsync(
        string databaseName,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(databaseName);
        await using (context.ConfigureAwait(false))
        {
            var entry = await context
                .CommandDeadLetterEntries.AsNoTracking()
                .SingleAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);
            return entry.Status;
        }
    }

    private sealed class FailingReplayCommandHandler : ICommandHandler<TestReplayCommand, string>
    {
        public Task<string> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("replay failed");
    }

    private sealed class RowDeletingFailingReplayCommandHandler(string databaseName)
        : ICommandHandler<TestReplayCommand, string>
    {
        public async Task<string> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default)
        {
            var context = CreateContext(databaseName);
            await using (context.ConfigureAwait(false))
            {
                context.CommandDeadLetterEntries.RemoveRange(
                    await context.CommandDeadLetterEntries.ToListAsync(cancellationToken).ConfigureAwait(false)
                );
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("replay failed");
        }
    }

    private sealed class DirtyingFailingReplayCommandHandler(
        TestCommandDeadLetterDbContext context,
        CommandDeadLetterEntry handlerEntry
    ) : ICommandHandler<TestReplayCommand, string>
    {
        public async Task<string> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default)
        {
            _ = await context.CommandDeadLetterEntries.AddAsync(handlerEntry, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("replay failed");
        }
    }

    private sealed class CancellingReplayCommandHandler(CancellationTokenSource replayCancellation)
        : ICommandHandler<TestReplayCommand, string>
    {
        public async Task<string> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default)
        {
            await replayCancellation.CancelAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return "handled";
        }
    }

    private sealed class NoOpMediator : IMediatorSendOnly
    {
        public Task PublishAsync<TEvent>([NotNull] TEvent message, CancellationToken cancellationToken = default)
            where TEvent : IEvent => Task.CompletedTask;

        public Task<TResponse> SendAsync<TCommand, TResponse>(
            [NotNull] TCommand command,
            CancellationToken cancellationToken = default
        )
            where TCommand : ICommand<TResponse> => Task.FromResult(default(TResponse)!);
    }

    private sealed class PassthroughPayloadSerializer : IPayloadSerializer
    {
        public string Serialize<T>(T value) => value?.ToString() ?? string.Empty;

        public string Serialize(object value, Type type) => value.ToString() ?? string.Empty;

        public byte[] SerializeToBytes<T>(T value) => [];

        public T? Deserialize<T>(string payload) => default;

        public T? Deserialize<T>(byte[] payload) => default;
    }

    private sealed class TestReplayCommand : ICommand<string>
    {
        public int OrderId { get; set; }

        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class TestReplayCommandHandler : ICommandHandler<TestReplayCommand, string>
    {
        public List<TestReplayCommand> HandledCommands { get; } = [];

        public Task<string> HandleAsync(TestReplayCommand command, CancellationToken cancellationToken = default)
        {
            HandledCommands.Add(command);
            return Task.FromResult("handled");
        }
    }
}
