namespace NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Extension methods for <see cref="Type"/> in the context of the outbox pattern.
/// </summary>
public static class TypeExtensions
{
    /// <summary>
    /// Returns the <see cref="Type.AssemblyQualifiedName"/> of the type for use as the outbox event type identifier.
    /// Falls back to <see cref="Type.FullName"/> and then <see cref="System.Reflection.MemberInfo.Name"/>
    /// if the assembly-qualified name is unavailable (e.g. for dynamic or generic types).
    /// </summary>
    /// <param name="type">The type whose name to retrieve.</param>
    /// <returns>The outbox event type name string.</returns>
    public static string ToOutboxEventTypeName(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
    }
}
