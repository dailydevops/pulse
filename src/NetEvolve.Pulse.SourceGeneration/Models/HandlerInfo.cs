namespace NetEvolve.Pulse.SourceGeneration.Models;

using System;
using Microsoft.CodeAnalysis;

/// <summary>
/// Lightweight model captured per annotated class for the pipeline.
/// </summary>
internal readonly struct HandlerInfo : IEquatable<HandlerInfo>
{
    /// <summary>Gets the fully qualified name of the handler type.</summary>
    public string HandlerTypeName { get; }

    /// <summary>Gets the handler interface registrations discovered for this type.</summary>
    public HandlerRegistration[] Registrations { get; }

    /// <summary>Gets the source location of the handler type declaration for diagnostic reporting.</summary>
    public Location Location { get; }

    /// <summary>
    /// Initializes a new <see cref="HandlerInfo"/> with the given type name, registrations,
    /// and source location.
    /// </summary>
    /// <param name="handlerTypeName">The fully qualified name of the handler type.</param>
    /// <param name="registrations">The handler interface registrations for this type.</param>
    /// <param name="location">The source location of the type declaration.</param>
    public HandlerInfo(string handlerTypeName, HandlerRegistration[] registrations, Location location)
    {
        HandlerTypeName = handlerTypeName;
        Registrations = registrations;
        Location = location;
    }

    /// <inheritdoc />
    public bool Equals(HandlerInfo other) =>
        string.Equals(HandlerTypeName, other.HandlerTypeName, StringComparison.Ordinal)
        && RegistrationsEqual(Registrations, other.Registrations);

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is HandlerInfo other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(HandlerTypeName);
            foreach (var r in Registrations)
            {
                hash = (hash * 31) + r.GetHashCode();
            }
            return hash;
        }
    }

    /// <summary>
    /// Compares two <see cref="HandlerRegistration"/> arrays for element-wise equality.
    /// </summary>
    /// <param name="left">The left array to compare.</param>
    /// <param name="right">The right array to compare.</param>
    /// <returns>
    /// <see langword="true"/> when both arrays have the same length and all elements are equal.
    /// </returns>
    private static bool RegistrationsEqual(HandlerRegistration[] left, HandlerRegistration[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }
}
