namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// EF Core value converter that maps a <see cref="Type"/> to its assembly-qualified name string
/// and back. Used for persisting <see cref="OutboxMessage.EventType"/> in the database.
/// </summary>
/// <remarks>
/// The converter enforces the maximum column length defined by
/// <see cref="OutboxMessageSchema.MaxLengths.EventType"/> when converting from <see cref="Type"/> to
/// <see cref="string"/>.
/// </remarks>
internal sealed class TypeValueConverter : ValueConverter<Type, string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TypeValueConverter"/> class.
    /// </summary>
    public TypeValueConverter()
        : base(type => ConvertToString(type), typeName => ConvertFromString(typeName)) { }

    private static string ConvertToString(Type type)
    {
        var name =
            type.AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Cannot get assembly-qualified name for type: {type}");

        if (name.Length > OutboxMessageSchema.MaxLengths.EventType)
        {
            throw new InvalidOperationException(
                $"Event type identifier exceeds the EventType column maximum length of {OutboxMessageSchema.MaxLengths.EventType} characters. "
                    + "Shorten the type identifier, increase the database column length, or use Type.FullName with a type registry."
            );
        }

        return name;
    }

    private static Type ConvertFromString(string typeName) =>
        Type.GetType(typeName) ?? throw new InvalidOperationException($"Cannot resolve event type '{typeName}'.");
}
