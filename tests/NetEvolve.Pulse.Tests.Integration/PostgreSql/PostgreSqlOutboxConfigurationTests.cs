namespace NetEvolve.Pulse.Tests.Integration.PostgreSql;

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="PostgreSqlExtensions"/>.
/// Tests the full DI registration and integration flow with PostgreSQL ADO.NET.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgreSqlOutboxConfigurationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxConfigurationTests"/> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL container fixture.</param>
    public PostgreSqlOutboxConfigurationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"pgcfgtest_{Guid.NewGuid():N}";
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
    public async Task AddPostgreSqlOutbox_RegistersRepositoryService()
    {
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config => config.AddOutbox().AddPostgreSqlOutbox(connectionString));

        await using var provider = services.BuildServiceProvider();

        _ = await Assert.That(provider.GetService<IOutboxRepository>()).IsNotNull();
    }

    [Test]
    public async Task AddPostgreSqlOutbox_RepositoryIsCorrectType()
    {
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config => config.AddOutbox().AddPostgreSqlOutbox(connectionString));

        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IOutboxRepository>();

        _ = await Assert.That(repository).IsTypeOf<PostgreSqlOutboxRepository>();
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithCustomOptions_AppliesOptions()
    {
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config =>
            config
                .AddOutbox()
                .AddPostgreSqlOutbox(
                    connectionString,
                    options =>
                    {
                        options.Schema = "custom";
                        options.TableName = "CustomOutbox";
                    }
                )
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.Value.Schema).IsEqualTo("custom");
            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomOutbox");
        }
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithConnectionStringFactory_ResolvesConnectionString()
    {
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddSingleton(connectionString);
        _ = services.AddPulse(config => config.AddOutbox().AddPostgreSqlOutbox(sp => sp.GetRequiredService<string>()));

        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IOutboxRepository>();

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task AddPostgreSqlOutbox_IntegrationWithMediator_WorksEndToEnd()
    {
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config => config.AddOutbox().AddPostgreSqlOutbox(connectionString));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var eventOutbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();

        var evt = new TestPgEvent("integration-1", "Integration test");
        await eventOutbox.StoreAsync(evt).ConfigureAwait(false);

        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var pending = await repository.GetPendingAsync(10).ConfigureAwait(false);

        _ = await Assert.That(pending).Count().IsEqualTo(1);
    }

    [Test]
    public async Task AddPostgreSqlOutbox_ConcurrentScopes_CreateIndependentRepositories()
    {
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config => config.AddOutbox().AddPostgreSqlOutbox(connectionString));

        await using var provider = services.BuildServiceProvider();

        await using var scope1 = provider.CreateAsyncScope();
        await using var scope2 = provider.CreateAsyncScope();

        var repository1 = scope1.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var repository2 = scope2.ServiceProvider.GetRequiredService<IOutboxRepository>();

        _ = await Assert.That(ReferenceEquals(repository1, repository2)).IsFalse();
    }

    [Test]
    public async Task AddPostgreSqlOutbox_StoresMessagesForProcessing()
    {
        var services = CreateServiceCollection();
        var connectionString = _fixture.GetConnectionString(_databaseName);

        _ = services.AddPulse(config =>
            config
                .AddOutbox(configureProcessorOptions: opts =>
                {
                    opts.PollingInterval = TimeSpan.FromMilliseconds(100);
                    opts.BatchSize = 10;
                })
                .AddPostgreSqlOutbox(connectionString)
        );

        await using var provider = services.BuildServiceProvider();

        var eventOutbox = provider.GetRequiredService<IEventOutbox>();
        var repository = provider.GetRequiredService<IOutboxRepository>();

        var evt = new TestPgEvent("storage-1", "Storage test");
        await eventOutbox.StoreAsync(evt).ConfigureAwait(false);

        var pending = await repository.GetPendingAsync(10).ConfigureAwait(false);
        _ = await Assert.That(pending).Count().IsEqualTo(1);

        using (Assert.Multiple())
        {
            _ = await Assert.That(pending[0].EventType).IsEqualTo(typeof(TestPgEvent));
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
    /// Test event for PostgreSQL integration tests.
    /// </summary>
    internal sealed class TestPgEvent : IEvent
    {
        public TestPgEvent() { }

        public TestPgEvent(string id, string data)
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
