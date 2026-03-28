namespace NetEvolve.Pulse.EntityFramework.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

public sealed class EntityFrameworkOutboxManagementTests
{
    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new EntityFrameworkOutboxManagement<TestDbContext>(null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithNullTimeProvider_ThrowsArgumentNullException))
            .Options;
        await using var context = new TestDbContext(options);

        _ = await Assert
            .That(() => new EntityFrameworkOutboxManagement<TestDbContext>(context, null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithValidArguments_CreatesInstance))
            .Options;
        await using var context = new TestDbContext(options);

        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(
                nameof(GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException)
            )
            .Options;
        await using var context = new TestDbContext(options);
        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithZeroPageSize_ThrowsArgumentOutOfRangeException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_WithZeroPageSize_ThrowsArgumentOutOfRangeException))
            .Options;
        await using var context = new TestDbContext(options);
        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: 0).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePage_ThrowsArgumentOutOfRangeException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_WithNegativePage_ThrowsArgumentOutOfRangeException))
            .Options;
        await using var context = new TestDbContext(options);
        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(page: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_EmptyDatabase_ReturnsEmptyList))
            .Options;
        await using var context = new TestDbContext(options);
        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var result = await management.GetDeadLetterMessagesAsync().ConfigureAwait(false);

        _ = await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithDeadLetterMessages_ReturnsMessages()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_WithDeadLetterMessages_ReturnsMessages))
            .Options;
        await using var context = new TestDbContext(options);
        var now = DateTimeOffset.UtcNow;
        _ = context.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "TestEvent",
                Payload = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                Status = OutboxMessageStatus.DeadLetter,
            }
        );
        _ = context.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "TestEvent",
                Payload = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                Status = OutboxMessageStatus.Pending,
            }
        );
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var result = await management.GetDeadLetterMessagesAsync().ConfigureAwait(false);

        _ = await Assert.That(result).HasCount().EqualTo(1);
        _ = await Assert.That(result[0].Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithExistingDeadLetterMessage_ReturnsMessage()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessageAsync_WithExistingDeadLetterMessage_ReturnsMessage))
            .Options;
        await using var context = new TestDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();
        _ = context.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = messageId,
                EventType = "TestEvent",
                Payload = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                Status = OutboxMessageStatus.DeadLetter,
            }
        );
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var result = await management.GetDeadLetterMessageAsync(messageId).ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(messageId);
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithNonDeadLetterMessage_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessageAsync_WithNonDeadLetterMessage_ReturnsNull))
            .Options;
        await using var context = new TestDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();
        _ = context.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = messageId,
                EventType = "TestEvent",
                Payload = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                Status = OutboxMessageStatus.Completed,
            }
        );
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var result = await management.GetDeadLetterMessageAsync(messageId).ConfigureAwait(false);

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithUnknownId_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessageAsync_WithUnknownId_ReturnsNull))
            .Options;
        await using var context = new TestDbContext(options);
        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var result = await management.GetDeadLetterMessageAsync(Guid.NewGuid()).ConfigureAwait(false);

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetDeadLetterCountAsync_EmptyDatabase_ReturnsZero()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterCountAsync_EmptyDatabase_ReturnsZero))
            .Options;
        await using var context = new TestDbContext(options);
        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var count = await management.GetDeadLetterCountAsync().ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(0L);
    }

    [Test]
    public async Task GetDeadLetterCountAsync_WithDeadLetterMessages_ReturnsCorrectCount()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterCountAsync_WithDeadLetterMessages_ReturnsCorrectCount))
            .Options;
        await using var context = new TestDbContext(options);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            _ = context.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = "TestEvent",
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Status = OutboxMessageStatus.DeadLetter,
                }
            );
        }

        _ = context.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "TestEvent",
                Payload = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                Status = OutboxMessageStatus.Pending,
            }
        );
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var count = await management.GetDeadLetterCountAsync().ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(3L);
    }

    [Test]
    [Skip(
        "The EF Core InMemory provider does not support ExecuteUpdateAsync (bulk updates). Covered by integration tests."
    )]
    public async Task ReplayMessageAsync_WithExistingDeadLetterMessage_ReturnsTrueAndResetsMessage()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayMessageAsync_WithExistingDeadLetterMessage_ReturnsTrueAndResetsMessage))
            .Options;
        await using var context = new TestDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();
        _ = context.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = messageId,
                EventType = "TestEvent",
                Payload = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                RetryCount = 5,
                Error = "Some error",
                Status = OutboxMessageStatus.DeadLetter,
            }
        );
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var result = await management.ReplayMessageAsync(messageId).ConfigureAwait(false);

        _ = await Assert.That(result).IsTrue();

        var message = await context.OutboxMessages.FindAsync(messageId).ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(message!.Status).IsEqualTo(OutboxMessageStatus.Pending);
            _ = await Assert.That(message.RetryCount).IsEqualTo(0);
            _ = await Assert.That(message.Error).IsNull();
        }
    }

    [Test]
    [Skip(
        "The EF Core InMemory provider does not support ExecuteUpdateAsync (bulk updates). Covered by integration tests."
    )]
    public async Task ReplayMessageAsync_WithNonDeadLetterMessage_ReturnsFalse()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayMessageAsync_WithNonDeadLetterMessage_ReturnsFalse))
            .Options;
        await using var context = new TestDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();
        _ = context.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = messageId,
                EventType = "TestEvent",
                Payload = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                Status = OutboxMessageStatus.Failed,
            }
        );
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var result = await management.ReplayMessageAsync(messageId).ConfigureAwait(false);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    [Skip(
        "The EF Core InMemory provider does not support ExecuteUpdateAsync (bulk updates). Covered by integration tests."
    )]
    public async Task ReplayMessageAsync_WithUnknownId_ReturnsFalse()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayMessageAsync_WithUnknownId_ReturnsFalse))
            .Options;
        await using var context = new TestDbContext(options);
        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var result = await management.ReplayMessageAsync(Guid.NewGuid()).ConfigureAwait(false);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    [Skip(
        "The EF Core InMemory provider does not support ExecuteUpdateAsync (bulk updates). Covered by integration tests."
    )]
    public async Task ReplayAllDeadLetterAsync_EmptyDatabase_ReturnsZero()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayAllDeadLetterAsync_EmptyDatabase_ReturnsZero))
            .Options;
        await using var context = new TestDbContext(options);
        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var count = await management.ReplayAllDeadLetterAsync().ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    [Skip(
        "The EF Core InMemory provider does not support ExecuteUpdateAsync (bulk updates). Covered by integration tests."
    )]
    public async Task ReplayAllDeadLetterAsync_WithDeadLetterMessages_ResetsAllAndReturnsCount()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayAllDeadLetterAsync_WithDeadLetterMessages_ResetsAllAndReturnsCount))
            .Options;
        await using var context = new TestDbContext(options);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            _ = context.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = "TestEvent",
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    RetryCount = 5,
                    Error = "Some error",
                    Status = OutboxMessageStatus.DeadLetter,
                }
            );
        }

        _ = context.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "TestEvent",
                Payload = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                Status = OutboxMessageStatus.Pending,
            }
        );
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var count = await management.ReplayAllDeadLetterAsync().ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task GetStatisticsAsync_EmptyDatabase_ReturnsZeroStatistics()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetStatisticsAsync_EmptyDatabase_ReturnsZeroStatistics))
            .Options;
        await using var context = new TestDbContext(options);
        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var statistics = await management.GetStatisticsAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(statistics.Pending).IsEqualTo(0L);
            _ = await Assert.That(statistics.Processing).IsEqualTo(0L);
            _ = await Assert.That(statistics.Completed).IsEqualTo(0L);
            _ = await Assert.That(statistics.Failed).IsEqualTo(0L);
            _ = await Assert.That(statistics.DeadLetter).IsEqualTo(0L);
            _ = await Assert.That(statistics.Total).IsEqualTo(0L);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_WithMessages_ReturnsCorrectCounts()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetStatisticsAsync_WithMessages_ReturnsCorrectCounts))
            .Options;
        await using var context = new TestDbContext(options);
        var now = DateTimeOffset.UtcNow;

        var statuses = new[]
        {
            OutboxMessageStatus.Pending,
            OutboxMessageStatus.Pending,
            OutboxMessageStatus.Processing,
            OutboxMessageStatus.Completed,
            OutboxMessageStatus.Completed,
            OutboxMessageStatus.Completed,
            OutboxMessageStatus.Failed,
            OutboxMessageStatus.DeadLetter,
            OutboxMessageStatus.DeadLetter,
        };

        foreach (var status in statuses)
        {
            _ = context.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = "TestEvent",
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Status = status,
                }
            );
        }

        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

        var statistics = await management.GetStatisticsAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(statistics.Pending).IsEqualTo(2L);
            _ = await Assert.That(statistics.Processing).IsEqualTo(1L);
            _ = await Assert.That(statistics.Completed).IsEqualTo(3L);
            _ = await Assert.That(statistics.Failed).IsEqualTo(1L);
            _ = await Assert.That(statistics.DeadLetter).IsEqualTo(2L);
            _ = await Assert.That(statistics.Total).IsEqualTo(9L);
        }
    }
}
