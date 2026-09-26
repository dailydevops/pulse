namespace NetEvolve.Pulse.Outbox;

using System.Text.Json.Serialization;

/// <summary>
/// Wire format of an outbox message sent to Azure Queue Storage.
/// </summary>
/// <param name="Id">The outbox message identifier.</param>
/// <param name="EventType">The outbox event type identifier.</param>
/// <param name="Payload">The serialized event payload.</param>
/// <param name="CorrelationId">The optional correlation identifier.</param>
/// <param name="CausationId">The optional causation identifier.</param>
/// <param name="CreatedAt">The creation timestamp of the outbox message.</param>
internal sealed record AzureQueueStorageEnvelope(
    Guid Id,
    string EventType,
    string Payload,
    string? CorrelationId,
    string? CausationId,
    DateTimeOffset CreatedAt
);

/// <summary>
/// Source-generated JSON contract for <see cref="AzureQueueStorageEnvelope"/>, so the envelope is written without
/// reflection and independent of the application's payload serializer settings.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AzureQueueStorageEnvelope))]
internal sealed partial class AzureQueueStorageJsonSerializerContext : JsonSerializerContext;
