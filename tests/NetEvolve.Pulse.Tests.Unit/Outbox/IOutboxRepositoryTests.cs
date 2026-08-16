namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Unit tests for the default interface implementations on <see cref="IOutboxRepository"/>,
/// in particular the batch overloads of <c>MarkAsCompletedAsync</c>, <c>MarkAsFailedAsync</c>,
/// and <c>MarkAsDeadLetterAsync</c>, which invoke the single-message methods on the same
/// repository instance for implementations that do not override the batch overloads.
/// </summary>
[TestGroup("Outbox")]
public sealed class IOutboxRepositoryTests
{
    [Test]
    public async Task MarkAsCompletedAsync_Batch_WithoutOverride_InvokesSingleItemSequentiallyInOrder(
        CancellationToken cancellationToken = default
    )
    {
        using var repository = new SingleItemOnlyRepository();
        var messageIds = CreateMessageIds();

        await ((IOutboxRepository)repository)
            .MarkAsCompletedAsync(messageIds, CancellationToken.None)
            .ConfigureAwait(false);

        _ = await Assert.That(repository.OverlapDetected).IsFalse();
        await AssertCallOrderAsync(repository, messageIds).ConfigureAwait(false);
    }

    [Test]
    public async Task MarkAsFailedAsync_Batch_WithoutOverride_InvokesSingleItemSequentiallyInOrder(
        CancellationToken cancellationToken = default
    )
    {
        using var repository = new SingleItemOnlyRepository();
        var messageIds = CreateMessageIds();

        await ((IOutboxRepository)repository)
            .MarkAsFailedAsync(messageIds, "error", CancellationToken.None)
            .ConfigureAwait(false);

        _ = await Assert.That(repository.OverlapDetected).IsFalse();
        await AssertCallOrderAsync(repository, messageIds).ConfigureAwait(false);
        _ = await Assert.That(repository.ErrorMessages.Count).IsEqualTo(messageIds.Count);
        foreach (var errorMessage in repository.ErrorMessages)
        {
            _ = await Assert.That(errorMessage).IsEqualTo("error");
        }
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_Batch_WithoutOverride_InvokesSingleItemSequentiallyInOrder(
        CancellationToken cancellationToken = default
    )
    {
        using var repository = new SingleItemOnlyRepository();
        var messageIds = CreateMessageIds();

        await ((IOutboxRepository)repository)
            .MarkAsDeadLetterAsync(messageIds, "error", CancellationToken.None)
            .ConfigureAwait(false);

        _ = await Assert.That(repository.OverlapDetected).IsFalse();
        await AssertCallOrderAsync(repository, messageIds).ConfigureAwait(false);
        _ = await Assert.That(repository.ErrorMessages.Count).IsEqualTo(messageIds.Count);
        foreach (var errorMessage in repository.ErrorMessages)
        {
            _ = await Assert.That(errorMessage).IsEqualTo("error");
        }
    }

    private static List<Guid> CreateMessageIds() => [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    private static async Task AssertCallOrderAsync(SingleItemOnlyRepository repository, List<Guid> messageIds)
    {
        _ = await Assert.That(repository.CallOrder.Count).IsEqualTo(messageIds.Count);
        for (var i = 0; i < messageIds.Count; i++)
        {
            _ = await Assert.That(repository.CallOrder[i]).IsEqualTo(messageIds[i]);
        }
    }

    /// <summary>
    /// Minimal repository implementing only the single-message mark methods, leaving the batch
    /// overloads to fall through to the default interface implementations. It records the invocation
    /// order and detects overlapping (concurrent) invocations on the same instance: the first call
    /// waits briefly for a subsequent call to start; if one starts before the first completes, the
    /// batch overload fans out concurrently rather than sequentially.
    /// </summary>
    private sealed class SingleItemOnlyRepository : IOutboxRepository, IDisposable
    {
        private readonly SemaphoreSlim _subsequentCallStarted = new(0);
        private int _activeCalls;
        private int _totalCalls;

        public bool OverlapDetected { get; private set; }

        public List<Guid> CallOrder { get; } = [];

        public List<string> ErrorMessages { get; } = [];

        public Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            RecordCallAsync(messageId, null, cancellationToken);

        public Task MarkAsFailedAsync(
            Guid messageId,
            string errorMessage,
            CancellationToken cancellationToken = default
        ) => RecordCallAsync(messageId, errorMessage, cancellationToken);

        public Task MarkAsDeadLetterAsync(
            Guid messageId,
            string errorMessage,
            CancellationToken cancellationToken = default
        ) => RecordCallAsync(messageId, errorMessage, cancellationToken);

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);

        public Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
            int maxRetryCount,
            int batchSize,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);

        public void Dispose() => _subsequentCallStarted.Dispose();

        private async Task RecordCallAsync(
            Guid messageId,
            string? errorMessage,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref _activeCalls) > 1)
            {
                OverlapDetected = true;
            }

            lock (CallOrder)
            {
                CallOrder.Add(messageId);
                if (errorMessage is not null)
                {
                    ErrorMessages.Add(errorMessage);
                }
            }

            if (Interlocked.Increment(ref _totalCalls) == 1)
            {
                _ = await _subsequentCallStarted.WaitAsync(500, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _ = _subsequentCallStarted.Release();
            }

            _ = Interlocked.Decrement(ref _activeCalls);
        }
    }
}
