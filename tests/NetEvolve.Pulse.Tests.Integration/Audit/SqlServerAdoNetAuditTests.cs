namespace NetEvolve.Pulse.Tests.Integration.Audit;

using System.Data.SqlTypes;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Audit;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<SqlServerDatabaseServiceFixture, SqlServerAdoNetAuditInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("AdoNet")]
[InheritsTests]
public class SqlServerAdoNetAuditTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : AuditTestsBase(databaseServiceFixture, databaseInitializer)
{
    /// <inheritdoc />
    protected override IComparer<Guid> IdComparer { get; } =
        Comparer<Guid>.Create((x, y) => new SqlGuid(x).CompareTo(new SqlGuid(y)));
}
