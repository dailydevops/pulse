namespace NetEvolve.Pulse.Tests.Integration.Internals.Audit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

public sealed class PostgreSqlAdoNetAuditInitializer : IServiceInitializer
{
    private PostgreSqlDatabaseServiceFixture? _serviceFixture;

    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _serviceFixture = (PostgreSqlDatabaseServiceFixture)serviceFixture;
        _ = mediatorBuilder.AddPostgreSqlAuditStore(options =>
            options.ConnectionString = serviceFixture.ConnectionString
        );
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = serviceProvider.GetRequiredService<IOptions<AuditStoreOptions>>().Value;

        var schema = string.IsNullOrWhiteSpace(options.Schema) ? AuditEntrySchema.DefaultSchema : options.Schema;

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? AuditEntrySchema.DefaultTableName
            : options.TableName;

        var fixture =
            _serviceFixture ?? throw new InvalidOperationException("Configure must run before CreateDatabaseAsync.");

        await fixture
            .Container.RunScriptAsync("AuditEntry.sql", fixture.DatabaseName, schema, tableName, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET audit trail tests.
        // The Configure method handles all necessary service registrations.
    }
}
