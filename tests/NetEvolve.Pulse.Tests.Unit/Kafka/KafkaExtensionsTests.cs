namespace NetEvolve.Pulse.Tests.Unit.Kafka;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    public async Task UseKafkaTransport_Registers_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseKafkaTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(KafkaMessageTransport));
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
        IMediatorBuilder? captured = null;

        _ = services.AddPulse(config =>
        {
            captured = config;
            _ = config.UseKafkaTransport().UseKafkaTransport();
        });

        _ = await Assert.That(captured).IsNotNull();
    }

    [Test]
    public async Task UseKafkaTransport_Does_not_register_adapters()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseKafkaTransport());

        _ = await Assert
            .That(services.Any(d => d.ServiceType.Name.Contains("Adapter", StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    public async Task UseKafkaTransport_With_configureOptions_registers_options()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseKafkaTransport(opt =>
            {
                opt.DefaultPartitionCount = 4;
                opt.DefaultReplicationFactor = 2;
                opt.AutoCreateTopics = false;
                opt.MessageRetention = TimeSpan.FromHours(48);
            })
        );

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KafkaTransportOptions>>().Value;

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.DefaultPartitionCount).IsEqualTo(4);
            _ = await Assert.That(options.DefaultReplicationFactor).IsEqualTo((short)2);
            _ = await Assert.That(options.AutoCreateTopics).IsFalse();
            _ = await Assert.That(options.MessageRetention).IsEqualTo(TimeSpan.FromHours(48));
        }
    }

    [Test]
    public async Task UseKafkaTransport_Without_configureOptions_uses_default_options()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseKafkaTransport());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KafkaTransportOptions>>().Value;

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.DefaultPartitionCount).IsEqualTo(1);
            _ = await Assert.That(options.DefaultReplicationFactor).IsEqualTo((short)1);
            _ = await Assert.That(options.AutoCreateTopics).IsTrue();
            _ = await Assert.That(options.MessageRetention).IsNull();
        }
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
