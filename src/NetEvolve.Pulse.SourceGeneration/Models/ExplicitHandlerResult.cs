namespace NetEvolve.Pulse.SourceGeneration.Models;

using System;
using Microsoft.CodeAnalysis;

/// <summary>
/// Combined result of analyzing a <c>[PulseHandler&lt;T&gt;]</c>-annotated type: the handler
/// registration info (when at least one valid registration exists) and the diagnostic errors for
/// invalid or incompatible message type arguments. Produced by a single transform so that the
/// symbol analysis runs only once per annotated type.
/// </summary>
internal readonly struct ExplicitHandlerResult : IEquatable<ExplicitHandlerResult>
{
    /// <summary>Gets the handler info, or <see langword="null"/> when no valid registration exists.</summary>
    public HandlerInfo? Info { get; }

    /// <summary>Gets the diagnostic errors for invalid or incompatible message type arguments.</summary>
    public ExplicitTypeError[] Errors { get; }

    /// <summary>
    /// Gets the diagnostic that prevents any registration of the handler type (PULSE004 or PULSE007), or
    /// <see langword="null"/> when the handler type itself can be registered.
    /// </summary>
    public DiagnosticDescriptor? Blocker { get; }

    /// <summary>
    /// Initializes a new <see cref="ExplicitHandlerResult"/> with the given handler info and errors.
    /// </summary>
    /// <param name="info">The handler info, or <see langword="null"/> when none exists.</param>
    /// <param name="errors">The diagnostic errors for invalid message type arguments.</param>
    /// <param name="blocker">The diagnostic that prevents any registration of the handler type.</param>
    public ExplicitHandlerResult(HandlerInfo? info, ExplicitTypeError[] errors, DiagnosticDescriptor? blocker = null)
    {
        Info = info;
        Errors = errors;
        Blocker = blocker;
    }

    /// <inheritdoc />
    public bool Equals(ExplicitHandlerResult other)
    {
        if (Info.HasValue != other.Info.HasValue)
        {
            return false;
        }

        if (Info.HasValue && !Info.Value.Equals(other.Info!.Value))
        {
            return false;
        }

        if (!Equals(Blocker, other.Blocker) || Errors.Length != other.Errors.Length)
        {
            return false;
        }

        for (var i = 0; i < Errors.Length; i++)
        {
            if (!Errors[i].Equals(other.Errors[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is ExplicitHandlerResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Info.HasValue ? Info.Value.GetHashCode() : 0;
            foreach (var error in Errors)
            {
                hash = (hash * 31) + error.GetHashCode();
            }

            return hash;
        }
    }
}
