namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

    [Test]
    [Arguments(MarkOperation.Completed)]
    [Arguments(MarkOperation.Failed)]
    [Arguments(MarkOperation.FailedWithRetry)]
    [Arguments(MarkOperation.DeadLetter)]
    public async Task MarkAsAsync_Always_SendsProcessingFilterPredicate(
        MarkOperation operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        PatchItemRequestOptions? capturedOptions = null;

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, _, options) =>
            {
                capturedOptions = options;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, 2));
            },
        };

        var repository = CreateRepository(container, enableTtl: false);

        await InvokeAsync(repository, operation, messageId, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capturedOptions).IsNotNull();
        _ = await Assert.That(capturedOptions!.FilterPredicate).IsEqualTo("FROM c WHERE c.status = 1");
    }

    [Test]
    [Arguments(MarkOperation.Completed, HttpStatusCode.PreconditionFailed)]
    [Arguments(MarkOperation.Failed, HttpStatusCode.PreconditionFailed)]
    [Arguments(MarkOperation.FailedWithRetry, HttpStatusCode.PreconditionFailed)]
    [Arguments(MarkOperation.DeadLetter, HttpStatusCode.PreconditionFailed)]
    [Arguments(MarkOperation.Completed, HttpStatusCode.NotFound)]
    [Arguments(MarkOperation.Failed, HttpStatusCode.NotFound)]
    [Arguments(MarkOperation.FailedWithRetry, HttpStatusCode.NotFound)]
    [Arguments(MarkOperation.DeadLetter, HttpStatusCode.NotFound)]
    public async Task MarkAsAsync_WhenMessageNotProcessing_IsNoOp(
        MarkOperation operation,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, _, _) => throw new CosmosException("skipped", statusCode, 0, "activity", 0),
        };

        var repository = CreateRepository(container, enableTtl: false);

        await InvokeAsync(repository, operation, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(container.PatchItemCalls).IsEqualTo(1);
    }

    [Test]
    public async Task MarkAsCompletedAsync_WhenPatchFailsOtherwise_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, _, _) =>
                throw new CosmosException("throttled", HttpStatusCode.TooManyRequests, 0, "activity", 0),
        };

        var repository = CreateRepository(container, enableTtl: false);

        _ = await Assert
            .That(async () => await repository.MarkAsCompletedAsync(Guid.NewGuid(), cancellationToken))
            .ThrowsExactly<CosmosException>();
    }

    public enum MarkOperation
    {
        Completed,
        Failed,
        FailedWithRetry,
        DeadLetter,
    }

    private static Task InvokeAsync(
        CosmosDbOutboxRepository repository,
        MarkOperation operation,
        Guid messageId,
        CancellationToken cancellationToken
    ) =>
        operation switch
        {
            MarkOperation.Completed => repository.MarkAsCompletedAsync(messageId, cancellationToken),
            MarkOperation.Failed => repository.MarkAsFailedAsync(messageId, "boom", cancellationToken),
            MarkOperation.FailedWithRetry => repository.MarkAsFailedAsync(
                messageId,
                "boom",
                DateTimeOffset.UtcNow.AddMinutes(5),
                cancellationToken
            ),
            _ => repository.MarkAsDeadLetterAsync(messageId, "fatal", cancellationToken),
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
                    TtlSeconds = 3600,
                }
            ),
            TimeProvider.System
        );
    }
}
