namespace NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Configures the MySQL ADO.NET idempotency store provider for integration tests.
/// Executes <c>Scripts/MySql/IdempotencyKey.sql</c> to create the required table before each test.
/// </summary>
public sealed class MySqlAdoNetIdempotencyInitializer : IServiceInitializer
{
    /// <inheritdoc />
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _ = mediatorBuilder.AddMySqlIdempotencyStore(serviceFixture.ConnectionString);
    }

    /// <inheritdoc />
    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<IdempotencyKeyOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("IdempotencyKeyOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? IdempotencyKeySchema.DefaultTableName
            : options.TableName;

        await MySqlScriptRunner
            .ExecuteAsync(
                connectionString,
                "IdempotencyKey.sql",
                IdempotencyKeySchema.DefaultTableName,
                tableName,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET idempotency tests.
    }
}
