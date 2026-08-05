namespace NetEvolve.Pulse.AspNetCore.Internals;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Serializes a <see cref="Type"/> as its outbox event type identifier string (see
/// <see cref="TypeExtensions.ToOutboxEventTypeName"/>), so that response models such as
/// <see cref="NetEvolve.Pulse.Extensibility.Outbox.OutboxMessage"/> — whose
/// <see cref="NetEvolve.Pulse.Extensibility.Outbox.OutboxMessage.EventType"/> is a raw
/// <see cref="Type"/> — can be written as JSON.
/// </summary>
/// <remarks>
/// This converter is write-only: it is used exclusively to serialize inspector responses.
/// Reading is not supported because these endpoints never accept an <see cref="OutboxMessage"/> as input.
/// </remarks>
internal sealed class TypeJsonConverter : JsonConverter<Type>
{
    /// <inheritdoc />
    public override Type Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException($"Deserializing a '{nameof(Type)}' value is not supported.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStringValue(value.ToOutboxEventTypeName());
    }
}
