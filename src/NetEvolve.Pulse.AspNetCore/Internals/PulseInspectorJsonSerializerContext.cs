namespace NetEvolve.Pulse.AspNetCore.Internals;

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
/// Using source-generated contracts keeps the inspector responses trim- and NativeAOT-safe and independent of the
/// application's <c>HttpJsonOptions</c>. The responses always use <see cref="JsonSerializerDefaults.Web"/> and write
/// <see cref="OutboxMessage.EventType"/> as its outbox event type identifier through <see cref="TypeJsonConverter"/>.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(TypeJsonConverter)])]
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
