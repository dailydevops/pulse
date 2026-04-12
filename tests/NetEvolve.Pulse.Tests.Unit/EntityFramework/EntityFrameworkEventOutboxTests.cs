namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkEventOutboxTests
{
    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException(CancellationToken cancellationToken) =>
        _ = await Assert
            .That(() =>
                new EntityFrameworkOutbox<TestDbContext>(
                    null!,
                    Options.Create(new OutboxOptions()),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithNullOptions_ThrowsArgumentNullException))
            .Options;
        await using var context = new TestDbContext(options);

        _ = await Assert
            .That(() => new EntityFrameworkOutbox<TestDbContext>(context, null!, TimeProvider.System))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithNullTimeProvider_ThrowsArgumentNullException))
            .Options;
        await using var context = new TestDbContext(options);

        _ = await Assert
            .That(() => new EntityFrameworkOutbox<TestDbContext>(context, Options.Create(new OutboxOptions()), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithValidArguments_CreatesInstance))
            .Options;
        await using var context = new TestDbContext(options);

        var outbox = new EntityFrameworkOutbox<TestDbContext>(
            context,
            Options.Create(new OutboxOptions()),
            TimeProvider.System
        );

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task StoreAsync_WithNullMessage_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(StoreAsync_WithNullMessage_ThrowsArgumentNullException))
            .Options;
        await using var context = new TestDbContext(options);
        var outbox = new EntityFrameworkOutbox<TestDbContext>(
            context,
            Options.Create(new OutboxOptions()),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await outbox.StoreAsync<TestEvent>(null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithLongCorrelationId_ThrowsInvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(StoreAsync_WithLongCorrelationId_ThrowsInvalidOperationException))
            .Options;
        await using var context = new TestDbContext(options);
        var outbox = new EntityFrameworkOutbox<TestDbContext>(
            context,
            Options.Create(new OutboxOptions()),
            TimeProvider.System
        );
        var message = new TestEvent
        {
            CorrelationId = new string('x', OutboxMessageSchema.MaxLengths.CorrelationId + 1),
        };

        _ = await Assert
            .That(async () => await outbox.StoreAsync(message, cancellationToken).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
    }

    private sealed record TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
