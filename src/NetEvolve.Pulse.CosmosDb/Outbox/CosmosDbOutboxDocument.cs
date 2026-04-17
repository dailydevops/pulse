namespace NetEvolve.Pulse.CosmosDb;

using System.Text.Json.Serialization;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Represents an <see cref="OutboxMessage"/> persisted as a Cosmos DB document.
/// </summary>
/// <remarks>
/// Maps all <see cref="OutboxMessage"/> fields to their Cosmos DB document equivalents.
/// The Cosmos DB document <c>id</c> property is mapped from <see cref="OutboxMessage.Id"/>.
/// The optional <c>ttl</c> property enables automatic cleanup via the Cosmos DB TTL engine
/// when <see cref="CosmosDbOutboxOptions.EnableTimeToLive"/> is <see langword="true"/>.
/// </remarks>
internal sealed class CosmosDbOutboxDocument
{
    /// <summary>
    /// Gets or sets the Cosmos DB document identifier, mapped from <see cref="OutboxMessage.Id"/>.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the assembly-qualified event type name.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON serialized event payload.
    /// </summary>
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the correlation identifier for distributed tracing.
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the causation identifier.
    /// </summary>
    [JsonPropertyName("causationId")]
    public string? CausationId { get; set; }

    /// <summary>
    /// Gets or sets the message creation timestamp in ISO 8601 format.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp in ISO 8601 format.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this message was successfully processed.
    /// </summary>
    [JsonPropertyName("processedAt")]
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// Gets or sets the scheduled timestamp for the next retry attempt.
    /// </summary>
    [JsonPropertyName("nextRetryAt")]
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>
    /// Gets or sets the number of processing attempts.
    /// </summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets or sets the last error message from a failed processing attempt.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the current processing status as an integer.
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    /// Gets or sets the TTL override in seconds for this document.
    /// A value of <c>-1</c> disables TTL; <see langword="null"/> inherits the container default.
    /// </summary>
    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Ttl { get; set; }

    /// <summary>
    /// Converts this Cosmos DB document to an <see cref="OutboxMessage"/>.
    /// </summary>
    /// <returns>The corresponding <see cref="OutboxMessage"/>.</returns>
    public OutboxMessage ToOutboxMessage()
    {
        return new OutboxMessage
        {
            Id = Guid.Parse(Id),
            EventType = Type.GetType(EventType, throwOnError: false) ?? typeof(object),
            Payload = Payload,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ProcessedAt = ProcessedAt,
            NextRetryAt = NextRetryAt,
            RetryCount = RetryCount,
            Error = Error,
            Status = (OutboxMessageStatus)Status,
        };
    }

    /// <summary>
    /// Creates a <see cref="CosmosDbOutboxDocument"/> from an <see cref="OutboxMessage"/>.
    /// </summary>
    /// <param name="message">The source outbox message.</param>
    /// <returns>A new <see cref="CosmosDbOutboxDocument"/> populated from the message.</returns>
    public static CosmosDbOutboxDocument FromOutboxMessage(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new CosmosDbOutboxDocument
        {
            Id = message.Id.ToString(),
            EventType = message.EventType.ToOutboxEventTypeName(),
            Payload = message.Payload,
            CorrelationId = message.CorrelationId,
            CausationId = message.CausationId,
            CreatedAt = message.CreatedAt,
            UpdatedAt = message.UpdatedAt,
            ProcessedAt = message.ProcessedAt,
            NextRetryAt = message.NextRetryAt,
            RetryCount = message.RetryCount,
            Error = message.Error,
            Status = (int)message.Status,
        };
    }
}
