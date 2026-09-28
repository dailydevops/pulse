namespace NetEvolve.Pulse.Tests.Integration.Internals.DeadLetter;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Configures the MySQL ADO.NET command dead letter store provider for integration tests.
/// Executes <c>Scripts/MySql/CommandDeadLetter.sql</c> to create the required table before each test.
/// </summary>
public sealed class MySqlAdoNetCommandDeadLetterInitializer : IServiceInitializer
{
    /// <inheritdoc />
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _ = mediatorBuilder.AddMySqlCommandDeadLetterStore(options =>
            options.ConnectionString = serviceFixture.ConnectionString
        );
    }

    /// <inheritdoc />
    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<CommandDeadLetterOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("CommandDeadLetterOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? CommandDeadLetterSchema.DefaultTableName
            : options.TableName;

        await MySqlScriptRunner
            .ExecuteAsync(
                connectionString,
                "CommandDeadLetter.sql",
                CommandDeadLetterSchema.DefaultTableName,
                tableName,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET command dead letter tests.
    }
}
