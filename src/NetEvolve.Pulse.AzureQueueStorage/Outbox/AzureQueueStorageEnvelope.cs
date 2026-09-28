namespace NetEvolve.Pulse.Outbox;

using System.Text.Json.Serialization;

/// <summary>
/// Wire format of an outbox message sent to Azure Queue Storage.
/// </summary>
/// <remarks>
/// The property names are fixed through <see cref="JsonPropertyNameAttribute"/>, so a custom naming policy in the
/// configured <see cref="System.Text.Json.JsonSerializerOptions"/> cannot change the wire format, and the optional
/// identifiers are always written, also as <see langword="null"/>.
/// </remarks>
/// <param name="Id">The outbox message identifier.</param>
/// <param name="EventType">The outbox event type identifier.</param>
/// <param name="Payload">The serialized event payload.</param>
/// <param name="CorrelationId">The optional correlation identifier.</param>
/// <param name="CausationId">The optional causation identifier.</param>
/// <param name="CreatedAt">The creation timestamp of the outbox message.</param>
internal sealed record AzureQueueStorageEnvelope(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("payload")] string Payload,
    [property: JsonPropertyName("correlationId"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        string? CorrelationId,
    [property: JsonPropertyName("causationId"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CausationId,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt
);
