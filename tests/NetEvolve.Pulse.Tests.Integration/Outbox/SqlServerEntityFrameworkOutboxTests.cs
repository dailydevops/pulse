namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using System.Data.SqlTypes;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<SqlServerDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SqlServerEntityFrameworkOutboxTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer)
{
    /// <inheritdoc />
    protected override IComparer<Guid> IdComparer { get; } =
        Comparer<Guid>.Create((x, y) => new SqlGuid(x).CompareTo(new SqlGuid(y)));
}
