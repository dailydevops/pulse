namespace NetEvolve.Pulse.AzureServiceBus.Tests.Unit;

using System.Threading.Tasks;
using NetEvolve.Pulse;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class AzureServiceBusTransportOptionsTests
{
    [Test]
    public async Task DefaultValues_AreExpected()
    {
        // Arrange
        var options = new AzureServiceBusTransportOptions();

        // Assert
        _ = await Assert.That(options.ConnectionString).IsNull();
        _ = await Assert.That(options.FullyQualifiedNamespace).IsNull();
        _ = await Assert.That(options.EntityPath).IsEqualTo(string.Empty);
        _ = await Assert.That(options.EnableBatching).IsTrue();
    }

    [Test]
    public async Task Properties_CanBeSet()
    {
        // Arrange
        var options = new AzureServiceBusTransportOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=K;SharedAccessKey=x=",
            FullyQualifiedNamespace = "test.servicebus.windows.net",
            EntityPath = "my-queue",
            EnableBatching = false,
        };

        // Assert
        _ = await Assert
            .That(options.ConnectionString)
            .IsEqualTo("Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=K;SharedAccessKey=x=");
        _ = await Assert.That(options.FullyQualifiedNamespace).IsEqualTo("test.servicebus.windows.net");
        _ = await Assert.That(options.EntityPath).IsEqualTo("my-queue");
        _ = await Assert.That(options.EnableBatching).IsFalse();
    }
}
