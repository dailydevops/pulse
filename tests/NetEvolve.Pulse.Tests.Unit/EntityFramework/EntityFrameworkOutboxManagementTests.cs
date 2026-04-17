namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
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
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            _ = await Assert
                .That(() => new EntityFrameworkOutboxManagement<TestDbContext>(context, null!))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithValidArguments_CreatesInstance))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            _ = await Assert.That(management).IsNotNull();
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(
                nameof(GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException)
            )
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            _ = await Assert
                .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: -1).ConfigureAwait(false))
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithZeroPageSize_ThrowsArgumentOutOfRangeException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_WithZeroPageSize_ThrowsArgumentOutOfRangeException))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            _ = await Assert
                .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: 0).ConfigureAwait(false))
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePage_ThrowsArgumentOutOfRangeException(
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_WithNegativePage_ThrowsArgumentOutOfRangeException))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            _ = await Assert
                .That(async () =>
                    await management
                        .GetDeadLetterMessagesAsync(page: -1, cancellationToken: cancellationToken)
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_EmptyDatabase_ReturnsEmptyList(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_EmptyDatabase_ReturnsEmptyList))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var result = await management
                .GetDeadLetterMessagesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEmpty();
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithDeadLetterMessages_ReturnsMessages(
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_WithDeadLetterMessages_ReturnsMessages))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            _ = context.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = typeof(TestDbEvent),
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
                    EventType = typeof(TestDbEvent),
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Status = OutboxMessageStatus.Pending,
                }
            );
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var result = await management
                .GetDeadLetterMessagesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).Count().IsEqualTo(1);
            _ = await Assert.That(result[0].Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithExistingDeadLetterMessage_ReturnsMessage(
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessageAsync_WithExistingDeadLetterMessage_ReturnsMessage))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var messageId = Guid.NewGuid();
            _ = context.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = messageId,
                    EventType = typeof(TestDbEvent),
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Status = OutboxMessageStatus.DeadLetter,
                }
            );
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var result = await management.GetDeadLetterMessageAsync(messageId, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsNotNull();
            _ = await Assert.That(result!.Id).IsEqualTo(messageId);
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithNonDeadLetterMessage_ReturnsNull(
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessageAsync_WithNonDeadLetterMessage_ReturnsNull))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var messageId = Guid.NewGuid();
            _ = context.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = messageId,
                    EventType = typeof(TestDbEvent),
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Status = OutboxMessageStatus.Completed,
                }
            );
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var result = await management.GetDeadLetterMessageAsync(messageId, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsNull();
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithUnknownId_ReturnsNull(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessageAsync_WithUnknownId_ReturnsNull))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var result = await management
                .GetDeadLetterMessageAsync(Guid.NewGuid(), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsNull();
        }
    }

    [Test]
    public async Task GetDeadLetterCountAsync_EmptyDatabase_ReturnsZero(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterCountAsync_EmptyDatabase_ReturnsZero))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var count = await management.GetDeadLetterCountAsync(cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(count).IsEqualTo(0L);
        }
    }

    [Test]
    public async Task GetDeadLetterCountAsync_WithDeadLetterMessages_ReturnsCorrectCount(
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterCountAsync_WithDeadLetterMessages_ReturnsCorrectCount))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 3; i++)
            {
                _ = context.OutboxMessages.Add(
                    new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(TestDbEvent),
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
                    EventType = typeof(TestDbEvent),
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Status = OutboxMessageStatus.Pending,
                }
            );
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var count = await management.GetDeadLetterCountAsync(cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(count).IsEqualTo(3L);
        }
    }

    [Test]
    public async Task ReplayMessageAsync_WithExistingDeadLetterMessage_ReturnsTrueAndResetsMessage(
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayMessageAsync_WithExistingDeadLetterMessage_ReturnsTrueAndResetsMessage))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var messageId = Guid.NewGuid();
            _ = context.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = messageId,
                    EventType = typeof(TestDbEvent),
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    RetryCount = 5,
                    Error = "Some error",
                    Status = OutboxMessageStatus.DeadLetter,
                }
            );
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var result = await management.ReplayMessageAsync(messageId, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsTrue();

            var message = await context.OutboxMessages.FindAsync([messageId], cancellationToken).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(message!.Status).IsEqualTo(OutboxMessageStatus.Pending);
                _ = await Assert.That(message.RetryCount).IsEqualTo(0);
                _ = await Assert.That(message.Error).IsNull();
            }
        }
    }

    [Test]
    public async Task ReplayMessageAsync_WithNonDeadLetterMessage_ReturnsFalse(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayMessageAsync_WithNonDeadLetterMessage_ReturnsFalse))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var messageId = Guid.NewGuid();
            _ = context.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = messageId,
                    EventType = typeof(TestDbEvent),
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Status = OutboxMessageStatus.Failed,
                }
            );
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var result = await management.ReplayMessageAsync(messageId, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsFalse();
        }
    }

    [Test]
    public async Task ReplayMessageAsync_WithUnknownId_ReturnsFalse(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayMessageAsync_WithUnknownId_ReturnsFalse))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var result = await management.ReplayMessageAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsFalse();
        }
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_EmptyDatabase_ReturnsZero(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayAllDeadLetterAsync_EmptyDatabase_ReturnsZero))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var count = await management.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_WithDeadLetterMessages_ResetsAllAndReturnsCount(
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayAllDeadLetterAsync_WithDeadLetterMessages_ResetsAllAndReturnsCount))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 3; i++)
            {
                _ = context.OutboxMessages.Add(
                    new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(TestDbEvent),
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
                    EventType = typeof(TestDbEvent),
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Status = OutboxMessageStatus.Pending,
                }
            );
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var count = await management.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(count).IsEqualTo(3);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_EmptyDatabase_ReturnsZeroStatistics(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetStatisticsAsync_EmptyDatabase_ReturnsZeroStatistics))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var statistics = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

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
    }

    [Test]
    public async Task GetStatisticsAsync_WithMessages_ReturnsCorrectCounts(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetStatisticsAsync_WithMessages_ReturnsCorrectCounts))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
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
                        EventType = typeof(TestDbEvent),
                        Payload = "{}",
                        CreatedAt = now,
                        UpdatedAt = now,
                        Status = status,
                    }
                );
            }

            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var statistics = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

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

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithPage_ReturnsCorrectPage(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_WithPage_ReturnsCorrectPage))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 5; i++)
            {
                _ = context.OutboxMessages.Add(
                    new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(TestDbEvent),
                        Payload = "{}",
                        CreatedAt = now,
                        UpdatedAt = now.AddMinutes(-i),
                        Status = OutboxMessageStatus.DeadLetter,
                    }
                );
            }
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var page0 = await management
                .GetDeadLetterMessagesAsync(pageSize: 3, page: 0, cancellationToken)
                .ConfigureAwait(false);
            var page1 = await management
                .GetDeadLetterMessagesAsync(pageSize: 3, page: 1, cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(page0.Count).IsEqualTo(3);
                _ = await Assert.That(page1.Count).IsEqualTo(2);
            }
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_OrderedByUpdatedAtDescending(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetDeadLetterMessagesAsync_OrderedByUpdatedAtDescending))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var oldest = now.AddHours(-2);
            var newest = now.AddHours(1);

            foreach (var updatedAt in new[] { now, oldest, newest })
            {
                _ = context.OutboxMessages.Add(
                    new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(TestDbEvent),
                        Payload = "{}",
                        CreatedAt = now,
                        UpdatedAt = updatedAt,
                        Status = OutboxMessageStatus.DeadLetter,
                    }
                );
            }
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var result = await management
                .GetDeadLetterMessagesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result.Count).IsEqualTo(3);
                _ = await Assert.That(result[0].UpdatedAt).IsEqualTo(newest);
                _ = await Assert.That(result[1].UpdatedAt).IsEqualTo(now);
                _ = await Assert.That(result[2].UpdatedAt).IsEqualTo(oldest);
            }
        }
    }

    [Test]
    public async Task ReplayMessageAsync_SetsUpdatedAtFromTimeProvider(CancellationToken cancellationToken)
    {
        var expectedTime = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayMessageAsync_SetsUpdatedAtFromTimeProvider))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var messageId = Guid.NewGuid();
            _ = context.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = messageId,
                    EventType = typeof(TestDbEvent),
                    Payload = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    RetryCount = 3,
                    Error = "Some error",
                    Status = OutboxMessageStatus.DeadLetter,
                }
            );
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var timeProvider = new FakeTimeProvider(expectedTime);
            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, timeProvider);

            _ = await management.ReplayMessageAsync(messageId, cancellationToken).ConfigureAwait(false);

            var message = await context.OutboxMessages.FindAsync([messageId], cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(message!.UpdatedAt).IsEqualTo(expectedTime);
        }
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_DoesNotAffectNonDeadLetterMessages(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayAllDeadLetterAsync_DoesNotAffectNonDeadLetterMessages))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var pendingId = Guid.NewGuid();
            var failedId = Guid.NewGuid();

            foreach (
                var (id, status, retryCount) in new[]
                {
                    (Guid.NewGuid(), OutboxMessageStatus.DeadLetter, 5),
                    (Guid.NewGuid(), OutboxMessageStatus.DeadLetter, 5),
                    (pendingId, OutboxMessageStatus.Pending, 0),
                    (failedId, OutboxMessageStatus.Failed, 2),
                }
            )
            {
                _ = context.OutboxMessages.Add(
                    new OutboxMessage
                    {
                        Id = id,
                        EventType = typeof(TestDbEvent),
                        Payload = "{}",
                        CreatedAt = now,
                        UpdatedAt = now,
                        RetryCount = retryCount,
                        Status = status,
                    }
                );
            }
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            var count = await management.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

            var pendingMessage = await context
                .OutboxMessages.FindAsync([pendingId], cancellationToken)
                .ConfigureAwait(false);
            var failedMessage = await context
                .OutboxMessages.FindAsync([failedId], cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(count).IsEqualTo(2);
                _ = await Assert.That(pendingMessage!.Status).IsEqualTo(OutboxMessageStatus.Pending);
                _ = await Assert.That(failedMessage!.Status).IsEqualTo(OutboxMessageStatus.Failed);
                _ = await Assert.That(failedMessage.RetryCount).IsEqualTo(2);
            }
        }
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_ResetsRetryCountAndError(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ReplayAllDeadLetterAsync_ResetsRetryCountAndError))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 3; i++)
            {
                _ = context.OutboxMessages.Add(
                    new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(TestDbEvent),
                        Payload = "{}",
                        CreatedAt = now,
                        UpdatedAt = now,
                        RetryCount = 5,
                        Error = "Fatal error",
                        Status = OutboxMessageStatus.DeadLetter,
                    }
                );
            }
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            _ = await management.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

            var messages = await context.OutboxMessages.ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var message in messages)
            {
                using (Assert.Multiple())
                {
                    _ = await Assert.That(message.Status).IsEqualTo(OutboxMessageStatus.Pending);
                    _ = await Assert.That(message.RetryCount).IsEqualTo(0);
                    _ = await Assert.That(message.Error).IsNull();
                }
            }
        }
    }

    private sealed record TestDbEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
