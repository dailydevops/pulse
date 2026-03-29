namespace NetEvolve.Pulse.AzureServiceBus.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class AzureServiceBusMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task UseAzureServiceBusTransport_NullConfigurator_ThrowsArgumentNullException() =>
        // Act & Assert
        _ = await Assert
            .That(() => AzureServiceBusMediatorConfiguratorExtensions.UseAzureServiceBusTransport(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseAzureServiceBusTransport_NoOptions_RegistersTransport()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.UseAzureServiceBusTransport());

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor).IsNotNull();
        _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(AzureServiceBusMessageTransport));
    }

    [Test]
    public async Task UseAzureServiceBusTransport_WithOptions_RegistersTransport()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator.UseAzureServiceBusTransport(options =>
            {
                options.ConnectionString =
                    "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123=";
                options.EntityPath = "my-queue";
                options.EnableBatching = false;
            })
        );

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor).IsNotNull();
        _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(AzureServiceBusMessageTransport));
    }

    [Test]
    public async Task UseAzureServiceBusTransport_ReplacesExistingTransport()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.UseAzureServiceBusTransport());

        // Act — call again to replace
        _ = services.AddPulse(configurator => configurator.UseAzureServiceBusTransport());

        // Assert — only one IMessageTransport registration
        var transportCount = services.Count(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(transportCount).IsEqualTo(1);
    }

    [Test]
    public async Task UseAzureServiceBusTransport_RegistersOptionsForAzureServiceBusTransportOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator.UseAzureServiceBusTransport(options =>
            {
                options.EntityPath = "test-entity";
                options.EnableBatching = false;
            })
        );

        var provider = services.BuildServiceProvider();
        var options =
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureServiceBusTransportOptions>>();

        // Assert
        _ = await Assert.That(options.Value.EntityPath).IsEqualTo("test-entity");
        _ = await Assert.That(options.Value.EnableBatching).IsFalse();
    }
}
