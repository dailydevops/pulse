namespace NetEvolve.Pulse.Outbox;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Internal BSON document model for persisting <see cref="OutboxMessage"/> in MongoDB.
/// Maps all outbox fields to BSON element names matching <see cref="OutboxMessageSchema.Columns"/> constants.
/// </summary>
/// <remarks>
/// <para><strong>ID Representation:</strong></para>
/// The <see cref="Id"/> property is mapped to the MongoDB <c>_id</c> field and stored as a BSON string
/// to avoid GUID representation ambiguity.
/// <para><strong>Date/Time Representation:</strong></para>
/// All <see cref="System.DateTimeOffset"/> fields from <see cref="OutboxMessage"/> are stored as UTC
/// <see cref="System.DateTime"/> values, since MongoDB's BSON date type natively represents UTC milliseconds.
/// Callers are responsible for converting to and from <see cref="System.DateTimeOffset"/> using
/// <see cref="System.TimeSpan.Zero"/> as the offset.
/// </remarks>
internal sealed class OutboxDocument
{
    /// <summary>Gets or sets the unique identifier for this outbox document, stored as <c>_id</c>.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>Gets or sets the assembly-qualified event type name.</summary>
    [BsonElement(OutboxMessageSchema.Columns.EventType)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON-serialized event payload.</summary>
    [BsonElement(OutboxMessageSchema.Columns.Payload)]
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional distributed tracing correlation identifier.</summary>
    [BsonElement(OutboxMessageSchema.Columns.CorrelationId)]
    public string? CorrelationId { get; set; }

    /// <summary>Gets or sets the optional causation identifier.</summary>
    [BsonElement(OutboxMessageSchema.Columns.CausationId)]
    public string? CausationId { get; set; }

    /// <summary>Gets or sets the UTC creation timestamp.</summary>
    [BsonElement(OutboxMessageSchema.Columns.CreatedAt)]
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC last-update timestamp.</summary>
    [BsonElement(OutboxMessageSchema.Columns.UpdatedAt)]
    public DateTime UpdatedAt { get; set; }

    /// <summary>Gets or sets the UTC timestamp when the message was successfully processed, or <see langword="null"/> if not yet processed.</summary>
    [BsonElement(OutboxMessageSchema.Columns.ProcessedAt)]
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Gets or sets the UTC timestamp for the next scheduled retry attempt, or <see langword="null"/> when exponential backoff is not in use.</summary>
    [BsonElement(OutboxMessageSchema.Columns.NextRetryAt)]
    public DateTime? NextRetryAt { get; set; }

    /// <summary>Gets or sets the number of processing attempts made so far.</summary>
    [BsonElement(OutboxMessageSchema.Columns.RetryCount)]
    public int RetryCount { get; set; }

    /// <summary>Gets or sets the last error message recorded during a failed processing attempt.</summary>
    [BsonElement(OutboxMessageSchema.Columns.Error)]
    public string? Error { get; set; }

    /// <summary>Gets or sets the current processing status as its integer enum value.</summary>
    [BsonElement(OutboxMessageSchema.Columns.Status)]
    public int Status { get; set; }
}
