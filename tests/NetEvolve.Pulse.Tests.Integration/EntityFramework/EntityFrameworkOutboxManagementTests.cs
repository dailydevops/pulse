namespace NetEvolve.Pulse.Tests.Integration.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="EntityFrameworkOutboxManagement{TContext}"/>.
/// Tests management operations against a real SQL Server database using Testcontainers.
/// These tests cover scenarios that cannot be tested with the EF Core InMemory provider
/// because it does not support <c>ExecuteUpdateAsync</c> (bulk updates).
/// </summary>
[ClassDataSource<SqlServerContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class EntityFrameworkOutboxManagementTests
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkOutboxManagementTests"/> class.
    /// </summary>
    /// <param name="fixture">The SQL Server container fixture.</param>
    public EntityFrameworkOutboxManagementTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"EFMgmtTests_{Guid.NewGuid():N}";
    }

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName).ConfigureAwait(false);

        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlServer(_fixture.GetConnectionString(_databaseName))
            .Options;

        await using var context = new TestOutboxDbContext(options);
        _ = await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    [After(Test)]
    public async Task CleanupAsync() => await _fixture.DropDatabaseAsync(_databaseName).ConfigureAwait(false);

    [Test]
    public async Task GetDeadLetterMessagesAsync_EmptyDatabase_ReturnsEmptyList()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        // Act
        var result = await management.GetDeadLetterMessagesAsync().ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithDeadLetterMessages_ReturnsOnlyDeadLetterRows()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        _ = await context
            .OutboxMessages.AddAsync(CreateMessage("dl-1", OutboxMessageStatus.DeadLetter))
            .ConfigureAwait(false);
        _ = await context
            .OutboxMessages.AddAsync(CreateMessage("pending-1", OutboxMessageStatus.Pending))
            .ConfigureAwait(false);
        _ = await context
            .OutboxMessages.AddAsync(CreateMessage("dl-2", OutboxMessageStatus.DeadLetter))
            .ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await management.GetDeadLetterMessagesAsync().ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result).Count().IsEqualTo(2);
        _ = await Assert.That(result[0].Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
        _ = await Assert.That(result[1].Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithPageSize_RespectsLimit()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        for (var i = 0; i < 5; i++)
        {
            _ = await context
                .OutboxMessages.AddAsync(CreateMessage($"dl-{i}", OutboxMessageStatus.DeadLetter))
                .ConfigureAwait(false);
        }

        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await management.GetDeadLetterMessagesAsync(pageSize: 3).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result).Count().IsEqualTo(3);
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithPage_SkipsCorrectly()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        for (var i = 0; i < 5; i++)
        {
            _ = await context
                .OutboxMessages.AddAsync(CreateMessage($"dl-{i}", OutboxMessageStatus.DeadLetter))
                .ConfigureAwait(false);
        }

        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page0 = await management.GetDeadLetterMessagesAsync(pageSize: 3, page: 0).ConfigureAwait(false);
        var page1 = await management.GetDeadLetterMessagesAsync(pageSize: 3, page: 1).ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(page0).Count().IsEqualTo(3);
            _ = await Assert.That(page1).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithExistingDeadLetterMessage_ReturnsMessage()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        var message = CreateMessage("dl-find", OutboxMessageStatus.DeadLetter);
        _ = await context.OutboxMessages.AddAsync(message).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await management.GetDeadLetterMessageAsync(message.Id).ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsNotNull();
            _ = await Assert.That(result!.Id).IsEqualTo(message.Id);
            _ = await Assert.That(result.Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithNonDeadLetterMessage_ReturnsNull()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        var message = CreateMessage("pending-find", OutboxMessageStatus.Pending);
        _ = await context.OutboxMessages.AddAsync(message).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await management.GetDeadLetterMessageAsync(message.Id).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithUnknownId_ReturnsNull()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        // Act
        var result = await management.GetDeadLetterMessageAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetDeadLetterCountAsync_EmptyDatabase_ReturnsZero()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        // Act
        var count = await management.GetDeadLetterCountAsync().ConfigureAwait(false);

        // Assert
        _ = await Assert.That(count).IsEqualTo(0L);
    }

    [Test]
    public async Task GetDeadLetterCountAsync_WithDeadLetterMessages_ReturnsCorrectCount()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        for (var i = 0; i < 3; i++)
        {
            _ = await context
                .OutboxMessages.AddAsync(CreateMessage($"dl-{i}", OutboxMessageStatus.DeadLetter))
                .ConfigureAwait(false);
        }

        _ = await context
            .OutboxMessages.AddAsync(CreateMessage("pending-1", OutboxMessageStatus.Pending))
            .ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var count = await management.GetDeadLetterCountAsync().ConfigureAwait(false);

        // Assert
        _ = await Assert.That(count).IsEqualTo(3L);
    }

    [Test]
    public async Task ReplayMessageAsync_WithExistingDeadLetterMessage_ReturnsTrueAndResetsMessage()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        var message = CreateMessage("replay-dl", OutboxMessageStatus.DeadLetter);
        message.RetryCount = 5;
        message.Error = "Some error";
        _ = await context.OutboxMessages.AddAsync(message).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await management.ReplayMessageAsync(message.Id).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result).IsTrue();

        context.ChangeTracker.Clear();
        var updated = await context.OutboxMessages.FirstOrDefaultAsync(m => m.Id == message.Id).ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(updated).IsNotNull();
            _ = await Assert.That(updated!.Status).IsEqualTo(OutboxMessageStatus.Pending);
            _ = await Assert.That(updated.RetryCount).IsEqualTo(0);
            _ = await Assert.That(updated.Error).IsNull();
        }
    }

    [Test]
    public async Task ReplayMessageAsync_WithNonDeadLetterMessage_ReturnsFalse()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        var message = CreateMessage("replay-failed", OutboxMessageStatus.Failed);
        _ = await context.OutboxMessages.AddAsync(message).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await management.ReplayMessageAsync(message.Id).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result).IsFalse();

        context.ChangeTracker.Clear();
        var unchanged = await context.OutboxMessages.FirstOrDefaultAsync(m => m.Id == message.Id).ConfigureAwait(false);
        _ = await Assert.That(unchanged!.Status).IsEqualTo(OutboxMessageStatus.Failed);
    }

    [Test]
    public async Task ReplayMessageAsync_WithUnknownId_ReturnsFalse()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        // Act
        var result = await management.ReplayMessageAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_EmptyDatabase_ReturnsZero()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        // Act
        var count = await management.ReplayAllDeadLetterAsync().ConfigureAwait(false);

        // Assert
        _ = await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_WithDeadLetterMessages_ResetsAllAndReturnsCount()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        for (var i = 0; i < 3; i++)
        {
            var msg = CreateMessage($"dl-{i}", OutboxMessageStatus.DeadLetter);
            msg.RetryCount = 5;
            msg.Error = "Some error";
            _ = await context.OutboxMessages.AddAsync(msg).ConfigureAwait(false);
        }

        _ = await context
            .OutboxMessages.AddAsync(CreateMessage("pending-1", OutboxMessageStatus.Pending))
            .ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var count = await management.ReplayAllDeadLetterAsync().ConfigureAwait(false);

        // Assert
        _ = await Assert.That(count).IsEqualTo(3);

        context.ChangeTracker.Clear();
        var allMessages = await context.OutboxMessages.ToListAsync().ConfigureAwait(false);
        var deadLetters = allMessages.Where(m => m.Status == OutboxMessageStatus.DeadLetter).ToList();
        var resetMessages = allMessages.Where(m => m.Status == OutboxMessageStatus.Pending).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(deadLetters).IsEmpty();
            _ = await Assert.That(resetMessages).Count().IsEqualTo(4);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_EmptyDatabase_ReturnsZeroStatistics()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

        // Act
        var statistics = await management.GetStatisticsAsync().ConfigureAwait(false);

        // Assert
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
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var management = new EntityFrameworkOutboxManagement<TestOutboxDbContext>(context, TimeProvider.System);

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

        var index = 0;
        foreach (var status in statuses)
        {
            _ = await context.OutboxMessages.AddAsync(CreateMessage($"msg-{index++}", status)).ConfigureAwait(false);
        }

        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var statistics = await management.GetStatisticsAsync().ConfigureAwait(false);

        // Assert
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

    private ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton(TimeProvider.System);
        _ = services.AddDbContext<TestOutboxDbContext>(options =>
            options.UseSqlServer(_fixture.GetConnectionString(_databaseName))
        );
        return services;
    }

    private static OutboxMessage CreateMessage(string id, OutboxMessageStatus status = OutboxMessageStatus.Pending)
    {
        var now = DateTimeOffset.UtcNow;
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestEFManagementEvent),
            Payload = $"{{\"Id\":\"{id}\"}}",
            CorrelationId = $"corr-{id}",
            CreatedAt = now,
            UpdatedAt = now,
            Status = status,
        };
    }

    private sealed record TestEFManagementEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
