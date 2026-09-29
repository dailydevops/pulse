namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Verifies the item-level <c>ttl</c> values written by <see cref="CosmosDbOutboxRepository"/>.
/// Undelivered documents must carry <c>ttl = -1</c> when TTL is enabled, because a missing <c>ttl</c>
/// inherits a positive container <c>DefaultTimeToLive</c> and the TTL engine would delete the message.
/// </summary>
[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxRepositoryTimeToLiveTests
{
    private const int TtlSeconds = 3600;

    [Test]
    public async Task AddAsync_WithTtlEnabled_CreatesDocumentThatNeverExpires(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CosmosDbOutboxDocument? created = null;
        var container = new FakeCosmosContainer
        {
            OnCreateItem = item =>
            {
                created = (CosmosDbOutboxDocument)item;
                return new FakeItemResponse<CosmosDbOutboxDocument>(created);
            },
        };

        var repository = CreateRepository(container, enableTtl: true);

        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(created).IsNotNull();
            _ = await Assert.That(created!.Ttl).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task AddAsync_WithTtlDisabled_CreatesDocumentWithoutTtl(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CosmosDbOutboxDocument? created = null;
        var container = new FakeCosmosContainer
        {
            OnCreateItem = item =>
            {
                created = (CosmosDbOutboxDocument)item;
                return new FakeItemResponse<CosmosDbOutboxDocument>(created);
            },
        };

        var repository = CreateRepository(container, enableTtl: false);

        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(created).IsNotNull();
            _ = await Assert.That(created!.Ttl).IsNull();
        }
    }

    [Test]
    public async Task GetPendingAsync_WithTtlEnabled_ClaimPatchesTtlToNeverExpire(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patches = await ClaimAsync(enableTtl: true, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(patches.Any(p => p is PatchOperation<int> { Path: "/ttl", Value: -1 })).IsTrue();
    }

    [Test]
    public async Task GetPendingAsync_WithTtlDisabled_ClaimDoesNotPatchTtl(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patches = await ClaimAsync(enableTtl: false, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(patches.Any(p => p.Path == "/ttl")).IsFalse();
    }

    [Test]
    public async Task MarkAsCompletedAsync_WithTtlEnabled_PatchesTtlToTtlSeconds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        var patches = await SettleAsync(
                messageId,
                repository => repository.MarkAsCompletedAsync(messageId, cancellationToken)
            )
            .ConfigureAwait(false);

        _ = await Assert.That(patches.Any(p => p is PatchOperation<int> { Path: "/ttl", Value: TtlSeconds })).IsTrue();
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_WithTtlEnabled_PatchesTtlToTtlSeconds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        var patches = await SettleAsync(
                messageId,
                repository => repository.MarkAsDeadLetterAsync(messageId, "boom", cancellationToken)
            )
            .ConfigureAwait(false);

        _ = await Assert.That(patches.Any(p => p is PatchOperation<int> { Path: "/ttl", Value: TtlSeconds })).IsTrue();
    }

    [Test]
    public async Task MarkAsFailedAsync_WithTtlEnabled_PatchesTtlToNeverExpire(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        var patches = await SettleAsync(
                messageId,
                repository => repository.MarkAsFailedAsync(messageId, "boom", cancellationToken)
            )
            .ConfigureAwait(false);

        _ = await Assert.That(patches.Any(p => p is PatchOperation<int> { Path: "/ttl", Value: -1 })).IsTrue();
    }

    [Test]
    public async Task MarkAsFailedAsync_WithNextRetryAtAndTtlEnabled_PatchesTtlToNeverExpire(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        var patches = await SettleAsync(
                messageId,
                repository =>
                    repository.MarkAsFailedAsync(
                        messageId,
                        "boom",
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        cancellationToken
                    )
            )
            .ConfigureAwait(false);

        _ = await Assert.That(patches.Any(p => p is PatchOperation<int> { Path: "/ttl", Value: -1 })).IsTrue();
    }

    private static async Task<IReadOnlyList<PatchOperation>> ClaimAsync(
        bool enableTtl,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        IReadOnlyList<PatchOperation> captured = [];

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [CreateDocument(messageId, status: 0)],
                ]),
            OnPatchItem = (_, _, patches, _) =>
            {
                captured = patches;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, status: 1));
            },
        };

        var repository = CreateRepository(container, enableTtl);

        _ = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        return captured;
    }

    private static async Task<IReadOnlyList<PatchOperation>> SettleAsync(
        Guid messageId,
        Func<CosmosDbOutboxRepository, Task> settle
    )
    {
        IReadOnlyList<PatchOperation> captured = [];

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, patches, _) =>
            {
                captured = patches;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, status: 2));
            },
        };

        await settle(CreateRepository(container, enableTtl: true)).ConfigureAwait(false);

        return captured;
    }

    private static OutboxMessage CreateMessage() =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(string),
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static CosmosDbOutboxDocument CreateDocument(Guid id, int status) =>
        new CosmosDbOutboxDocument
        {
            Id = id.ToString(),
            EventType = typeof(string).AssemblyQualifiedName!,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = status,
        };

    private static CosmosDbOutboxRepository CreateRepository(FakeCosmosContainer container, bool enableTtl)
    {
        using var client = new FakeCosmosClient(container);

        return new CosmosDbOutboxRepository(
            client,
            Options.Create(
                new CosmosDbOutboxOptions
                {
                    DatabaseName = "TestDb",
                    EnableTimeToLive = enableTtl,
                    TtlSeconds = TtlSeconds,
                }
            ),
            TimeProvider.System
        );
    }
}
