namespace NetEvolve.Pulse.Tests.Integration.Internals.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

public sealed class PostgreSqlAdoNetOutboxInitializer : IServiceInitializer
{
    private PostgreSqlDatabaseServiceFixture? _serviceFixture;

    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _serviceFixture = (PostgreSqlDatabaseServiceFixture)serviceFixture;
        _ = mediatorBuilder.AddPostgreSqlOutbox(serviceFixture.ConnectionString);
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = serviceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value;

        var schema = string.IsNullOrWhiteSpace(options.Schema) ? OutboxMessageSchema.DefaultSchema : options.Schema;

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? OutboxMessageSchema.DefaultTableName
            : options.TableName;

        var fixture =
            _serviceFixture ?? throw new InvalidOperationException("Configure must run before CreateDatabaseAsync.");

        await fixture
            .Container.RunScriptAsync("OutboxMessage.sql", fixture.DatabaseName, schema, tableName, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET outbox tests.
        // The Configure method handles all necessary service registrations.
    }
}
