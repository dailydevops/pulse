namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkAuditStoreTests
{
    private static TestAuditStoreDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestAuditStoreDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new TestAuditStoreDbContext(options);
    }

    private static EntityFrameworkAuditStore<TestAuditStoreDbContext> CreateStore(TestAuditStoreDbContext context) =>
        new(context);

    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new EntityFrameworkAuditStore<TestAuditStoreDbContext>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidContext_CreatesInstance()
    {
        var context = CreateContext(nameof(Constructor_WithValidContext_CreatesInstance));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert.That(store).IsNotNull();
        }
    }

    [Test]
    public async Task RecordAsync_WithNullRecord_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = CreateContext(nameof(RecordAsync_WithNullRecord_ThrowsArgumentNullException));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert
                .That(async () => await store.RecordAsync(null!, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task RecordAsync_WithValidRecord_InsertsNewRow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = CreateContext(nameof(RecordAsync_WithValidRecord_InsertsNewRow));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);
            var record = new AuditRecord
            {
                Id = Guid.NewGuid(),
                CommandType = "MyApp.Commands.CreateOrderCommand",
                UserId = "user-42",
                CorrelationId = "correlation-1",
                OccurredAt = DateTimeOffset.UtcNow,
                DurationMs = 12.5,
                Result = AuditResult.Success,
                Payload = "{\"orderId\":42}",
            };

            await store.RecordAsync(record, cancellationToken).ConfigureAwait(false);

            var entry = await context.AuditEntries.SingleAsync(cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(entry.Id).IsEqualTo(record.Id);
                _ = await Assert.That(entry.CommandType).IsEqualTo("MyApp.Commands.CreateOrderCommand");
                _ = await Assert.That(entry.UserId).IsEqualTo("user-42");
                _ = await Assert.That(entry.CorrelationId).IsEqualTo("correlation-1");
                _ = await Assert.That(entry.DurationMs).IsEqualTo(12.5);
                _ = await Assert.That(entry.Result).IsEqualTo(AuditResult.Success);
                _ = await Assert.That(entry.Payload).IsEqualTo("{\"orderId\":42}");
            }
        }
    }

    [Test]
    public async Task RecordAsync_CalledTwice_StoresTwoDistinctEntries(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = CreateContext(nameof(RecordAsync_CalledTwice_StoresTwoDistinctEntries));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            await store
                .RecordAsync(
                    new AuditRecord
                    {
                        Id = Guid.NewGuid(),
                        CommandType = "Some.Command",
                        OccurredAt = DateTimeOffset.UtcNow,
                        Result = AuditResult.Success,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            await store
                .RecordAsync(
                    new AuditRecord
                    {
                        Id = Guid.NewGuid(),
                        CommandType = "Some.Command",
                        OccurredAt = DateTimeOffset.UtcNow,
                        Result = AuditResult.Failure,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            var count = await context.AuditEntries.CountAsync(cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(count).IsEqualTo(2);
        }
    }
}
