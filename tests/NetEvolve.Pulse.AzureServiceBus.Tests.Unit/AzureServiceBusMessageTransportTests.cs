namespace NetEvolve.Pulse.AzureServiceBus.Tests.Unit;

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class AzureServiceBusMessageTransportTests
{
    [Test]
    public async Task Constructor_When_client_is_null_throws_ArgumentNullException()
    {
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new AzureServiceBusMessageTransport(null!, resolver, options))
        );
    }

    [Test]
    public async Task Constructor_When_resolver_is_null_throws_ArgumentNullException()
    {
        var connectionString =
            "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";
        await using var client = new ServiceBusClient(connectionString);
        var options = Options.Create(new AzureServiceBusTransportOptions());

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new AzureServiceBusMessageTransport(client, null!, options))
        );
    }

    [Test]
    public async Task Constructor_When_options_is_null_throws_ArgumentNullException()
    {
        var connectionString =
            "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";
        await using var client = new ServiceBusClient(connectionString);
        var resolver = new FakeTopicNameResolver();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new AzureServiceBusMessageTransport(client, resolver, null!))
        );
    }

    [Test]
    public async Task IsHealthyAsync_When_client_not_closed_returns_true()
    {
        var connectionString =
            "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";
        await using var client = new ServiceBusClient(connectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsTrue();
    }

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => "test-topic";
    }
}
