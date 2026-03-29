namespace NetEvolve.Pulse.SourceGeneration;

using Microsoft.CodeAnalysis;

/// <summary>
/// Diagnostic descriptors emitted by the Pulse source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    /// <summary>
    /// PULSE001 – the annotated class does not implement any known handler interface.
    /// </summary>
    public static readonly DiagnosticDescriptor NoHandlerInterface = new(
        id: "PULSE001",
        title: "Type does not implement a Pulse handler interface",
        messageFormat: "Type '{0}' is annotated with [PulseHandler] but does not implement any known Pulse handler interface",
        category: "PulseSourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    /// <summary>
    /// PULSE002 – multiple handlers registered for a single-handler contract (command or query).
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateHandlerRegistration = new(
        id: "PULSE002",
        title: "Duplicate handler registration",
        messageFormat: "Multiple [PulseHandler] types implement '{0}': {1}. Commands and queries must have exactly one handler.",
        category: "PulseSourceGeneration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
}
