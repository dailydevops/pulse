namespace NetEvolve.Pulse.Tests.Unit.Dapr;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("Dapr")]
public class DaprMessageTransportOptionsValidatorTests
{
    private static readonly DaprMessageTransportOptionsValidator _validator = new();

    [Test]
    public async Task Validate_DefaultOptions_Succeeds()
    {
        var options = new DaprMessageTransportOptions();

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithCustomPubSubName_Succeeds()
    {
        var options = new DaprMessageTransportOptions { PubSubName = "custom-pubsub" };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithNullPubSubName_Fails()
    {
        var options = new DaprMessageTransportOptions { PubSubName = null! };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithEmptyPubSubName_Fails()
    {
        var options = new DaprMessageTransportOptions { PubSubName = string.Empty };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithWhitespacePubSubName_Fails()
    {
        var options = new DaprMessageTransportOptions { PubSubName = "   " };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }
}
