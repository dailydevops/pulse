namespace NetEvolve.Pulse.SourceGeneration;

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

    public HandlerRegistration(string handlerTypeName, string serviceTypeName, HandlerKind kind)
    {
        HandlerTypeName = handlerTypeName;
        ServiceTypeName = serviceTypeName;
        Kind = kind;
    }

    public bool Equals(HandlerRegistration other) =>
        string.Equals(HandlerTypeName, other.HandlerTypeName, StringComparison.Ordinal)
        && string.Equals(ServiceTypeName, other.ServiceTypeName, StringComparison.Ordinal)
        && Kind == other.Kind;

    public override bool Equals(object obj) => obj is HandlerRegistration other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(HandlerTypeName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ServiceTypeName);
            hash = (hash * 31) + (int)Kind;
            return hash;
        }
    }
}

/// <summary>
/// Classifies which Pulse handler interface a registration targets.
/// </summary>
internal enum HandlerKind
{
    Command,
    Query,
    Event,
}
