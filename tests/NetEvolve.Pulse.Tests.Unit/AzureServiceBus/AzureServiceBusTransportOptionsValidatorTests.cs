namespace NetEvolve.Pulse.Tests.Unit.AzureServiceBus;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("AzureServiceBus")]
public class AzureServiceBusTransportOptionsValidatorTests
{
    private static readonly AzureServiceBusTransportOptionsValidator _validator = new();

    [Test]
    public async Task Validate_WithConnectionString_Succeeds()
    {
        var options = new AzureServiceBusTransportOptions { ConnectionString = "Endpoint=sb://localhost/" };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithFullyQualifiedNamespace_Succeeds()
    {
        var options = new AzureServiceBusTransportOptions
        {
            FullyQualifiedNamespace = "contoso.servicebus.windows.net",
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithNeitherConnectionStringNorNamespace_Fails()
    {
        var options = new AzureServiceBusTransportOptions();

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithWhitespaceValues_Fails()
    {
        var options = new AzureServiceBusTransportOptions { ConnectionString = "   ", FullyQualifiedNamespace = "   " };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }
}
