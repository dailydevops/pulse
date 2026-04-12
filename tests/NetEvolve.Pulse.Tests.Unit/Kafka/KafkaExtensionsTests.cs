namespace NetEvolve.Pulse.Tests.Unit.Kafka;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Kafka")]
public sealed class KafkaExtensionsTests
{
    [Test]
    public async Task UseKafkaTransport_Registers_transport(CancellationToken cancellationToken)
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseKafkaTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(KafkaMessageTransport));
    }

    [Test]
    public async Task UseKafkaTransport_Replaces_existing_transport(CancellationToken cancellationToken)
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config => config.UseKafkaTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(KafkaMessageTransport));
    }

    [Test]
    public async Task UseKafkaTransport_Returns_same_configurator_for_chaining(CancellationToken cancellationToken)
    {
        IServiceCollection services = new ServiceCollection();
        IMediatorBuilder? captured = null;

        _ = services.AddPulse(config =>
        {
            captured = config;
            _ = config.UseKafkaTransport().UseKafkaTransport();
        });

        _ = await Assert.That(captured).IsNotNull();
    }

    [Test]
    public async Task UseKafkaTransport_Does_not_register_adapters(CancellationToken cancellationToken)
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseKafkaTransport());

        _ = await Assert
            .That(services.Any(d => d.ServiceType.Name.Contains("Adapter", StringComparison.Ordinal)))
            .IsFalse();
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
