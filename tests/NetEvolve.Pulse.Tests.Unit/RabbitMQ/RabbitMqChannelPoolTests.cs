namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("RabbitMQ")]
public sealed class RabbitMqChannelPoolTests
{
    [Test]
    public async Task Constructor_When_connectionAdapter_null_throws()
    {
        IRabbitMqConnectionAdapter connectionAdapter = null!;

        var exception = Assert.Throws<ArgumentNullException>(() => _ = new RabbitMqChannelPool(connectionAdapter, 10));

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception.ParamName).IsEqualTo("connectionAdapter");
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Constructor_When_maxChannelPoolSize_not_positive_throws(int maxChannelPoolSize)
    {
        var connectionAdapter = new FakeConnectionAdapter();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new RabbitMqChannelPool(connectionAdapter, maxChannelPoolSize)
        );

        _ = await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task RentAsync_When_pool_empty_creates_new_channel(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter();
        using var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        var channel = await pool.RentAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(channel).IsNotNull();
            _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Return_Of_open_channel_makes_it_available_for_next_rent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter();
        using var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        var channel = await pool.RentAsync(cancellationToken).ConfigureAwait(false);
        pool.Return(channel);

        var reused = await pool.RentAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reused).IsSameReferenceAs(channel);
            _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Return_Of_closed_channel_disposes_it_and_next_rent_creates_a_fresh_one(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter();
        using var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        var channel = (FakeChannelAdapter)await pool.RentAsync(cancellationToken).ConfigureAwait(false);
        channel.IsOpen = false; // Simulate broker-side channel closure while rented.
        pool.Return(channel);

        var next = await pool.RentAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(channel.DisposeCallCount).IsEqualTo(1);
            _ = await Assert.That(next).IsNotSameReferenceAs(channel);
            _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Return_Null_channel_throws()
    {
        var connectionAdapter = new FakeConnectionAdapter();
        using var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        var exception = Assert.Throws<ArgumentNullException>(() => pool.Return(null!));

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception.ParamName).IsEqualTo("channel");
    }

    [Test]
    public async Task RentAsync_ConcurrentCalls_AreCappedAtMaxChannelPoolSize(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const int MaxPoolSize = 3;
        var connectionAdapter = new FakeConnectionAdapter();
        using var pool = new RabbitMqChannelPool(connectionAdapter, MaxPoolSize);

        var rentTasks = new List<Task<IRabbitMqChannelAdapter>>();
        for (var i = 0; i < MaxPoolSize; i++)
        {
            // Each iteration converts a distinct ValueTask returned by RentAsync exactly once via
            // AsTask(); the analyzer cannot see across loop iterations that no ValueTask instance
            // is consumed twice.
#pragma warning disable S5034 // Refactor this 'ValueTask' usage to consume it only once
            rentTasks.Add(pool.RentAsync(cancellationToken).AsTask());
#pragma warning restore S5034
        }

        var rented = await Task.WhenAll(rentTasks)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
            .ConfigureAwait(false);

        // A further rent must block because the pool is exhausted.
        var blockedRent = pool.RentAsync(cancellationToken).AsTask();
        _ = await Task.WhenAny(blockedRent, Task.Delay(200, cancellationToken)).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(rented.Length).IsEqualTo(MaxPoolSize);
            _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(MaxPoolSize);
            _ = await Assert.That(blockedRent.IsCompleted).IsFalse();
        }

        // Release a slot so the blocked rent can complete, avoiding a hanging task.
        pool.Return(rented[0]);
        var unblocked = await blockedRent.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(unblocked).IsNotNull();
    }

    [Test]
    public async Task RentAsync_When_pool_exhausted_waits_until_a_channel_is_returned(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter();
        using var pool = new RabbitMqChannelPool(connectionAdapter, 1);

        var firstChannel = await pool.RentAsync(cancellationToken).ConfigureAwait(false);

        var secondRent = pool.RentAsync(cancellationToken).AsTask();
        _ = await Task.WhenAny(secondRent, Task.Delay(200, cancellationToken)).ConfigureAwait(false);
        _ = await Assert.That(secondRent.IsCompleted).IsFalse();

        pool.Return(firstChannel);

        var secondChannel = await secondRent
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(secondChannel).IsSameReferenceAs(firstChannel);
    }

    [Test]
    public async Task IsHealthyAsync_Reflects_connection_state(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter { IsOpen = false };
        using var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        var healthy = await pool.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_When_exception_thrown_returns_false(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter { IsOpen = true, ThrowOnIsOpen = true };
        using var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        var healthy = await pool.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_After_dispose_returns_false(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter { IsOpen = true };
        var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        pool.Dispose();

        var healthy = await pool.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task Dispose_Disposes_all_idle_channels(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter();
        var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        var channel1 = (FakeChannelAdapter)await pool.RentAsync(cancellationToken).ConfigureAwait(false);
        var channel2 = (FakeChannelAdapter)await pool.RentAsync(cancellationToken).ConfigureAwait(false);
        pool.Return(channel1);
        pool.Return(channel2);

        pool.Dispose();

        using (Assert.Multiple())
        {
            _ = await Assert.That(channel1.DisposeCallCount).IsEqualTo(1);
            _ = await Assert.That(channel2.DisposeCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Dispose_Is_idempotent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter();
        var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        var channel = (FakeChannelAdapter)await pool.RentAsync(cancellationToken).ConfigureAwait(false);
        pool.Return(channel);

        pool.Dispose();
        pool.Dispose();

        _ = await Assert.That(channel.DisposeCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task RentAsync_After_dispose_throws_ObjectDisposedException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter();
        var pool = new RabbitMqChannelPool(connectionAdapter, 10);

        pool.Dispose();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            pool.RentAsync(cancellationToken).AsTask()
        );

        _ = await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task RentAsync_When_channel_creation_fails_releases_rental_slot_and_rethrows(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter { ThrowOnCreateChannel = true };
        using var pool = new RabbitMqChannelPool(connectionAdapter, 1);

#pragma warning disable S5034 // Refactor this 'ValueTask' usage to consume it only once
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pool.RentAsync(cancellationToken).AsTask()
        );
        _ = await Assert.That(exception).IsNotNull();

        // The rental slot released by the failed attempt must be available for the next
        // caller instead of leaking, even though channel creation keeps failing.
        var secondAttempt = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pool.RentAsync(cancellationToken).AsTask()
        );
#pragma warning restore S5034
        _ = await Assert.That(secondAttempt).IsNotNull();
        _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(2);
    }

    [Test]
    public async Task Return_After_dispose_disposes_channel_without_throwing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionAdapter = new FakeConnectionAdapter();
        var pool = new RabbitMqChannelPool(connectionAdapter, 1);

        var channel = (FakeChannelAdapter)await pool.RentAsync(cancellationToken).ConfigureAwait(false);

        pool.Dispose();

        pool.Return(channel);

        _ = await Assert.That(channel.DisposeCallCount).IsEqualTo(1);
    }

    private sealed class FakeConnectionAdapter : IRabbitMqConnectionAdapter
    {
        private bool _isOpen = true;

        public bool IsOpen
        {
            get
            {
                if (ThrowOnIsOpen)
                {
                    throw new InvalidOperationException("Connection check failed");
                }

                return _isOpen;
            }
            set => _isOpen = value;
        }

        public bool ThrowOnIsOpen { get; set; }

        public bool ThrowOnCreateChannel { get; set; }

        public int CreateChannelCallCount { get; private set; }

        public Task<IRabbitMqChannelAdapter> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CreateChannelCallCount++;

            if (ThrowOnCreateChannel)
            {
                throw new InvalidOperationException("Channel creation failed");
            }

            return Task.FromResult<IRabbitMqChannelAdapter>(new FakeChannelAdapter());
        }
    }

    private sealed class FakeChannelAdapter : IRabbitMqChannelAdapter
    {
        private int _disposeCallCount;

        public bool IsOpen { get; set; } = true;

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public ValueTask BasicPublishAsync<TProperties>(
            string exchange,
            string routingKey,
            bool mandatory,
            TProperties basicProperties,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default
        )
            where TProperties : global::RabbitMQ.Client.IReadOnlyBasicProperties, global::RabbitMQ.Client.IAmqpHeader =>
            ValueTask.CompletedTask;

        public void Dispose() => Interlocked.Increment(ref _disposeCallCount);
    }
}
