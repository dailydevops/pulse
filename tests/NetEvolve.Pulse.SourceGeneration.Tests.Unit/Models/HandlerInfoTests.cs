namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit.Models;

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.SourceGeneration.Models;
using TUnit.Core;

[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.Models")]
public class HandlerInfoTests
{
    private static LocationInfo CreateLocation(string filePath = "File.cs") =>
        new(filePath, new TextSpan(0, 1), new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1)));

    private static HandlerRegistration CreateRegistration(string handlerTypeName = "global::Ns.Handler") =>
        new(handlerTypeName, "global::Ns.IHandler", HandlerKind.Command, lifetime: 1);

    [Test]
    public async Task EqualsThenTrueForIdenticalValues()
    {
        var location = CreateLocation();
        var left = new HandlerInfo("global::Ns.Handler", [CreateRegistration()], location);
        var right = new HandlerInfo("global::Ns.Handler", [CreateRegistration()], location);

        _ = await Assert.That(left.Equals(right)).IsTrue();
        _ = await Assert.That(left.Equals((object)right)).IsTrue();
        _ = await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task EqualsThenFalseWhenLocationDiffers()
    {
        var registrations = new[] { CreateRegistration() };
        var left = new HandlerInfo("global::Ns.Handler", registrations, CreateLocation("FileA.cs"));
        var right = new HandlerInfo("global::Ns.Handler", registrations, CreateLocation("FileB.cs"));

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsThenFalseWhenHandlerTypeNameDiffers()
    {
        var location = CreateLocation();
        var registrations = new[] { CreateRegistration() };
        var left = new HandlerInfo("global::Ns.HandlerA", registrations, location);
        var right = new HandlerInfo("global::Ns.HandlerB", registrations, location);

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsThenFalseWhenRegistrationCountDiffers()
    {
        var location = CreateLocation();
        var left = new HandlerInfo("global::Ns.Handler", [CreateRegistration()], location);
        var right = new HandlerInfo(
            "global::Ns.Handler",
            [CreateRegistration(), CreateRegistration("global::Ns.OtherHandler")],
            location
        );

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsThenFalseWhenRegistrationElementDiffers()
    {
        var location = CreateLocation();
        var left = new HandlerInfo("global::Ns.Handler", [CreateRegistration("global::Ns.HandlerA")], location);
        var right = new HandlerInfo("global::Ns.Handler", [CreateRegistration("global::Ns.HandlerB")], location);

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsObjectThenFalseWhenOtherIsNotHandlerInfo()
    {
        var handlerInfo = new HandlerInfo("global::Ns.Handler", [CreateRegistration()], CreateLocation());

        _ = await Assert.That(handlerInfo.Equals("not a HandlerInfo")).IsFalse();
    }

    [Test]
    public async Task GetHashCodeThenIncludesEachRegistration()
    {
        var location = CreateLocation();
        var withOneRegistration = new HandlerInfo("global::Ns.Handler", [CreateRegistration()], location);
        var withTwoRegistrations = new HandlerInfo(
            "global::Ns.Handler",
            [CreateRegistration(), CreateRegistration("global::Ns.OtherHandler")],
            location
        );

        _ = await Assert.That(withOneRegistration.GetHashCode()).IsNotEqualTo(withTwoRegistrations.GetHashCode());
    }
}
