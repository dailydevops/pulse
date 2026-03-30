namespace NetEvolve.Pulse.RabbitMQ.Tests.Unit;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class RabbitMqMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task UseRabbitMqTransport_Registers_transport_service()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseRabbitMqTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(RabbitMqMessageTransport));
    }

    [Test]
    public async Task UseRabbitMqTransport_Replaces_existing_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config => config.UseRabbitMqTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(RabbitMqMessageTransport));
    }

    [Test]
    public async Task UseRabbitMqTransport_Configures_options()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseRabbitMqTransport(options =>
            {
                options.HostName = "test-host";
                options.Port = 5673;
                options.ExchangeName = "test-exchange";
            })
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqTransportOptions>>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.Value.HostName).IsEqualTo("test-host");
            _ = await Assert.That(options.Value.Port).IsEqualTo(5673);
            _ = await Assert.That(options.Value.ExchangeName).IsEqualTo("test-exchange");
        }
    }

    [Test]
    public async Task UseRabbitMqTransport_When_configurator_null_throws()
    {
        IMediatorConfigurator configurator = null!;

        var exception = Assert.Throws<ArgumentNullException>(() => configurator.UseRabbitMqTransport());

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("configurator");
    }

#pragma warning disable CA1812 // Avoid uninstantiated internal classes - instantiated via DI container
    private sealed class DummyTransport : IMessageTransport
#pragma warning restore CA1812
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
