namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxRepositoryMarkAsTests
{
    [Test]
    public async Task MarkAsCompletedAsync_WithTtlDisabled_PatchesStatusWithoutTtl(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        IReadOnlyList<PatchOperation>? capturedPatches = null;

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, patches, _) =>
            {
                capturedPatches = patches;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, 2));
            },
        };

        var repository = CreateRepository(container, enableTtl: false);

        await repository.MarkAsCompletedAsync(messageId, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(container.PatchItemCalls).IsEqualTo(1);
            _ = await Assert.That(capturedPatches).IsNotNull();
            _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/ttl")).IsFalse();
        }
    }

    [Test]
    public async Task MarkAsCompletedAsync_WithTtlEnabled_PatchesTtlField(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        IReadOnlyList<PatchOperation>? capturedPatches = null;

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, patches, _) =>
            {
                capturedPatches = patches;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, 2));
            },
        };

        var repository = CreateRepository(container, enableTtl: true);

        await repository.MarkAsCompletedAsync(messageId, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/ttl")).IsTrue();
    }

    [Test]
    public async Task MarkAsFailedAsync_WithoutNextRetryAt_IncrementsRetryCountAndSetsError(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        IReadOnlyList<PatchOperation>? capturedPatches = null;

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, patches, _) =>
            {
                capturedPatches = patches;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, 3));
            },
        };

        var repository = CreateRepository(container, enableTtl: false);

        await repository.MarkAsFailedAsync(messageId, "boom", cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(container.PatchItemCalls).IsEqualTo(1);
            _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/retryCount")).IsTrue();
            _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/error")).IsTrue();
            _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/nextRetryAt")).IsFalse();
        }
    }

    [Test]
    public async Task MarkAsFailedAsync_WithNextRetryAt_PatchesNextRetryAtField(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        IReadOnlyList<PatchOperation>? capturedPatches = null;

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, patches, _) =>
            {
                capturedPatches = patches;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, 3));
            },
        };

        var repository = CreateRepository(container, enableTtl: false);

        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await repository.MarkAsFailedAsync(messageId, "boom", nextRetryAt, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/nextRetryAt")).IsTrue();
    }

    [Test]
    public async Task MarkAsFailedAsync_WithNullNextRetryAt_StillPatchesNextRetryAtFieldAsNull(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        IReadOnlyList<PatchOperation>? capturedPatches = null;

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, patches, _) =>
            {
                capturedPatches = patches;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, 3));
            },
        };

        var repository = CreateRepository(container, enableTtl: false);

        await repository
            .MarkAsFailedAsync(messageId, "boom", nextRetryAt: null, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/nextRetryAt")).IsTrue();
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_WithTtlEnabled_PatchesStatusErrorAndTtl(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        IReadOnlyList<PatchOperation>? capturedPatches = null;

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, patches, _) =>
            {
                capturedPatches = patches;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, 4));
            },
        };

        var repository = CreateRepository(container, enableTtl: true);

        await repository.MarkAsDeadLetterAsync(messageId, "fatal", cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/status")).IsTrue();
            _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/error")).IsTrue();
            _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/ttl")).IsTrue();
        }
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_WithTtlDisabled_DoesNotPatchTtl(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        IReadOnlyList<PatchOperation>? capturedPatches = null;

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, patches, _) =>
            {
                capturedPatches = patches;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, 4));
            },
        };

        var repository = CreateRepository(container, enableTtl: false);

        await repository.MarkAsDeadLetterAsync(messageId, "fatal", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capturedPatches!.Any(p => p.Path == "/ttl")).IsFalse();
    }

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
                    TtlSeconds = 3600,
                }
            ),
            TimeProvider.System
        );
    }
}
