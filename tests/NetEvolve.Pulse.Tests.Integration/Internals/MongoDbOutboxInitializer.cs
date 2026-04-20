namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

public sealed class MongoDbOutboxInitializer : IServiceInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(mediatorBuilder);
        ArgumentNullException.ThrowIfNull(serviceFixture);

        var fixture = (MongoDbDatabaseServiceFixture)serviceFixture;

        _ = mediatorBuilder.AddMongoDbOutbox(opts => opts.DatabaseName = fixture.DatabaseName);

        // Propagate OutboxOptions.TableName -> MongoDbOutboxOptions.CollectionName so that each
        // test method uses an isolated collection (matching the per-test table-name isolation
        // applied by PulseTestsBase.RunAndVerify).
        _ = mediatorBuilder.Services.AddSingleton<IConfigureOptions<MongoDbOutboxOptions>>(sp =>
        {
            var outboxOptions = sp.GetRequiredService<IOptionsMonitor<OutboxOptions>>();
            return new ConfigureOptions<MongoDbOutboxOptions>(opts =>
                opts.CollectionName = outboxOptions.CurrentValue.TableName
            );
        });
    }

    public ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken) =>
        // MongoDB creates collections automatically on first insert — no schema setup required.
        ValueTask.CompletedTask;

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "MongoClient is registered as a Singleton and disposed by the DI container when it is torn down at the end of the test."
    )]
    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceFixture);

        // Register IMongoClient using the connection string from the container fixture.
        _ = services.AddSingleton<IMongoClient>(new MongoClient(serviceFixture.ConnectionString));
    }
}
