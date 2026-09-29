namespace NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

public sealed class PostgreSqlAdoNetIdempotencyInitializer : IServiceInitializer
{
    private PostgreSqlDatabaseServiceFixture? _serviceFixture;

    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _serviceFixture = (PostgreSqlDatabaseServiceFixture)serviceFixture;
        _ = mediatorBuilder.AddPostgreSqlIdempotencyStore(serviceFixture.ConnectionString);
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = serviceProvider.GetRequiredService<IOptions<IdempotencyKeyOptions>>().Value;

        var schema = string.IsNullOrWhiteSpace(options.Schema) ? IdempotencyKeySchema.DefaultSchema : options.Schema;

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? IdempotencyKeySchema.DefaultTableName
            : options.TableName;

        var fixture =
            _serviceFixture ?? throw new InvalidOperationException("Configure must run before CreateDatabaseAsync.");

        await fixture
            .Container.RunScriptAsync("IdempotencyKey.sql", fixture.DatabaseName, schema, tableName, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET idempotency tests.
        // The Configure method handles all necessary service registrations.
    }
}
