namespace NetEvolve.Pulse.Tests.Unit.AzureServiceBus;

using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class AzureServiceBusExtensionsTests
{
    private const string FakeConnectionString =
        "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";

    [Test]
    public void UseAzureServiceBusTransport_When_configurator_is_null_throws_ArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>("configurator", () => configurator!.UseAzureServiceBusTransport());
    }

    [Test]
    public async Task UseAzureServiceBusTransport_registers_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options => options.ConnectionString = FakeConnectionString)
        );

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(AzureServiceBusMessageTransport));
    }

    [Test]
    public async Task UseAzureServiceBusTransport_registers_ServiceBusClient_as_singleton()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options => options.ConnectionString = FakeConnectionString)
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
    public async Task UseAzureServiceBusTransport_validates_required_options()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseAzureServiceBusTransport());

        await using var provider = services.BuildServiceProvider();

        _ = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ServiceBusClient>());
    }

    [Test]
    public async Task UseAzureServiceBusTransport_with_fully_qualified_namespace_creates_ServiceBusClient()
    {
        IServiceCollection services = new ServiceCollection();

        // Register a fake credential so we don't depend on DefaultAzureCredential
        _ = services.AddSingleton<TokenCredential>(new FakeTokenCredential());

        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options =>
                options.FullyQualifiedNamespace = "contoso.servicebus.windows.net"
            )
        );

        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<ServiceBusClient>();
        _ = await Assert.That(client).IsNotNull();
        _ = await Assert.That(client.FullyQualifiedNamespace).IsEqualTo("contoso.servicebus.windows.net");
    }

    [Test]
    public async Task UseAzureServiceBusTransport_with_connection_string_creates_ServiceBusClient()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options => options.ConnectionString = FakeConnectionString)
        );

        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<ServiceBusClient>();
        _ = await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task UseAzureServiceBusTransport_replaces_existing_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options => options.ConnectionString = FakeConnectionString)
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
