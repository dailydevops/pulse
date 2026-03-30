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
    private const string FakeConnectionString =
        "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";

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
        await using var client = new ServiceBusClient(FakeConnectionString);
        var options = Options.Create(new AzureServiceBusTransportOptions());

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new AzureServiceBusMessageTransport(client, null!, options))
        );
    }

    [Test]
    public async Task Constructor_When_options_is_null_throws_ArgumentNullException()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new AzureServiceBusMessageTransport(client, resolver, null!))
        );
    }

    [Test]
    public async Task IsHealthyAsync_When_client_not_closed_returns_true()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_When_client_is_closed_returns_false()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());
        var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        // Dispose the transport early – this also closes the underlying client (IsClosed = true)
        await transport.DisposeAsync();

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task SendAsync_When_message_is_null_throws_ArgumentNullException()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(null!));
    }

    [Test]
    public async Task SendBatchAsync_When_messages_is_null_throws_ArgumentNullException()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendBatchAsync(null!));
    }

    [Test]
    public async Task SendBatchAsync_When_messages_is_empty_does_not_throw()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        await transport.SendBatchAsync([]);
    }

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => "test-topic";
    }
}
