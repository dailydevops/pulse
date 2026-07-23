namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Mapping and serialization invariants for <see cref="CosmosDbOutboxDocument"/>.
/// The Cosmos document is persisted via the official SDK using both Newtonsoft.Json
/// (default container) and System.Text.Json (when CosmosSystemTextJsonSerializer is configured),
/// so both attribute sets must be intact and the round trip must be lossless.
/// </summary>
[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxDocumentTests
{
    private sealed record SampleEvent;

    private static OutboxMessage CreateMessage(OutboxMessageStatus status = OutboxMessageStatus.Pending) =>
        new OutboxMessage
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            EventType = typeof(SampleEvent),
            Payload = "{\"value\":42}",
            CorrelationId = "corr-id",
            CausationId = "cause-id",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2025, 1, 1, 10, 5, 0, TimeSpan.Zero),
            ProcessedAt = new DateTimeOffset(2025, 1, 1, 10, 5, 0, TimeSpan.Zero),
            NextRetryAt = new DateTimeOffset(2025, 1, 1, 11, 0, 0, TimeSpan.Zero),
            RetryCount = 3,
            Error = "boom",
            Status = status,
        };

    // INVARIANT: Round-trip OutboxMessage -> CosmosDbOutboxDocument -> OutboxMessage
    // must preserve every field value-for-value. Any silent loss breaks at-least-once
    // delivery semantics, since the dispatcher uses the reconstituted EventType / Payload
    // to drive the in-process handler.
    [Test]
    public async Task FromOutboxMessage_then_ToOutboxMessage_is_lossless()
    {
        var original = CreateMessage();

        var doc = CosmosDbOutboxDocument.FromOutboxMessage(original);
        var roundtrip = doc.ToOutboxMessage();

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

    // INVARIANT: FromOutboxMessage rejects null - guards against accidental NRE in callers.
    [Test]
    public async Task FromOutboxMessage_with_null_throws() =>
        _ = await Assert.That(() => CosmosDbOutboxDocument.FromOutboxMessage(null!)).Throws<ArgumentNullException>();

    // INVARIANT: The document uses System.Text.Json attributes (verified via serialization)
    // because the project explicitly opts into CosmosSystemTextJsonSerializer. The JSON field
    // names must be lowerCamel as documented in OutboxMessageSchema, and the optional `ttl`
    // field must be omitted when null (Cosmos otherwise interprets ttl=0 as "expire now").
    [Test]
    public async Task System_Text_Json_Serialization_Uses_camel_case_keys_and_omits_null_ttl()
    {
        var doc = CosmosDbOutboxDocument.FromOutboxMessage(CreateMessage());
        // Ttl deliberately not set (null) - must NOT appear in the JSON output.

        var json = JsonSerializer.Serialize(doc);

        using (Assert.Multiple())
        {
            _ = await Assert.That(json).Contains("\"id\":");
            _ = await Assert.That(json).Contains("\"eventType\":");
            _ = await Assert.That(json).Contains("\"payload\":");
            _ = await Assert.That(json).Contains("\"correlationId\":");
            _ = await Assert.That(json).Contains("\"createdAt\":");
            _ = await Assert.That(json).Contains("\"status\":");
            // TTL must be omitted when null (otherwise Cosmos interprets ttl=0 as immediate expiry).
            _ = await Assert.That(json).DoesNotContain("\"ttl\":");
        }
    }

    // INVARIANT: When TTL is set (e.g. via PatchOperation in MarkAsCompletedAsync), it MUST
    // serialize to the `ttl` field so the Cosmos TTL engine can evict the document.
    [Test]
    public async Task System_Text_Json_Serialization_Includes_ttl_when_set()
    {
        var doc = CosmosDbOutboxDocument.FromOutboxMessage(CreateMessage());
        doc.Ttl = 86_400;

        var json = JsonSerializer.Serialize(doc);

        _ = await Assert.That(json).Contains("\"ttl\":86400");
    }

    // INVARIANT: The status enum must round-trip via its INTEGER value (not name).
    // Storing as int matches OutboxMessageSchema.Columns.Status and lets the query
    // `WHERE c.status = 0` work across all providers.
    [Test]
    public async Task Status_Serializes_as_integer()
    {
        var doc = CosmosDbOutboxDocument.FromOutboxMessage(CreateMessage(OutboxMessageStatus.DeadLetter));

        var json = JsonSerializer.Serialize(doc);

        // DeadLetter = 4
        _ = await Assert.That(json).Contains("\"status\":4");
    }
}
