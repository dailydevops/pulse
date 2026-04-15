namespace NetEvolve.Pulse.SourceGeneration;

/// <summary>
/// Fully qualified metadata names of types referenced by the Pulse source generator.
/// </summary>
internal static class WellKnownTypeNames
{
    // ── Attributes ──────────────────────────────────────────────────────────────

    /// <summary>Fully qualified metadata name of the <c>[PulseHandler]</c> attribute.</summary>
    internal const string PulseHandlerAttributeFullName =
        "NetEvolve.Pulse.Extensibility.Attributes.PulseHandlerAttribute";

    /// <summary>Fully qualified metadata name of the generic <c>[PulseHandler&lt;T&gt;]</c> attribute.</summary>
    internal const string PulseHandlerGenericAttributeFullName =
        "NetEvolve.Pulse.Extensibility.Attributes.PulseHandlerAttribute`1";

    // ── Handler interfaces ───────────────────────────────────────────────────────

    /// <summary>Metadata name of the two-type-argument <c>ICommandHandler</c> interface.</summary>
    internal const string CommandHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.ICommandHandler`2";

    /// <summary>Metadata name of the single-type-argument <c>ICommandHandler</c> interface.</summary>
    internal const string CommandHandlerSingleInterfaceName = "NetEvolve.Pulse.Extensibility.ICommandHandler`1";

    /// <summary>Metadata name of the <c>IQueryHandler</c> interface.</summary>
    internal const string QueryHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IQueryHandler`2";

    /// <summary>Metadata name of the <c>IEventHandler</c> interface.</summary>
    internal const string EventHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IEventHandler`1";

    /// <summary>Metadata name of the <c>IStreamQueryHandler</c> interface.</summary>
    internal const string StreamQueryHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IStreamQueryHandler`2";

    // ── Message interfaces ───────────────────────────────────────────────────────

    /// <summary>Metadata name of the <c>ICommand&lt;TResponse&gt;</c> message interface.</summary>
    internal const string CommandMessageInterfaceName = "NetEvolve.Pulse.Extensibility.ICommand`1";

    /// <summary>Metadata name of the <c>IQuery&lt;TResponse&gt;</c> message interface.</summary>
    internal const string QueryMessageInterfaceName = "NetEvolve.Pulse.Extensibility.IQuery`1";

    /// <summary>Metadata name of the <c>IEvent</c> message interface.</summary>
    internal const string EventMessageInterfaceName = "NetEvolve.Pulse.Extensibility.IEvent";

    /// <summary>Metadata name of the <c>IStreamQuery&lt;TResponse&gt;</c> message interface.</summary>
    internal const string StreamQueryMessageInterfaceName = "NetEvolve.Pulse.Extensibility.IStreamQuery`1";
}
