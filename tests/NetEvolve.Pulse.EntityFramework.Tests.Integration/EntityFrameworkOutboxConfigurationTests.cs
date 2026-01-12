namespace NetEvolve.Pulse.EntityFramework.Tests.Integration;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.EntityFramework.Tests.Integration.Fixtures;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="EntityFrameworkMediatorConfiguratorExtensions"/>.
/// Tests the full DI registration and integration flow with EF Core.
/// </summary>
[ClassDataSource<SqlServerContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class EntityFrameworkOutboxConfigurationTests
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkOutboxConfigurationTests"/> class.
    /// </summary>
    /// <param name="fixture">The SQL Server container fixture.</param>
    public EntityFrameworkOutboxConfigurationTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"EFConfigTests_{Guid.NewGuid():N}";
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
    public async Task AddEntityFrameworkOutbox_RegistersAllRequiredServices()
    {
        // Arrange
        var services = CreateServiceCollection();

        _ = services.AddPulse(configurator => configurator.AddOutbox().AddEntityFrameworkOutbox<TestOutboxDbContext>());

        // Act
        await using var provider = services.BuildServiceProvider();

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(provider.GetService<IEventOutbox>()).IsNotNull();
            _ = await Assert.That(provider.GetService<IOutboxRepository>()).IsNotNull();
            _ = await Assert.That(provider.GetService<IOutboxTransactionScope>()).IsNotNull();
        }
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_EventOutboxIsCorrectType()
    {
        // Arrange
        var services = CreateServiceCollection();

        _ = services.AddPulse(configurator => configurator.AddOutbox().AddEntityFrameworkOutbox<TestOutboxDbContext>());

        // Act
        await using var provider = services.BuildServiceProvider();
        var outbox = provider.GetRequiredService<IEventOutbox>();

        // Assert
        _ = await Assert.That(outbox).IsTypeOf<EntityFrameworkEventOutbox<TestOutboxDbContext>>();
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_RepositoryIsCorrectType()
    {
        // Arrange
        var services = CreateServiceCollection();

        _ = services.AddPulse(configurator => configurator.AddOutbox().AddEntityFrameworkOutbox<TestOutboxDbContext>());

        // Act
        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IOutboxRepository>();

        // Assert
        _ = await Assert.That(repository).IsTypeOf<EntityFrameworkOutboxRepository<TestOutboxDbContext>>();
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_WithCustomOptions_AppliesOptions()
    {
        // Arrange
        var services = CreateServiceCollection();

        _ = services.AddPulse(configurator =>
            configurator
                .AddOutbox()
                .AddEntityFrameworkOutbox<TestOutboxDbContext>(options =>
                {
                    options.Schema = "custom";
                    options.TableName = "CustomOutbox";
                })
        );

        // Act
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>();

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(options.Value.Schema).IsEqualTo("custom");
            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomOutbox");
        }
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_IntegrationWithMediator_WorksEndToEnd()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handler = new TrackingEventHandler();

        _ = services
            .AddScoped<IEventHandler<TestIntegrationEvent>>(_ => handler)
            .AddPulse(configurator => configurator.AddOutbox().AddEntityFrameworkOutbox<TestOutboxDbContext>());

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();
        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        // Act
        var evt = new TestIntegrationEvent("integration-1", "Integration test");
        await outbox.StoreAsync(evt).ConfigureAwait(false);

        // Assert - Event should be stored in database
        var storedMessage = await context.OutboxMessages.FirstOrDefaultAsync().ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(storedMessage).IsNotNull();
            _ = await Assert.That(storedMessage!.EventType).Contains(nameof(TestIntegrationEvent));
        }
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_TransactionScope_ProvidesCurrentTransaction()
    {
        // Arrange
        var services = CreateServiceCollection();

        _ = services.AddPulse(configurator => configurator.AddOutbox().AddEntityFrameworkOutbox<TestOutboxDbContext>());

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var transactionScope = scope.ServiceProvider.GetRequiredService<IOutboxTransactionScope>();

        // Initially no transaction
        _ = await Assert.That(transactionScope.GetCurrentTransaction()).IsNull();

        // Act - Start a transaction
        await using var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);

        // Assert - Transaction scope should provide the current transaction
        _ = await Assert.That(transactionScope.GetCurrentTransaction()).IsNotNull();

        await transaction.RollbackAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Test event for integration tests.
    /// </summary>
    internal sealed class TestIntegrationEvent : IEvent
    {
        public TestIntegrationEvent() { }

        public TestIntegrationEvent(string id, string data)
        {
            Id = id;
            Data = data;
        }

        public string Id { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    /// <summary>
    /// Tracking event handler for integration tests.
    /// </summary>
    private sealed class TrackingEventHandler : IEventHandler<TestIntegrationEvent>
    {
        public List<TestIntegrationEvent> HandledEvents { get; } = [];

        public Task HandleAsync(TestIntegrationEvent message, CancellationToken cancellationToken = default)
        {
            HandledEvents.Add(message);
            return Task.CompletedTask;
        }
    }
}
