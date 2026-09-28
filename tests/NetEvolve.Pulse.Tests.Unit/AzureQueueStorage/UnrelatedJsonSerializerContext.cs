namespace NetEvolve.Pulse.Tests.Unit.AzureQueueStorage;

using System.Text.Json.Serialization;

// An application context that knows nothing about the transport envelope.
[JsonSerializable(typeof(int))]
internal sealed partial class UnrelatedJsonSerializerContext : JsonSerializerContext;
