namespace NetEvolve.Pulse.Outbox;

using System.Text.Json.Serialization;

/// <summary>
/// Source-generated JSON contract for <see cref="AzureQueueStorageEnvelope"/>, appended as fallback resolver to the
/// configured <see cref="System.Text.Json.JsonSerializerOptions"/>, so the envelope is written without reflection.
/// </summary>
[JsonSerializable(typeof(AzureQueueStorageEnvelope))]
internal sealed partial class AzureQueueStorageJsonSerializerContext : JsonSerializerContext;
