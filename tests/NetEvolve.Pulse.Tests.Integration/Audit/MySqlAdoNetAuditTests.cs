namespace NetEvolve.Pulse.Tests.Integration.Audit;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Audit;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<MySqlDatabaseServiceFixture, MySqlAdoNetAuditInitializer>(Shared = [SharedType.None, SharedType.None])]
[TestGroup("MySql")]
[TestGroup("AdoNet")]
[InheritsTests]
public class MySqlAdoNetAuditTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : AuditTestsBase(databaseServiceFixture, databaseInitializer)
{
    /// <inheritdoc />
    protected override IComparer<Guid> IdComparer { get; } =
        Comparer<Guid>.Create((x, y) => x.ToByteArray().AsSpan().SequenceCompareTo(y.ToByteArray()));
}
