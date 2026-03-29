namespace NetEvolve.Pulse.AzureServiceBus.Tests.Unit;

using Azure.Messaging.ServiceBus;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class ServiceBusSenderAdapterTests
{
    [Test]
    public async Task CreateMessageBatchAsync_returns_delegate_result()
    {
        var expectedBatch = new TestBatch();
        await using var adapter = new ServiceBusSenderAdapter(
            _ => Task.FromResult<IServiceBusMessageBatch>(expectedBatch),
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            () => ValueTask.CompletedTask
        );

        var batch = await adapter.CreateMessageBatchAsync(CancellationToken.None);

        _ = await Assert.That(batch).IsSameReferenceAs(expectedBatch);
    }

    [Test]
    public async Task SendMessageAsync_invokes_delegate()
    {
        var called = false;
        await using var adapter = new ServiceBusSenderAdapter(
            _ => Task.FromResult<IServiceBusMessageBatch>(new TestBatch()),
            (_, _) =>
            {
                called = true;
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask,
            () => ValueTask.CompletedTask
        );

        await adapter.SendMessageAsync(new ServiceBusMessage(), CancellationToken.None);

        _ = await Assert.That(called).IsTrue();
    }

    [Test]
    public async Task SendMessagesAsync_invokes_delegate_with_batch()
    {
        var sentBatch = default(IServiceBusMessageBatch);
        var batch = new TestBatch();
        await using var adapter = new ServiceBusSenderAdapter(
            _ => Task.FromResult<IServiceBusMessageBatch>(batch),
            (_, _) => Task.CompletedTask,
            (b, _) =>
            {
                sentBatch = b;
                return Task.CompletedTask;
            },
            () => ValueTask.CompletedTask
        );

        await adapter.SendMessagesAsync(batch, CancellationToken.None);

        _ = await Assert.That(sentBatch).IsSameReferenceAs(batch);
    }

    [Test]
    public async Task DisposeAsync_invokes_delegate()
    {
        var disposed = false;
        await using var adapter = new ServiceBusSenderAdapter(
            _ => Task.FromResult<IServiceBusMessageBatch>(new TestBatch()),
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            () =>
            {
                disposed = true;
                return ValueTask.CompletedTask;
            }
        );

        await adapter.DisposeAsync();

        _ = await Assert.That(disposed).IsTrue();
    }

    private sealed class TestBatch : IServiceBusMessageBatch
    {
        public ServiceBusMessageBatch InnerBatch =>
            throw new NotSupportedException("Inner batch is not used in the test batch.");

        public bool TryAddMessage(ServiceBusMessage message) => true;
    }
}
