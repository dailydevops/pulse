namespace NetEvolve.Pulse.Internals;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the Pulse-owned response models written by the
/// outbox, audit and command dead letter inspector endpoints.
/// </summary>
/// <remarks>
/// The context is not used on its own. <see cref="PulseInspectorJsonOptions"/> appends it to a copy of the
/// application's <c>HttpJsonOptions</c> as fallback resolver, so the contracts stay trim- and NativeAOT-safe while the
/// application's naming policy and converters apply. <see cref="TypeJsonConverter"/> is added there,
/// because the converters of <see cref="JsonSourceGenerationOptionsAttribute"/> only apply to the context's own options.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(OutboxStatistics))]
[JsonSerializable(typeof(OutboxMessage))]
[JsonSerializable(typeof(IReadOnlyList<OutboxMessage>))]
[JsonSerializable(typeof(OutboxInspectorEndpoints.OutboxReplayAllResult))]
[JsonSerializable(typeof(AuditStatistics))]
[JsonSerializable(typeof(AuditRecord))]
[JsonSerializable(typeof(IReadOnlyList<AuditRecord>))]
[JsonSerializable(typeof(CommandDeadLetterStatistics))]
[JsonSerializable(typeof(CommandDeadLetterEntry))]
[JsonSerializable(typeof(IReadOnlyList<CommandDeadLetterEntry>))]
[JsonSerializable(typeof(long))]
internal sealed partial class PulseInspectorJsonSerializerContext : JsonSerializerContext;
