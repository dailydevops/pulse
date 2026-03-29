namespace NetEvolve.Pulse.AzureServiceBus.Tests.Unit;

using Azure;
using Azure.Messaging.ServiceBus;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class ServiceBusAdministrationClientAdapterTests
{
    [Test]
    public async Task TryGetQueueRuntimePropertiesAsync_returns_true_on_success()
    {
        var adapter = new ServiceBusAdministrationClientAdapter(
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask
        );

        var result = await adapter.TryGetQueueRuntimePropertiesAsync("queue", CancellationToken.None);

        _ = await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryGetQueueRuntimePropertiesAsync_returns_false_on_request_failed()
    {
        var adapter = new ServiceBusAdministrationClientAdapter(
            (_, _) => throw new RequestFailedException("not found"),
            (_, _) => Task.CompletedTask
        );

        var result = await adapter.TryGetQueueRuntimePropertiesAsync("queue", CancellationToken.None);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TryGetTopicRuntimePropertiesAsync_returns_false_on_service_bus_exception()
    {
        var adapter = new ServiceBusAdministrationClientAdapter(
            (_, _) => Task.CompletedTask,
            (_, _) => throw new ServiceBusException("service unavailable", ServiceBusFailureReason.ServiceBusy)
        );

        var result = await adapter.TryGetTopicRuntimePropertiesAsync("topic", CancellationToken.None);

        _ = await Assert.That(result).IsFalse();
    }
}
