namespace NetEvolve.Pulse.Tests.Unit.AzureServiceBus;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("AzureServiceBus")]
public class AzureServiceBusTransportOptionsConfigurationTests
{
    [Test]
    public async Task Configure_WithPopulatedSection_BindsAllProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Pulse:Transports:AzureServiceBus:ConnectionString"] = "Endpoint=sb://test/",
                    ["Pulse:Transports:AzureServiceBus:FullyQualifiedNamespace"] = "contoso.servicebus.windows.net",
                    ["Pulse:Transports:AzureServiceBus:EnableBatching"] = "false",
                }
            )
            .Build();

        var configurator = new AzureServiceBusTransportOptionsConfiguration(configuration);
        var options = new AzureServiceBusTransportOptions();

        configurator.Configure(options);

        _ = await Assert.That(options.ConnectionString).IsEqualTo("Endpoint=sb://test/");
        _ = await Assert.That(options.FullyQualifiedNamespace).IsEqualTo("contoso.servicebus.windows.net");
        _ = await Assert.That(options.EnableBatching).IsFalse();
    }

    [Test]
    public async Task Configure_WithoutSection_LeavesDefaultsUnchanged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var configurator = new AzureServiceBusTransportOptionsConfiguration(configuration);
        var options = new AzureServiceBusTransportOptions();
        var defaultConnectionString = options.ConnectionString;
        var defaultNamespace = options.FullyQualifiedNamespace;
        var defaultBatching = options.EnableBatching;

        configurator.Configure(options);

        _ = await Assert.That(options.ConnectionString).IsEqualTo(defaultConnectionString);
        _ = await Assert.That(options.FullyQualifiedNamespace).IsEqualTo(defaultNamespace);
        _ = await Assert.That(options.EnableBatching).IsEqualTo(defaultBatching);
    }

    [Test]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new AzureServiceBusTransportOptionsConfiguration(null!));
}
