namespace NetEvolve.Pulse.SourceGeneration.Models;

using System;
using Microsoft.CodeAnalysis;

/// <summary>
/// Carries information about an invalid <c>[PulseHandler&lt;T&gt;]</c> usage for diagnostic reporting.
/// </summary>
internal readonly struct ExplicitTypeError : IEquatable<ExplicitTypeError>
{
    /// <summary>Gets the display name of the invalid message type.</summary>
    public string MessageTypeName { get; }

    /// <summary>Gets the fully qualified name of the handler type.</summary>
    public string HandlerTypeName { get; }

    /// <summary>Gets the source location of the type declaration for diagnostic reporting.</summary>
    public Location Location { get; }

    /// <summary>
    /// Gets a value indicating whether to emit PULSE005 (<see langword="true"/>) or
    /// PULSE006 (<see langword="false"/>).
    /// </summary>
    public bool IsPulse005 { get; }

    /// <summary>
    /// Initializes a new <see cref="ExplicitTypeError"/>.
    /// </summary>
    public ExplicitTypeError(string messageTypeName, string handlerTypeName, Location location, bool isPulse005)
    {
        MessageTypeName = messageTypeName;
        HandlerTypeName = handlerTypeName;
        Location = location;
        IsPulse005 = isPulse005;
    }

    /// <inheritdoc />
    public bool Equals(ExplicitTypeError other) =>
        string.Equals(MessageTypeName, other.MessageTypeName, StringComparison.Ordinal)
        && string.Equals(HandlerTypeName, other.HandlerTypeName, StringComparison.Ordinal)
        && IsPulse005 == other.IsPulse005;

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is ExplicitTypeError other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(MessageTypeName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(HandlerTypeName);
            hash = (hash * 31) + IsPulse005.GetHashCode();
            return hash;
        }
    }
}
