namespace NetEvolve.Pulse.Kafka.Tests.Unit;

using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class KafkaMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task UseKafkaTransport_Registers_transport_and_adapters()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseKafkaTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(KafkaMessageTransport));

        var producerAdapterDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IKafkaProducerAdapter));
        _ = await Assert.That(producerAdapterDescriptor).IsNotNull();

        var adminAdapterDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IKafkaAdminAdapter));
        _ = await Assert.That(adminAdapterDescriptor).IsNotNull();
    }

    [Test]
    public async Task UseKafkaTransport_Replaces_existing_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config => config.UseKafkaTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(KafkaMessageTransport));
    }

    [Test]
    public async Task UseKafkaTransport_Returns_same_configurator_for_chaining()
    {
        IServiceCollection services = new ServiceCollection();
        IMediatorConfigurator? captured = null;

        _ = services.AddPulse(config =>
        {
            captured = config;
            _ = config.UseKafkaTransport().UseKafkaTransport();
        });

        _ = await Assert.That(captured).IsNotNull();
    }

    [Test]
    public void UseKafkaTransport_Requires_IProducer_to_be_registered_by_caller()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseKafkaTransport());

        using var provider = services.BuildServiceProvider();

        // KafkaProducerAdapter depends on IProducer<string, string> which the user must provide.
        _ = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IKafkaProducerAdapter>());
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
