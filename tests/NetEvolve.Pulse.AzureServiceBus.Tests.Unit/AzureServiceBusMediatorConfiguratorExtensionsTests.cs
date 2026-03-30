namespace NetEvolve.Pulse.AzureServiceBus.Tests.Unit;

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class AzureServiceBusMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task UseAzureServiceBusTransport_registers_transport_and_adapters()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options =>
            {
                options.ConnectionString =
                    "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";
            })
        );

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(AzureServiceBusMessageTransport));
    }

    [Test]
    public void UseAzureServiceBusTransport_validates_required_options()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseAzureServiceBusTransport());

        using var provider = services.BuildServiceProvider();

        _ = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ServiceBusClient>());
    }

    [Test]
    public async Task UseAzureServiceBusTransport_replaces_existing_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options =>
            {
                options.ConnectionString =
                    "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";
            })
        );

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(AzureServiceBusMessageTransport));
    }

    private sealed class DummyTransport : IMessageTransport
    {
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendBatchAsync(
            IEnumerable<OutboxMessage> messages,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}
