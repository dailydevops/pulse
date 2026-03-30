namespace NetEvolve.Pulse.AzureServiceBus.Tests.Unit;

using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class AzureServiceBusMediatorConfiguratorExtensionsTests
{
    private const string FakeConnectionString =
        "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";

    [Test]
    public void UseAzureServiceBusTransport_When_configurator_is_null_throws_ArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;

        _ = Assert.Throws<ArgumentNullException>("configurator", () => configurator!.UseAzureServiceBusTransport());
    }

    [Test]
    public async Task UseAzureServiceBusTransport_registers_transport_and_adapters()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options =>
            {
                options.ConnectionString = FakeConnectionString;
            })
        );

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(AzureServiceBusMessageTransport));
    }

    [Test]
    public async Task UseAzureServiceBusTransport_registers_ServiceBusClient_as_singleton()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options =>
            {
                options.ConnectionString = FakeConnectionString;
            })
        );

        var descriptor = services.Single(d => d.ServiceType == typeof(ServiceBusClient));
        _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
    }

    [Test]
    public async Task UseAzureServiceBusTransport_registers_DefaultAzureCredential_as_TokenCredential()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseAzureServiceBusTransport());

        var descriptor = services.Single(d => d.ServiceType == typeof(TokenCredential));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(DefaultAzureCredential));
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
    public async Task UseAzureServiceBusTransport_with_fully_qualified_namespace_does_not_throw_on_validation()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options =>
            {
                options.FullyQualifiedNamespace = "contoso.servicebus.windows.net";
            })
        );

        // Options are valid when FullyQualifiedNamespace is set – no exception expected during build
        using var provider = services.BuildServiceProvider();

        // ServiceBusClient registration itself should not throw during validation
        // (It requires a TokenCredential, which DefaultAzureCredential provides at runtime)
        var descriptor = services.Single(d => d.ServiceType == typeof(ServiceBusClient));
        _ = await Assert.That(descriptor).IsNotNull();
    }

    [Test]
    public async Task UseAzureServiceBusTransport_replaces_existing_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options =>
            {
                options.ConnectionString = FakeConnectionString;
            })
        );

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(AzureServiceBusMessageTransport));
    }

    [Test]
    public async Task UseAzureServiceBusTransport_does_not_replace_custom_TokenCredential()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<TokenCredential>(new FakeTokenCredential());
        _ = services.AddPulse(config => config.UseAzureServiceBusTransport());

        var descriptors = services.Where(d => d.ServiceType == typeof(TokenCredential)).ToList();

        // TryAdd should NOT replace the custom credential already registered
        _ = await Assert.That(descriptors).Count().IsEqualTo(1);
        _ = await Assert.That(descriptors[0].ImplementationInstance).IsTypeOf<FakeTokenCredential>();
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

    private sealed class FakeTokenCredential : TokenCredential
    {
        // Returns a minimal placeholder token for testing purposes only – not used to authenticate
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(new AccessToken("fake-token", DateTimeOffset.MaxValue));
    }
}
