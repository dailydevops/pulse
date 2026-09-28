namespace NetEvolve.Pulse.Tests.Integration.Internals.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Configures the MySQL ADO.NET outbox provider for integration tests.
/// Executes <c>Scripts/MySql/OutboxMessage.sql</c> to create the required table before each test.
/// </summary>
public sealed class MySqlAdoNetOutboxInitializer : IServiceInitializer
{
    /// <inheritdoc />
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _ = mediatorBuilder.AddMySqlOutbox(serviceFixture.ConnectionString);
    }

    /// <inheritdoc />
    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("OutboxOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? OutboxMessageSchema.DefaultTableName
            : options.TableName;

        await MySqlScriptRunner
            .ExecuteAsync(
                connectionString,
                "OutboxMessage.sql",
                OutboxMessageSchema.DefaultTableName,
                tableName,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET outbox tests.
    }
}
