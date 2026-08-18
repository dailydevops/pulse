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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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

            var pending = await management.GetPendingAsync(2, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(pending).HasCount(2);
        }
    }

    [Test]
    public async Task ReplayAsync_WithUnknownId_ThrowsKeyNotFoundException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = CreateContext(nameof(ReplayAsync_WithUnknownId_ThrowsKeyNotFoundException));
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkCommandDeadLetterManagement<TestCommandDeadLetterDbContext>(
                context,
                new NoOpMediator(),
                new PassthroughPayloadSerializer()
            );

            _ = await Assert
                .That(async () => await management.ReplayAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false))
                .Throws<KeyNotFoundException>();
        }
    }

    [Test]
    public async Task ReplayAsync_ExecutesHandlerAndResolvesEntry(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
    public async Task DismissAsync_WithUnknownId_ThrowsKeyNotFoundException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = CreateContext(nameof(DismissAsync_WithUnknownId_ThrowsKeyNotFoundException));
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
                .Throws<KeyNotFoundException>();
        }
    }

    [Test]
    public async Task DismissAsync_SetsStatusToDismissed(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
            cancellationToken.ThrowIfCancellationRequested();

            HandledCommands.Add(command);
            return Task.FromResult("handled");
        }
    }
}
