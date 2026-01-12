namespace NetEvolve.Pulse.EntityFramework.Tests.Integration;

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.EntityFramework.Tests.Integration.Fixtures;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="EntityFrameworkEventOutbox{TContext}"/>.
/// Tests event storage with EF Core against a real SQL Server database.
/// </summary>
[ClassDataSource<SqlServerContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class EntityFrameworkEventOutboxTests
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkEventOutboxTests"/> class.
    /// </summary>
    /// <param name="fixture">The SQL Server container fixture.</param>
    public EntityFrameworkEventOutboxTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"EFOutboxTests_{Guid.NewGuid():N}";
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
    public async Task StoreAsync_WithValidEvent_PersistsToDatabase()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();
        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        var evt = new TestStoredEvent("stored-1", "Test data");

        // Act
        await outbox.StoreAsync(evt).ConfigureAwait(false);

        // Assert
        var message = await context.OutboxMessages.FirstOrDefaultAsync().ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(message).IsNotNull();
            _ = await Assert.That(message!.EventType).Contains(nameof(TestStoredEvent));
            _ = await Assert.That(message.Payload).Contains("stored-1");
            _ = await Assert.That(message.Status).IsEqualTo(OutboxMessageStatus.Pending);
        }
    }

    [Test]
    public async Task StoreAsync_WithTransaction_ParticipatesInTransaction()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var outbox = new EntityFrameworkEventOutbox<TestOutboxDbContext>(
            context,
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions()),
            TimeProvider.System
        );

        var evt = new TestStoredEvent("transaction-test", "Transaction data");

        // Act - Start transaction, store event, then rollback
        await using var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
        await outbox.StoreAsync(evt).ConfigureAwait(false);

        // Verify message exists before rollback
        var beforeRollback = await context.OutboxMessages.CountAsync().ConfigureAwait(false);
        _ = await Assert.That(beforeRollback).IsEqualTo(1);

        await transaction.RollbackAsync().ConfigureAwait(false);

        // Assert - After rollback, message should not exist
        context.ChangeTracker.Clear();
        var afterRollback = await context.OutboxMessages.CountAsync().ConfigureAwait(false);
        _ = await Assert.That(afterRollback).IsEqualTo(0);
    }

    [Test]
    public async Task StoreAsync_WithTransaction_CommitsWithTransaction()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var outbox = new EntityFrameworkEventOutbox<TestOutboxDbContext>(
            context,
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions()),
            TimeProvider.System
        );

        var evt = new TestStoredEvent("commit-test", "Commit data");

        // Act - Start transaction, store event, then commit
        await using var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
        await outbox.StoreAsync(evt).ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);

        // Assert - After commit, message should persist
        context.ChangeTracker.Clear();
        var afterCommit = await context.OutboxMessages.CountAsync().ConfigureAwait(false);
        _ = await Assert.That(afterCommit).IsEqualTo(1);
    }

    [Test]
    public async Task StoreAsync_PreservesEventProperties()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var outbox = new EntityFrameworkEventOutbox<TestOutboxDbContext>(
            context,
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions()),
            TimeProvider.System
        );

        var evt = new TestStoredEvent("props-test", "Property test data") { CorrelationId = "corr-abc-xyz" };

        // Act
        await outbox.StoreAsync(evt).ConfigureAwait(false);

        // Assert
        var message = await context.OutboxMessages.FirstOrDefaultAsync().ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(message).IsNotNull();
            _ = await Assert.That(message!.CorrelationId).IsEqualTo("corr-abc-xyz");
            _ = await Assert.That(message.Payload).Contains("props-test");
            _ = await Assert.That(message.Payload).Contains("Property test data");
        }
    }

    [Test]
    public async Task StoreAsync_SetsTimestamps()
    {
        // Arrange
        var services = CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var outbox = new EntityFrameworkEventOutbox<TestOutboxDbContext>(
            context,
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions()),
            TimeProvider.System
        );

        var beforeStore = DateTimeOffset.UtcNow;
        var evt = new TestStoredEvent("timestamp-test", "Timestamp data");

        // Act
        await outbox.StoreAsync(evt).ConfigureAwait(false);

        var afterStore = DateTimeOffset.UtcNow;

        // Assert
        var message = await context.OutboxMessages.FirstOrDefaultAsync().ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(message).IsNotNull();
            _ = await Assert.That(message!.CreatedAt).IsGreaterThanOrEqualTo(beforeStore);
            _ = await Assert.That(message.CreatedAt).IsLessThanOrEqualTo(afterStore);
            _ = await Assert.That(message.UpdatedAt).IsGreaterThanOrEqualTo(beforeStore);
            _ = await Assert.That(message.UpdatedAt).IsLessThanOrEqualTo(afterStore);
        }
    }

    [Test]
    public void StoreAsync_WithNullEvent_ThrowsArgumentNullException() =>
        _ = Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            var services = CreateServiceCollection();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();
            await outbox.StoreAsync<TestStoredEvent>(null!).ConfigureAwait(false);
        });

    private ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton(TimeProvider.System);
        _ = services.AddDbContext<TestOutboxDbContext>(options =>
            options.UseSqlServer(_fixture.GetConnectionString(_databaseName))
        );
        _ = services.Configure<OutboxOptions>(_ => { });
        _ = services.AddScoped<IEventOutbox, EntityFrameworkEventOutbox<TestOutboxDbContext>>();
        return services;
    }

    /// <summary>
    /// Test event for outbox storage tests.
    /// </summary>
    internal sealed class TestStoredEvent : IEvent
    {
        public TestStoredEvent() { }

        public TestStoredEvent(string id, string data)
        {
            Id = id;
            Data = data;
        }

        public string Id { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
