namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

public sealed class CosmosDbOutboxInitializer : IServiceInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(mediatorBuilder);
        ArgumentNullException.ThrowIfNull(serviceFixture);

        var fixture = (CosmosDbDatabaseServiceFixture)serviceFixture;

        _ = mediatorBuilder.AddCosmosDbOutbox(opts => opts.DatabaseName = fixture.DatabaseName);

        // Propagate OutboxOptions.TableName -> CosmosDbOutboxOptions.ContainerName so that each
        // test method uses an isolated container (matching the per-test table-name isolation
        // applied by PulseTestsBase.RunAndVerify).
        _ = mediatorBuilder.Services.AddSingleton<IConfigureOptions<CosmosDbOutboxOptions>>(sp =>
        {
            var outboxOptions = sp.GetRequiredService<IOptionsMonitor<OutboxOptions>>();
            return new ConfigureOptions<CosmosDbOutboxOptions>(opts =>
                opts.ContainerName = outboxOptions.CurrentValue.TableName
            );
        });
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var cosmosClient = serviceProvider.GetRequiredService<CosmosClient>();
        var options = serviceProvider.GetRequiredService<IOptions<CosmosDbOutboxOptions>>().Value;

        var databaseResponse = await cosmosClient
            .CreateDatabaseIfNotExistsAsync(options.DatabaseName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _ = await databaseResponse
            .Database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(options.ContainerName, options.PartitionKeyPath),
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CosmosClient is registered as a Singleton and disposed by the DI container when it is torn down at the end of the test."
    )]
    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceFixture);

        var fixture = (CosmosDbDatabaseServiceFixture)serviceFixture;

        // Register CosmosClient with the emulator's HTTP handler so SSL certificate is trusted.
        _ = services.AddSingleton<CosmosClient>(_ =>
        {
            var clientOptions = new CosmosClientOptions
            {
                HttpClientFactory = () => new System.Net.Http.HttpClient(fixture.HttpMessageHandler),
                ConnectionMode = ConnectionMode.Gateway,
            };

            return new CosmosClient(fixture.ConnectionString, clientOptions);
        });
    }
}
