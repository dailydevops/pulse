namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit.Models;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.SourceGeneration.Models;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.Models")]
public class ExplicitHandlerResultTests
{
    private static readonly HandlerRegistration Registration = new(
        "global::MyHandler",
        "global::ICommandHandler<global::MyCommand, string>",
        HandlerKind.Command,
        lifetime: 1
    );

    private static HandlerInfo CreateInfo(string handlerTypeName = "global::MyHandler") =>
        new(handlerTypeName, [Registration], default(LocationInfo));

    private static ExplicitTypeError CreateError(string messageTypeName = "global::string") =>
        new(messageTypeName, "global::MyHandler", default(LocationInfo), isPulse005: true);

    [Test]
    public async Task Equals_WhenBothInfoAbsentAndNoErrorsThenTrue()
    {
        var left = new ExplicitHandlerResult(null, []);
        var right = new ExplicitHandlerResult(null, []);

        _ = await Assert.That(left.Equals(right)).IsTrue();
    }

    [Test]
    public async Task Equals_WhenOneInfoPresentAndOtherAbsentThenFalse()
    {
        var left = new ExplicitHandlerResult(CreateInfo(), []);
        var right = new ExplicitHandlerResult(null, []);

        _ = await Assert.That(left.Equals(right)).IsFalse();
        _ = await Assert.That(right.Equals(left)).IsFalse();
    }

    [Test]
    public async Task Equals_WhenBothInfoPresentButDifferentThenFalse()
    {
        var left = new ExplicitHandlerResult(CreateInfo("global::HandlerA"), []);
        var right = new ExplicitHandlerResult(CreateInfo("global::HandlerB"), []);

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task Equals_WhenBothInfoPresentAndEqualThenTrue()
    {
        var left = new ExplicitHandlerResult(CreateInfo(), []);
        var right = new ExplicitHandlerResult(CreateInfo(), []);

        _ = await Assert.That(left.Equals(right)).IsTrue();
    }

    [Test]
    public async Task Equals_WhenErrorCountsDifferThenFalse()
    {
        var left = new ExplicitHandlerResult(null, [CreateError()]);
        var right = new ExplicitHandlerResult(null, []);

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task Equals_WhenErrorsDifferAtSameIndexThenFalse()
    {
        var left = new ExplicitHandlerResult(null, [CreateError("global::string")]);
        var right = new ExplicitHandlerResult(null, [CreateError("global::int")]);

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task Equals_WhenErrorsMatchElementWiseThenTrue()
    {
        var left = new ExplicitHandlerResult(null, [CreateError("global::string"), CreateError("global::int")]);
        var right = new ExplicitHandlerResult(null, [CreateError("global::string"), CreateError("global::int")]);

        _ = await Assert.That(left.Equals(right)).IsTrue();
    }

    [Test]
    public async Task EqualsObject_WhenArgumentIsBoxedEqualInstanceThenTrue()
    {
        var left = new ExplicitHandlerResult(CreateInfo(), [CreateError()]);
        object right = new ExplicitHandlerResult(CreateInfo(), [CreateError()]);

        _ = await Assert.That(left.Equals(right)).IsTrue();
    }

    [Test]
    public async Task EqualsObject_WhenArgumentIsDifferentTypeThenFalse()
    {
        var left = new ExplicitHandlerResult(null, []);

        _ = await Assert.That(left.Equals("not a result")).IsFalse();
    }

    [Test]
    public async Task GetHashCode_WhenInstancesAreEqualThenHashCodesMatch()
    {
        var left = new ExplicitHandlerResult(CreateInfo(), [CreateError("global::string"), CreateError("global::int")]);
        var right = new ExplicitHandlerResult(
            CreateInfo(),
            [CreateError("global::string"), CreateError("global::int")]
        );

        _ = await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task GetHashCode_WhenInfoAbsentThenDoesNotThrow()
    {
        var result = new ExplicitHandlerResult(null, [CreateError()]);

        var hashCode = result.GetHashCode();

        _ = await Assert.That(hashCode).IsEqualTo(result.GetHashCode());
    }

    [Test]
    public async Task Constructor_ExposesInfoAndErrors()
    {
        var info = CreateInfo();
        var errors = new[] { CreateError() };

        var result = new ExplicitHandlerResult(info, errors);

        _ = await Assert.That(result.Info).IsEqualTo(info);
        _ = await Assert.That(result.Errors).IsEquivalentTo(errors);
    }
}
