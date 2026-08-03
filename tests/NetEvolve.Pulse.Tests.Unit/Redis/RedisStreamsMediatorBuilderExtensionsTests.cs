namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Redis")]
public sealed class RedisStreamsMediatorBuilderExtensionsTests
{
    [Test]
    public async Task UseRedisStreamsTransport_Registers_transport_service()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseRedisStreamsTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(RedisStreamsMessageTransport));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseRedisStreamsTransport_Replaces_existing_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config => config.UseRedisStreamsTransport());

        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageTransport)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors.Count).IsEqualTo(1);
            _ = await Assert.That(descriptors[0].ImplementationType).IsEqualTo(typeof(RedisStreamsMessageTransport));
        }
    }

    [Test]
    public async Task UseRedisStreamsTransport_Configures_options()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ = services.AddPulse(config =>
            config.UseRedisStreamsTransport(options => options.StreamKey = "custom:stream")
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<RedisStreamsTransportOptions>>();

            _ = await Assert.That(options.Value.StreamKey).IsEqualTo("custom:stream");
        }
    }

    [Test]
    public async Task UseRedisStreamsTransport_Without_configureOptions_Default_options_are_valid()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ = services.AddPulse(config => config.UseRedisStreamsTransport());

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<RedisStreamsTransportOptions>>();

            _ = await Assert.That(options.Value.StreamKey).IsEqualTo("pulse:outbox");
        }
    }

    [Test]
    public async Task UseRedisStreamsTransport_When_configurator_null_throws()
    {
        IMediatorBuilder configurator = null!;

        var exception = Assert.Throws<ArgumentNullException>(() => configurator.UseRedisStreamsTransport());

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception.ParamName).IsEqualTo("configurator");
    }

    [Test]
    public async Task UseRedisStreamsTransport_Returns_configurator_for_chaining()
    {
        IServiceCollection services = new ServiceCollection();
        IMediatorBuilder? returnedConfigurator = null;

        _ = services.AddPulse(config => returnedConfigurator = config.UseRedisStreamsTransport());

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
