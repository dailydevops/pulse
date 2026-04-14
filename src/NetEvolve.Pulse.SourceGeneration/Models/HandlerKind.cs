namespace NetEvolve.Pulse.SourceGeneration.Models;

/// <summary>
/// Classifies which Pulse handler interface a registration targets.
/// </summary>
internal enum HandlerKind
{
    /// <summary>The handler processes command messages.</summary>
    Command,

    /// <summary>The handler processes query messages.</summary>
    Query,

    /// <summary>The handler processes event messages.</summary>
    Event,

    /// <summary>The handler processes stream query messages.</summary>
    StreamQuery,
}
