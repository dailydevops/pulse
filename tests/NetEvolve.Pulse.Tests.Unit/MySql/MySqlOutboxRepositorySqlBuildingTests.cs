namespace NetEvolve.Pulse.Tests.Unit.MySql;

using System.Reflection;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Pins the contract of <see cref="MySqlOutboxRepository"/>'s private <c>BuildInParameters</c>
/// helper: it is a pure string-building function (no I/O), so its output shape is verified
/// directly via reflection rather than through a live database round-trip.
/// </summary>
[TestGroup("MySql")]
public sealed class MySqlOutboxRepositorySqlBuildingTests
{
    private static string BuildInParameters(int count)
    {
        var method = typeof(MySqlOutboxRepository).GetMethod(
            "BuildInParameters",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        return (string)method!.Invoke(null, [count])!;
    }

    [Test]
    public async Task BuildInParameters_WithZeroCount_ReturnsEmptyString() =>
        _ = await Assert.That(BuildInParameters(0)).IsEqualTo(string.Empty);

    [Test]
    public async Task BuildInParameters_WithOneCount_ReturnsSingleParameter() =>
        _ = await Assert.That(BuildInParameters(1)).IsEqualTo("@id0");

    [Test]
    public async Task BuildInParameters_WithMultipleCount_ReturnsCommaSeparatedParameters() =>
        _ = await Assert.That(BuildInParameters(3)).IsEqualTo("@id0, @id1, @id2");
}
