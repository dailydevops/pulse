namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("RabbitMQ")]
public sealed class RabbitMqExtensionsTests
{
    [Test]
    public async Task UseRabbitMqTransport_Registers_transport_service(CancellationToken cancellationToken)
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseRabbitMqTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(RabbitMqMessageTransport));
        _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
    }

    [Test]
    public async Task UseRabbitMqTransport_Configures_options(CancellationToken cancellationToken)
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseRabbitMqTransport(options => options.ExchangeName = "test-exchange"));

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqTransportOptions>>();

        _ = await Assert.That(options.Value.ExchangeName).IsEqualTo("test-exchange");
    }

    [Test]
    public async Task UseRabbitMqTransport_Without_configureOptions_registers_default_options(
        CancellationToken cancellationToken
    )
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseRabbitMqTransport());

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqTransportOptions>>();

        // Verify default options are accessible
        _ = await Assert.That(options.Value).IsNotNull();
        _ = await Assert.That(options.Value.ExchangeName).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task UseRabbitMqTransport_Replaces_existing_transport(CancellationToken cancellationToken)
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config => config.UseRabbitMqTransport());

        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageTransport)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors.Count).IsEqualTo(1);
            _ = await Assert.That(descriptors[0].ImplementationType).IsEqualTo(typeof(RabbitMqMessageTransport));
        }
    }

    [Test]
    public async Task UseRabbitMqTransport_When_configurator_null_throws(CancellationToken cancellationToken)
    {
        IMediatorBuilder configurator = null!;

        var exception = Assert.Throws<ArgumentNullException>(() => configurator.UseRabbitMqTransport());

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("configurator");
    }

    [Test]
    public async Task UseRabbitMqTransport_Returns_configurator_for_chaining(CancellationToken cancellationToken)
    {
        IServiceCollection services = new ServiceCollection();
        IMediatorBuilder? returnedConfigurator = null;

        _ = services.AddPulse(config => returnedConfigurator = config.UseRabbitMqTransport());

        _ = await Assert.That(returnedConfigurator).IsNotNull();
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
