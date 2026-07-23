namespace NetEvolve.Pulse.Tests.Unit.MongoDB;

using System;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Invariants for <see cref="OutboxDocumentMapper"/>.
/// BSON's native date type carries no timezone information; the contract is that all
/// <see cref="DateTimeOffset"/> values are stored as UTC <see cref="DateTime"/> and re-read
/// with <see cref="TimeSpan.Zero"/> offset. The mapper must enforce that on both sides
/// of the round trip.
/// </summary>
[TestGroup("MongoDB")]
public sealed class OutboxDocumentMapperTests
{
    private sealed record SampleEvent;

    private static OutboxMessage CreateMessage() =>
        new OutboxMessage
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            EventType = typeof(SampleEvent),
            Payload = "{\"n\":1}",
            CorrelationId = "corr",
            CausationId = "cause",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2025, 1, 1, 10, 5, 0, TimeSpan.Zero),
            ProcessedAt = new DateTimeOffset(2025, 1, 1, 10, 5, 0, TimeSpan.Zero),
            NextRetryAt = new DateTimeOffset(2025, 1, 1, 11, 0, 0, TimeSpan.Zero),
            RetryCount = 2,
            Error = "x",
            Status = OutboxMessageStatus.Failed,
        };

    // INVARIANT: ToDocument/ToOutboxMessage round-trip preserves every field.
    [Test]
    public async Task ToDocument_then_ToOutboxMessage_is_lossless()
    {
        var original = CreateMessage();

        var doc = OutboxDocumentMapper.ToDocument(original);
        var roundtrip = OutboxDocumentMapper.ToOutboxMessage(doc);

        using (Assert.Multiple())
        {
            _ = await Assert.That(roundtrip.Id).IsEqualTo(original.Id);
            _ = await Assert.That(roundtrip.EventType).IsEqualTo(original.EventType);
            _ = await Assert.That(roundtrip.Payload).IsEqualTo(original.Payload);
            _ = await Assert.That(roundtrip.CorrelationId).IsEqualTo(original.CorrelationId);
            _ = await Assert.That(roundtrip.CausationId).IsEqualTo(original.CausationId);
            _ = await Assert.That(roundtrip.CreatedAt).IsEqualTo(original.CreatedAt);
            _ = await Assert.That(roundtrip.UpdatedAt).IsEqualTo(original.UpdatedAt);
            _ = await Assert.That(roundtrip.ProcessedAt).IsEqualTo(original.ProcessedAt);
            _ = await Assert.That(roundtrip.NextRetryAt).IsEqualTo(original.NextRetryAt);
            _ = await Assert.That(roundtrip.RetryCount).IsEqualTo(original.RetryCount);
            _ = await Assert.That(roundtrip.Error).IsEqualTo(original.Error);
            _ = await Assert.That(roundtrip.Status).IsEqualTo(original.Status);
        }
    }

    // INVARIANT: ToDocument stores DateTime as UTC (Kind == Utc), so that BSON's
    // tzless date type preserves wall-clock UTC across the round trip.
    [Test]
    public async Task ToDocument_Stores_DateTime_with_Utc_kind()
    {
        var original = CreateMessage();

        var doc = OutboxDocumentMapper.ToDocument(original);

        using (Assert.Multiple())
        {
            _ = await Assert.That(doc.CreatedAt.Kind).IsEqualTo(DateTimeKind.Utc);
            _ = await Assert.That(doc.UpdatedAt.Kind).IsEqualTo(DateTimeKind.Utc);
            _ = await Assert.That(doc.ProcessedAt!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
            _ = await Assert.That(doc.NextRetryAt!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        }
    }

    // INVARIANT: A non-UTC DateTimeOffset (e.g. local time) is still serialized as
    // its UTC equivalent. The mapper's contract is "UTC in BSON, regardless of input."
    [Test]
    public async Task ToDocument_Converts_offset_DateTimes_to_Utc_wall_clock()
    {
        // 12:00 in +02:00 zone == 10:00 UTC.
        var withOffset = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.FromHours(2));
        var msg = CreateMessage();
        msg.CreatedAt = withOffset;

        var doc = OutboxDocumentMapper.ToDocument(msg);

        _ = await Assert.That(doc.CreatedAt).IsEqualTo(new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));
    }

    // INVARIANT: ToOutboxMessage re-attaches DateTimeKind.Utc on parsed values whose
    // Kind comes back as Unspecified from the BSON deserializer. Without
    // SpecifyKind(...Utc), the resulting DateTimeOffset would erroneously apply the local
    // machine offset and shift the wall-clock value.
    [Test]
    public async Task ToOutboxMessage_Reattaches_Utc_kind_when_BSON_date_kind_is_Unspecified()
    {
        var doc = new OutboxDocument
        {
            Id = Guid.NewGuid(),
            EventType = typeof(SampleEvent).AssemblyQualifiedName!,
            Payload = "{}",
            // Simulate the BSON deserializer producing Unspecified-kind values.
            CreatedAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Unspecified),
            UpdatedAt = new DateTime(2025, 1, 1, 10, 5, 0, DateTimeKind.Unspecified),
            ProcessedAt = null,
            NextRetryAt = null,
            RetryCount = 0,
            Status = (int)OutboxMessageStatus.Pending,
        };

        var msg = OutboxDocumentMapper.ToOutboxMessage(doc);

        using (Assert.Multiple())
        {
            _ = await Assert.That(msg.CreatedAt.Offset).IsEqualTo(TimeSpan.Zero);
            _ = await Assert
                .That(msg.CreatedAt.UtcDateTime)
                .IsEqualTo(new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        }
    }

    // INVARIANT: Unresolvable event type causes the mapper to fail loudly, so the dispatcher
    // never silently swallows a typo-injected payload.
    [Test]
    public async Task ToOutboxMessage_with_Unresolvable_EventType_throws()
    {
        var doc = new OutboxDocument
        {
            Id = Guid.NewGuid(),
            EventType = "Nonexistent.Type, Nonexistent.Assembly",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Status = (int)OutboxMessageStatus.Pending,
        };

        _ = await Assert.That(() => OutboxDocumentMapper.ToOutboxMessage(doc)).Throws<InvalidOperationException>();
    }
}
