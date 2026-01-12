namespace NetEvolve.Pulse.SqlServer.Tests.Integration;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.SqlServer.Tests.Integration.Fixtures;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="SqlServerMediatorConfiguratorExtensions"/>.
/// Tests the full DI registration and integration flow with SQL Server ADO.NET.
/// </summary>
[ClassDataSource<SqlServerContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class SqlServerOutboxConfigurationTests
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerOutboxConfigurationTests"/> class.
    /// </summary>
    /// <param name="fixture">The SQL Server container fixture.</param>
    public SqlServerOutboxConfigurationTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"SqlConfigTests_{Guid.NewGuid():N}";
    }

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName).ConfigureAwait(false);
        await _fixture.InitializeSchemaAsync(_databaseName).ConfigureAwait(false);
    }

    [After(Test)]
    public async Task CleanupAsync() => await _fixture.DropDatabaseAsync(_databaseName).ConfigureAwait(false);

    [Test]
    public async Task AddSqlServerOutbox_RegistersRepositoryService()
    {
        // Arrange
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox(connectionString));

        // Act
        await using var provider = services.BuildServiceProvider();

        // Assert
        _ = await Assert.That(provider.GetService<IOutboxRepository>()).IsNotNull();
    }

    [Test]
    public async Task AddSqlServerOutbox_RepositoryIsCorrectType()
    {
        // Arrange
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox(connectionString));

        // Act
        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IOutboxRepository>();

        // Assert
        _ = await Assert.That(repository).IsTypeOf<SqlServerOutboxRepository>();
    }

    [Test]
    public async Task AddSqlServerOutbox_WithCustomOptions_AppliesOptions()
    {
        // Arrange
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config =>
            config
                .AddOutbox()
                .AddSqlServerOutbox(
                    connectionString,
                    options =>
                    {
                        options.Schema = "custom";
                        options.TableName = "CustomOutbox";
                    }
                )
        );

        // Act
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(options.Value.Schema).IsEqualTo("custom");
            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomOutbox");
        }
    }

    [Test]
    public async Task AddSqlServerOutbox_WithConnectionStringFactory_ResolvesConnectionString()
    {
        // Arrange
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        // Register connection string in DI
        _ = services.AddSingleton(connectionString);
        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox(sp => sp.GetRequiredService<string>()));

        // Act
        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IOutboxRepository>();

        // Assert - Should be able to use the repository
        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task AddSqlServerOutbox_IntegrationWithMediator_WorksEndToEnd()
    {
        // Arrange
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox(connectionString));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var eventOutbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();

        // Act
        var evt = new TestSqlEvent("integration-1", "Integration test");
        await eventOutbox.StoreAsync(evt).ConfigureAwait(false);

        // Assert - Verify stored in database
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var pending = await repository.GetPendingAsync(10).ConfigureAwait(false);

        _ = await Assert.That(pending).Count().IsEqualTo(1);
    }

    [Test]
    public async Task AddSqlServerOutbox_ConcurrentScopes_CreateIndependentRepositories()
    {
        // Arrange
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox(connectionString));

        await using var provider = services.BuildServiceProvider();

        // Act
        await using var scope1 = provider.CreateAsyncScope();
        await using var scope2 = provider.CreateAsyncScope();

        var repository1 = scope1.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var repository2 = scope2.ServiceProvider.GetRequiredService<IOutboxRepository>();

        // Assert - Should be different instances (scoped)
        _ = await Assert.That(ReferenceEquals(repository1, repository2)).IsFalse();
    }

    [Test]
    public async Task AddSqlServerOutbox_StoresMessagesForProcessing()
    {
        // Arrange
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config =>
            config
                .AddOutbox(configureProcessorOptions: opts =>
                {
                    opts.PollingInterval = TimeSpan.FromMilliseconds(100);
                    opts.BatchSize = 10;
                })
                .AddSqlServerOutbox(connectionString)
        );

        await using var provider = services.BuildServiceProvider();

        var eventOutbox = provider.GetRequiredService<IEventOutbox>();
        var repository = provider.GetRequiredService<IOutboxRepository>();

        // Act - Store event
        var evt = new TestSqlEvent("storage-1", "Storage test");
        await eventOutbox.StoreAsync(evt).ConfigureAwait(false);

        // Assert - Event should be stored pending processing
        var pending = await repository.GetPendingAsync(10).ConfigureAwait(false);
        _ = await Assert.That(pending).Count().IsEqualTo(1);

        // Verify the stored message contains the correct event
        using (Assert.Multiple())
        {
            _ = await Assert.That(pending[0].EventType).Contains(nameof(TestSqlEvent));
            _ = await Assert.That(pending[0].Payload).Contains("storage-1").And.Contains("Storage test");
        }
    }

    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Test event for SQL Server integration tests.
    /// </summary>
    internal sealed class TestSqlEvent : IEvent
    {
        public TestSqlEvent() { }

        public TestSqlEvent(string id, string data)
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
