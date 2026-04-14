namespace NetEvolve.Pulse.SourceGeneration.Models;

using System;

/// <summary>
/// Represents a single handler registration discovered by the source generator.
/// </summary>
internal readonly struct HandlerRegistration : IEquatable<HandlerRegistration>
{
    /// <summary>The fully qualified name of the concrete handler class.</summary>
    public string HandlerTypeName { get; }

    /// <summary>The fully qualified name of the service interface (e.g. <c>ICommandHandler&lt;…&gt;</c>).</summary>
    public string ServiceTypeName { get; }

    /// <summary>The kind of handler (command, query, or event).</summary>
    public HandlerKind Kind { get; }

    /// <summary>The service lifetime for the registration (0 = Singleton, 1 = Scoped, 2 = Transient).</summary>
    public int Lifetime { get; }

    /// <summary>
    /// Initializes a new <see cref="HandlerRegistration"/> with the specified type names, kind, and lifetime.
    /// </summary>
    /// <param name="handlerTypeName">The fully qualified name of the concrete handler class.</param>
    /// <param name="serviceTypeName">The fully qualified name of the service interface.</param>
    /// <param name="kind">The kind of handler contract.</param>
    /// <param name="lifetime">The service lifetime for the registration.</param>
    public HandlerRegistration(string handlerTypeName, string serviceTypeName, HandlerKind kind, int lifetime)
    {
        HandlerTypeName = handlerTypeName;
        ServiceTypeName = serviceTypeName;
        Kind = kind;
        Lifetime = lifetime;
    }

    /// <inheritdoc />
    public bool Equals(HandlerRegistration other) =>
        string.Equals(HandlerTypeName, other.HandlerTypeName, StringComparison.Ordinal)
        && string.Equals(ServiceTypeName, other.ServiceTypeName, StringComparison.Ordinal)
        && Kind == other.Kind
        && Lifetime == other.Lifetime;

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is HandlerRegistration other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(HandlerTypeName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ServiceTypeName);
            hash = (hash * 31) + (int)Kind;
            hash = (hash * 31) + Lifetime;
            return hash;
        }
    }
}
