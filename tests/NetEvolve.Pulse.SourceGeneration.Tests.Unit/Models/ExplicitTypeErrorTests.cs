namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit.Models;

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.SourceGeneration.Models;
using TUnit.Core;

[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.Models")]
public class ExplicitTypeErrorTests
{
    private static LocationInfo CreateLocation(string filePath = "File.cs") =>
        new(filePath, new TextSpan(0, 1), new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1)));

    [Test]
    public async Task EqualsThenTrueForIdenticalValues()
    {
        var location = CreateLocation();
        var left = new ExplicitTypeError("global::Ns.Message", "global::Ns.Handler", location, isPulse005: true);
        var right = new ExplicitTypeError("global::Ns.Message", "global::Ns.Handler", location, isPulse005: true);

        _ = await Assert.That(left.Equals(right)).IsTrue();
        _ = await Assert.That(left.Equals((object)right)).IsTrue();
        _ = await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task EqualsThenFalseWhenLocationDiffers()
    {
        var left = new ExplicitTypeError(
            "global::Ns.Message",
            "global::Ns.Handler",
            CreateLocation("FileA.cs"),
            isPulse005: true
        );
        var right = new ExplicitTypeError(
            "global::Ns.Message",
            "global::Ns.Handler",
            CreateLocation("FileB.cs"),
            isPulse005: true
        );

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsThenFalseWhenIsPulse005Differs()
    {
        var location = CreateLocation();
        var left = new ExplicitTypeError("global::Ns.Message", "global::Ns.Handler", location, isPulse005: true);
        var right = new ExplicitTypeError("global::Ns.Message", "global::Ns.Handler", location, isPulse005: false);

        _ = await Assert.That(left.Equals(right)).IsFalse();
        _ = await Assert.That(left.GetHashCode()).IsNotEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task EqualsThenFalseWhenMessageTypeNameDiffers()
    {
        var location = CreateLocation();
        var left = new ExplicitTypeError("global::Ns.MessageA", "global::Ns.Handler", location, isPulse005: true);
        var right = new ExplicitTypeError("global::Ns.MessageB", "global::Ns.Handler", location, isPulse005: true);

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsThenFalseWhenHandlerTypeNameDiffers()
    {
        var location = CreateLocation();
        var left = new ExplicitTypeError("global::Ns.Message", "global::Ns.HandlerA", location, isPulse005: true);
        var right = new ExplicitTypeError("global::Ns.Message", "global::Ns.HandlerB", location, isPulse005: true);

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsObjectThenFalseWhenOtherIsNotExplicitTypeError()
    {
        var error = new ExplicitTypeError(
            "global::Ns.Message",
            "global::Ns.Handler",
            CreateLocation(),
            isPulse005: true
        );

        _ = await Assert.That(error.Equals("not an ExplicitTypeError")).IsFalse();
    }
}
