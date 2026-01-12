namespace NetEvolve.Pulse.EntityFramework.Tests.Integration;

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.EntityFramework.Tests.Integration.Fixtures;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="EntityFrameworkOutboxRepository{TContext}"/>.
/// Tests repository operations against a real SQL Server database using Testcontainers.
/// </summary>
[ClassDataSource<SqlServerContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class EntityFrameworkOutboxRepositoryTests
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkOutboxRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The SQL Server container fixture.</param>
    public EntityFrameworkOutboxRepositoryTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"EFRepoTests_{Guid.NewGuid():N}";
    }

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName).ConfigureAwait(false);

        // Create and migrate the database
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlServer(_fixture.GetConnectionString(_databaseName))
            .Options;

        await using var context = new TestOutboxDbContext(options);
        _ = await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    [After(Test)]
    public async Task CleanupAsync() => await _fixture.DropDatabaseAsync(_databaseName).ConfigureAwait(false);

    [Test]
    public async Task AddAsync_WithValidMessage_PersistsToDatabase()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var repository = new EntityFrameworkOutboxRepository<TestOutboxDbContext>(context, TimeProvider.System);

        var message = CreateMessage("add-test");

        // Act
        await repository.AddAsync(message).ConfigureAwait(false);

        // Assert
        var persisted = await context.OutboxMessages.FirstOrDefaultAsync(m => m.Id == message.Id).ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(persisted).IsNotNull();
            _ = await Assert.That(persisted!.EventType).IsEqualTo(message.EventType);
            _ = await Assert.That(persisted.Payload).IsEqualTo(message.Payload);
            _ = await Assert.That(persisted.Status).IsEqualTo(OutboxMessageStatus.Pending);
        }
    }

    [Test]
    public async Task GetPendingAsync_WithPendingMessages_ReturnsMessagesAndMarksAsProcessing()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var repository = new EntityFrameworkOutboxRepository<TestOutboxDbContext>(context, TimeProvider.System);

        var message1 = CreateMessage("pending-1");
        var message2 = CreateMessage("pending-2");
        await context.OutboxMessages.AddRangeAsync(message1, message2).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var pending = await repository.GetPendingAsync(10).ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(pending).Count().IsEqualTo(2);
            _ = await Assert.That(pending[0].Status).IsEqualTo(OutboxMessageStatus.Processing);
            _ = await Assert.That(pending[1].Status).IsEqualTo(OutboxMessageStatus.Processing);
        }
    }

    [Test]
    public async Task GetPendingAsync_WithBatchSize_RespectsLimit()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var repository = new EntityFrameworkOutboxRepository<TestOutboxDbContext>(context, TimeProvider.System);

        for (var i = 0; i < 5; i++)
        {
            _ = await context.OutboxMessages.AddAsync(CreateMessage($"batch-{i}")).ConfigureAwait(false);
        }

        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var pending = await repository.GetPendingAsync(2).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(pending).Count().IsEqualTo(2);
    }

    [Test]
    public async Task MarkAsCompletedAsync_WithValidMessage_UpdatesStatusAndProcessedAt()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var repository = new EntityFrameworkOutboxRepository<TestOutboxDbContext>(context, TimeProvider.System);

        var message = CreateMessage("complete-test");
        message.Status = OutboxMessageStatus.Processing;
        _ = await context.OutboxMessages.AddAsync(message).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await repository.MarkAsCompletedAsync(message.Id).ConfigureAwait(false);

        // Assert - need to reload to see changes
        context.ChangeTracker.Clear();
        var completed = await context.OutboxMessages.FirstOrDefaultAsync(m => m.Id == message.Id).ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(completed).IsNotNull();
            _ = await Assert.That(completed!.Status).IsEqualTo(OutboxMessageStatus.Completed);
            _ = await Assert.That(completed.ProcessedAt).IsNotNull();
        }
    }

    [Test]
    public async Task MarkAsFailedAsync_WithValidMessage_UpdatesStatusAndRetryCount()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var repository = new EntityFrameworkOutboxRepository<TestOutboxDbContext>(context, TimeProvider.System);

        var message = CreateMessage("fail-test");
        message.Status = OutboxMessageStatus.Processing;
        message.RetryCount = 0;
        _ = await context.OutboxMessages.AddAsync(message).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await repository.MarkAsFailedAsync(message.Id, "Test error message").ConfigureAwait(false);

        // Assert
        context.ChangeTracker.Clear();
        var failed = await context.OutboxMessages.FirstOrDefaultAsync(m => m.Id == message.Id).ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(failed).IsNotNull();
            _ = await Assert.That(failed!.Status).IsEqualTo(OutboxMessageStatus.Failed);
            _ = await Assert.That(failed.RetryCount).IsEqualTo(1);
            _ = await Assert.That(failed.Error).IsEqualTo("Test error message");
        }
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_WithValidMessage_UpdatesStatus()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var repository = new EntityFrameworkOutboxRepository<TestOutboxDbContext>(context, TimeProvider.System);

        var message = CreateMessage("dead-letter-test");
        message.Status = OutboxMessageStatus.Processing;
        message.RetryCount = 3;
        _ = await context.OutboxMessages.AddAsync(message).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await repository.MarkAsDeadLetterAsync(message.Id, "Max retries exceeded").ConfigureAwait(false);

        // Assert
        context.ChangeTracker.Clear();
        var deadLetter = await context
            .OutboxMessages.FirstOrDefaultAsync(m => m.Id == message.Id)
            .ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(deadLetter).IsNotNull();
            _ = await Assert.That(deadLetter!.Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
            _ = await Assert.That(deadLetter.Error).IsEqualTo("Max retries exceeded");
        }
    }

    [Test]
    public async Task GetFailedForRetryAsync_WithFailedMessages_ReturnsEligibleMessages()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var repository = new EntityFrameworkOutboxRepository<TestOutboxDbContext>(context, TimeProvider.System);

        var message1 = CreateMessage("retry-eligible");
        message1.Status = OutboxMessageStatus.Failed;
        message1.RetryCount = 1;

        var message2 = CreateMessage("retry-exceeded");
        message2.Status = OutboxMessageStatus.Failed;
        message2.RetryCount = 5; // Exceeds max

        await context.OutboxMessages.AddRangeAsync(message1, message2).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var forRetry = await repository.GetFailedForRetryAsync(3, 10).ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(forRetry).Count().IsEqualTo(1);
            _ = await Assert.That(forRetry[0].Id).IsEqualTo(message1.Id);
        }
    }

    [Test]
    public async Task DeleteCompletedAsync_WithOldCompletedMessages_DeletesThem()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var repository = new EntityFrameworkOutboxRepository<TestOutboxDbContext>(context, TimeProvider.System);

        var oldMessage = CreateMessage("old-completed");
        oldMessage.Status = OutboxMessageStatus.Completed;
        oldMessage.ProcessedAt = DateTimeOffset.UtcNow.AddDays(-10);

        var recentMessage = CreateMessage("recent-completed");
        recentMessage.Status = OutboxMessageStatus.Completed;
        recentMessage.ProcessedAt = DateTimeOffset.UtcNow;

        await context.OutboxMessages.AddRangeAsync(oldMessage, recentMessage).ConfigureAwait(false);
        _ = await context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var deletedCount = await repository.DeleteCompletedAsync(TimeSpan.FromDays(7)).ConfigureAwait(false);

        // Assert
        context.ChangeTracker.Clear();
        var remaining = await context.OutboxMessages.ToListAsync().ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(deletedCount).IsEqualTo(1);
            _ = await Assert.That(remaining).Count().IsEqualTo(1);
            _ = await Assert.That(remaining[0].Id).IsEqualTo(recentMessage.Id);
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

    private static OutboxMessage CreateMessage(string id)
    {
        var now = DateTimeOffset.UtcNow;
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = $"TestEvent.{id}",
            Payload = $"{{\"Id\":\"{id}\"}}",
            CorrelationId = $"corr-{id}",
            CreatedAt = now,
            UpdatedAt = now,
            Status = OutboxMessageStatus.Pending,
        };
    }
}
