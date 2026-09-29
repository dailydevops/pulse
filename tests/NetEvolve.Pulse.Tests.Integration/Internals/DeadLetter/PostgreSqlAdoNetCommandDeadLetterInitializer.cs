namespace NetEvolve.Pulse.Tests.Integration.Internals.DeadLetter;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

public sealed class PostgreSqlAdoNetCommandDeadLetterInitializer : IServiceInitializer
{
    private PostgreSqlDatabaseServiceFixture? _serviceFixture;

    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _serviceFixture = (PostgreSqlDatabaseServiceFixture)serviceFixture;
        _ = mediatorBuilder.AddPostgreSqlCommandDeadLetterStore(options =>
            options.ConnectionString = serviceFixture.ConnectionString
        );
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = serviceProvider.GetRequiredService<IOptions<CommandDeadLetterOptions>>().Value;

        var schema = string.IsNullOrWhiteSpace(options.Schema) ? CommandDeadLetterSchema.DefaultSchema : options.Schema;

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? CommandDeadLetterSchema.DefaultTableName
            : options.TableName;

        var fixture =
            _serviceFixture ?? throw new InvalidOperationException("Configure must run before CreateDatabaseAsync.");

        await fixture
            .Container.RunScriptAsync(
                "CommandDeadLetter.sql",
                fixture.DatabaseName,
                schema,
                tableName,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET command dead letter tests.
        // The Configure method handles all necessary service registrations.
    }
}
