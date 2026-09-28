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
        category: "Usage",
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
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    /// <summary>
    /// PULSE003 – the type implements a Pulse handler interface but is missing the [PulseHandler] attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingPulseHandlerAttribute = new(
        id: "PULSE003",
        title: "Type implements a Pulse handler interface but is missing [PulseHandler]",
        messageFormat: "Type '{0}' implements a Pulse handler interface but is not annotated with [PulseHandler]",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    /// <summary>
    /// PULSE004 – the annotated type is an open generic type that cannot be automatically registered.
    /// </summary>
    public static readonly DiagnosticDescriptor OpenGenericHandlerNotSupported = new(
        id: "PULSE004",
        title: "Open generic type cannot be automatically registered",
        messageFormat: "Type '{0}' is an open generic type and cannot be automatically registered by [PulseHandler]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    /// <summary>
    /// PULSE005 – the type argument <c>T</c> of <c>[PulseHandler&lt;T&gt;]</c> does not implement
    /// any known Pulse message interface.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidExplicitMessageType = new(
        id: "PULSE005",
        title: "Type does not implement a known Pulse message interface",
        messageFormat: "Type '{0}' passed to [PulseHandler<T>] on '{1}' does not implement a known Pulse message interface (ICommand, ICommand<T>, IQuery<T>, IEvent, or IStreamQuery<T>)",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    /// <summary>
    /// PULSE006 – a closed registration for the given message type cannot be constructed because
    /// the handler does not implement a compatible handler interface or its type parameters cannot
    /// all be inferred from the message type, or the inferred type arguments do not satisfy the
    /// handler's generic constraints.
    /// </summary>
    public static readonly DiagnosticDescriptor IncompatibleExplicitMessageType = new(
        id: "PULSE006",
        title: "No compatible handler registration can be constructed for the message type",
        messageFormat: "Cannot construct a registration for message type '{0}' on '{1}': the handler does not implement a compatible handler interface, not all type parameters can be inferred, or the inferred type arguments do not satisfy the handler's generic constraints",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    /// <summary>
    /// PULSE007 – the annotated type cannot be registered because the generated registration code cannot
    /// reference it (the type or a containing type is <see langword="private"/>, <see langword="protected"/>,
    /// <see langword="private protected"/> or <see langword="file"/>-local) or the DI container cannot instantiate it (the type is
    /// <see langword="abstract"/>, <see langword="static"/> or a value type).
    /// </summary>
    public static readonly DiagnosticDescriptor UnregistrableHandler = new(
        id: "PULSE007",
        title: "Handler type cannot be registered",
        messageFormat: "Type '{0}' cannot be registered as a Pulse handler: it must be a non-abstract, non-static class that is not file-local and neither it nor any containing type is private, protected or private protected",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
