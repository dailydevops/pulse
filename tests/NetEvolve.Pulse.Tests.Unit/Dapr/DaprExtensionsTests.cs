namespace NetEvolve.Pulse.Tests.Unit.Dapr;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class DaprExtensionsTests
{
    [Test]
    public async Task UseDaprTransport_When_configurator_is_null_throws_ArgumentNullException() =>
        _ = await Assert.That(() => DaprExtensions.UseDaprTransport(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task UseDaprTransport_Registers_transport_as_singleton()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseDaprTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(DaprMessageTransport));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseDaprTransport_Replaces_existing_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config => config.UseDaprTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(DaprMessageTransport));
    }

    [Test]
    public async Task UseDaprTransport_Configures_options()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseDaprTransport(o => o.PubSubName = "custom-pubsub"));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DaprMessageTransportOptions>>();

        _ = await Assert.That(options.Value.PubSubName).IsEqualTo("custom-pubsub");
    }

    [Test]
    public async Task UseDaprTransport_Without_configureOptions_uses_default_PubSubName()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseDaprTransport());

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DaprMessageTransportOptions>>();

        _ = await Assert.That(options.Value.PubSubName).IsEqualTo("pubsub");
    }

    [Test]
    public async Task UseDaprTransport_Returns_same_configurator_for_chaining()
    {
        IServiceCollection services = new ServiceCollection();
        IMediatorBuilder? captured = null;

        _ = services.AddPulse(config =>
        {
            captured = config;
            _ = config.UseDaprTransport().UseDaprTransport();
        });

        _ = await Assert.That(captured).IsNotNull();
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
