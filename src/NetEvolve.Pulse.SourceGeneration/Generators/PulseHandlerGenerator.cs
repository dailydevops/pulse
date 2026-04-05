namespace NetEvolve.Pulse.SourceGeneration.Generators;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetEvolve.CodeBuilder;
using NetEvolve.Pulse.SourceGeneration.Models;

/// <summary>
/// Roslyn incremental source generator that emits DI registrations for classes annotated with
/// <c>[PulseHandler]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PulseHandlerGenerator : IIncrementalGenerator
{
    /// <summary>Fully qualified metadata name of the <c>[PulseHandler]</c> attribute.</summary>
    private const string PulseHandlerAttributeFullName = "NetEvolve.Pulse.Attributes.PulseHandlerAttribute";

    /// <summary>Metadata name of the two-type-argument <c>ICommandHandler</c> interface.</summary>
    private const string CommandHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.ICommandHandler`2";

    /// <summary>Metadata name of the single-type-argument <c>ICommandHandler</c> interface.</summary>
    private const string CommandHandlerSingleInterfaceName = "NetEvolve.Pulse.Extensibility.ICommandHandler`1";

    /// <summary>Metadata name of the <c>IQueryHandler</c> interface.</summary>
    private const string QueryHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IQueryHandler`2";

    /// <summary>Metadata name of the <c>IEventHandler</c> interface.</summary>
    private const string EventHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IEventHandler`1";

    /// <summary>Metadata name of the <c>IStreamQueryHandler</c> interface.</summary>
    private const string StreamQueryHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IStreamQueryHandler`2";

    /// <summary>
    /// Default service lifetime value matching <c>PulseServiceLifetime.Scoped</c>.
    /// </summary>
    private const int DefaultLifetime = 1; // PulseServiceLifetime.Scoped

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        RegisterHandlerRegistrationPipeline(context);
        RegisterOpenGenericDiagnosticPipeline(context);
        RegisterUnannotatedHandlerDiagnosticPipeline(context);
    }

    /// <summary>
    /// Registers the main handler registration pipeline that collects <c>[PulseHandler]</c>-annotated
    /// types and emits the DI registration extension method via <see cref="Execute"/>.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    private static void RegisterHandlerRegistrationPipeline(IncrementalGeneratorInitializationContext context)
    {
        var handlerInfos = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                PulseHandlerAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ExtractHandlerInfo(ctx, ct)
            )
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);

        var collected = handlerInfos.Collect();

        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _1) =>
            {
                _ = provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns);
                return ns;
            }
        );

        var assemblyName = context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName);

        var combined = collected.Combine(rootNamespace).Combine(assemblyName);

        context.RegisterSourceOutput(
            combined,
            static (spc, data) => Execute(spc, data.Left.Left, data.Left.Right, data.Right)
        );
    }

    /// <summary>
    /// Registers the incremental pipeline that reports PULSE004 for <c>[PulseHandler]</c>-annotated
    /// open generic types that cannot be automatically registered.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    private static void RegisterOpenGenericDiagnosticPipeline(IncrementalGeneratorInitializationContext context)
    {
        var openGenericHandlers = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                PulseHandlerAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ExtractOpenGenericHandlerInfo(ctx, ct)
            )
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);

        context.RegisterSourceOutput(
            openGenericHandlers,
            static (spc, info) =>
                spc.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.OpenGenericHandlerNotSupported,
                        info.Location,
                        info.HandlerTypeName
                    )
                )
        );
    }

    /// <summary>
    /// Registers the incremental pipeline that reports PULSE003 for types that implement a known
    /// Pulse handler interface but are not annotated with <c>[PulseHandler]</c>.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    private static void RegisterUnannotatedHandlerDiagnosticPipeline(IncrementalGeneratorInitializationContext context)
    {
        var unannotatedHandlers = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node
                        is ClassDeclarationSyntax { BaseList: not null }
                            or RecordDeclarationSyntax { BaseList: not null },
                transform: static (ctx, ct) => ExtractUnannotatedHandlerInfo(ctx, ct)
            )
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);

        context.RegisterSourceOutput(
            unannotatedHandlers,
            static (spc, info) =>
                spc.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.MissingPulseHandlerAttribute,
                        info.Location,
                        info.HandlerTypeName
                    )
                )
        );
    }

    /// <summary>
    /// Extracts handler registration info from a single annotated class.
    /// Returns <c>null</c> when the symbol cannot be resolved.
    /// </summary>
    private static HandlerInfo? ExtractHandlerInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        // Skip open generic types - they cannot be registered in the DI container.
        if (classSymbol.TypeParameters.Length > 0)
        {
            return null;
        }

        var lifetime = ReadLifetime(ctx.Attributes);
        var location = ctx.TargetNode.GetLocation();
        var registrations = BuildHandlerRegistrations(classSymbol, lifetime);

        return new HandlerInfo(GetFullyQualifiedName(classSymbol), [.. registrations], location);
    }

    /// <summary>
    /// Detects open generic types annotated with <c>[PulseHandler]</c> for PULSE004 reporting.
    /// Returns <c>null</c> when the type is not an open generic.
    /// </summary>
    private static HandlerInfo? ExtractOpenGenericHandlerInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        if (classSymbol.TypeParameters.Length == 0)
        {
            return null;
        }

        return new HandlerInfo(GetFullyQualifiedName(classSymbol), [], ctx.TargetNode.GetLocation());
    }

    /// <summary>
    /// Checks whether a class implements a known Pulse handler interface without carrying the
    /// <c>[PulseHandler]</c> attribute. Returns a <see cref="HandlerInfo"/> for diagnostic
    /// reporting, or <c>null</c> when no issue is detected.
    /// </summary>
    private static HandlerInfo? ExtractUnannotatedHandlerInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.Node is not (ClassDeclarationSyntax or RecordDeclarationSyntax))
        {
            return null;
        }

        var typeDeclaration = (TypeDeclarationSyntax)ctx.Node;

        if (ctx.SemanticModel.GetDeclaredSymbol(typeDeclaration, ct) is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        // Skip open generic types - they cannot be registered even with [PulseHandler], so PULSE003 is not applicable.
        if (classSymbol.TypeParameters.Length > 0)
        {
            return null;
        }

        // Skip classes already annotated with [PulseHandler].
        if (
            classSymbol
                .GetAttributes()
                .Any(attr =>
                    attr.AttributeClass is not null
                    && string.Equals(
                        GetFullMetadataName(attr.AttributeClass),
                        PulseHandlerAttributeFullName,
                        StringComparison.Ordinal
                    )
                )
        )
        {
            return null;
        }

        // Check whether the class implements any known handler interface.
        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (TryGetHandlerKind(GetFullMetadataName(iface.OriginalDefinition), out _))
            {
                return new HandlerInfo(GetFullyQualifiedName(classSymbol), [], typeDeclaration.GetLocation());
            }
        }

        return null;
    }

    /// <summary>
    /// Generates the source output and reports diagnostics.
    /// </summary>
    private static void Execute(
        SourceProductionContext spc,
        ImmutableArray<HandlerInfo> handlers,
        string? rootNamespace,
        string? assemblyName
    )
    {
        ReportNoHandlerInterfaceDiagnostics(spc, handlers);

        var singleHandlerContracts = BuildSingleHandlerContracts(handlers);
        var duplicatedServiceTypes = ReportDuplicateHandlerDiagnostics(spc, singleHandlerContracts);
        var allRegistrations = BuildRegistrations(handlers, duplicatedServiceTypes);

        if (allRegistrations.Count == 0)
        {
            return;
        }

        var source = GenerateSource(allRegistrations, rootNamespace, assemblyName);
        spc.AddSource("PulseRegistrations.Handlers.g.cs", source);
    }

    /// <summary>
    /// Reports PULSE001 for every handler in <paramref name="handlers"/> that has no recognized
    /// Pulse handler interface registrations.
    /// </summary>
    /// <param name="spc">The production context used to emit diagnostics.</param>
    /// <param name="handlers">The collected handler infos to inspect.</param>
    private static void ReportNoHandlerInterfaceDiagnostics(
        SourceProductionContext spc,
        ImmutableArray<HandlerInfo> handlers
    )
    {
        foreach (var handler in handlers)
        {
            if (handler.Registrations.Length == 0)
            {
                spc.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.NoHandlerInterface,
                        handler.Location,
                        handler.HandlerTypeName
                    )
                );
            }
        }
    }

    /// <summary>
    /// Builds a map from service type name to the list of handler type names for all non-event
    /// single-handler contracts (commands, queries, and stream queries).
    /// </summary>
    /// <param name="handlers">The collected handler infos to aggregate.</param>
    /// <returns>
    /// A dictionary keyed by service type name whose values are the handler type names registered
    /// for that contract.
    /// </returns>
    private static Dictionary<string, List<string>> BuildSingleHandlerContracts(ImmutableArray<HandlerInfo> handlers)
    {
        var contracts = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var handler in handlers)
        {
            foreach (var reg in handler.Registrations)
            {
                if (reg.Kind == HandlerKind.Event)
                {
                    continue;
                }

                if (!contracts.TryGetValue(reg.ServiceTypeName, out var handlerNames))
                {
                    handlerNames = [];
                    contracts[reg.ServiceTypeName] = handlerNames;
                }

                handlerNames.Add(reg.HandlerTypeName);
            }
        }

        return contracts;
    }

    /// <summary>
    /// Reports PULSE002 for every service type that has more than one registered handler, and
    /// returns the set of duplicated service type names for downstream deduplication.
    /// </summary>
    /// <param name="spc">The production context used to emit diagnostics.</param>
    /// <param name="singleHandlerContracts">
    /// The map of service type names to handler type names produced by
    /// <see cref="BuildSingleHandlerContracts"/>.
    /// </param>
    /// <returns>The set of service type names that have duplicate registrations.</returns>
    private static HashSet<string> ReportDuplicateHandlerDiagnostics(
        SourceProductionContext spc,
        Dictionary<string, List<string>> singleHandlerContracts
    )
    {
        var duplicatedServiceTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kvp in singleHandlerContracts)
        {
            if (kvp.Value.Count > 1)
            {
                spc.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateHandlerRegistration,
                        Location.None,
                        kvp.Key,
                        string.Join(", ", kvp.Value)
                    )
                );
                _ = duplicatedServiceTypes.Add(kvp.Key);
            }
        }

        return duplicatedServiceTypes;
    }

    /// <summary>
    /// Assembles the final deduplicated list of <see cref="HandlerRegistration"/> entries to emit.
    /// For service types with duplicate registrations only the first encountered handler is included.
    /// </summary>
    /// <param name="handlers">The collected handler infos to flatten.</param>
    /// <param name="duplicatedServiceTypes">Service type names that have more than one registered handler.</param>
    /// <returns>The deduplicated list of registrations ready for code generation.</returns>
    private static List<HandlerRegistration> BuildRegistrations(
        ImmutableArray<HandlerInfo> handlers,
        HashSet<string> duplicatedServiceTypes
    )
    {
        var emittedServiceTypes = new HashSet<string>(StringComparer.Ordinal);
        var allRegistrations = new List<HandlerRegistration>();

        foreach (var handler in handlers)
        {
            foreach (var reg in handler.Registrations)
            {
                if (duplicatedServiceTypes.Contains(reg.ServiceTypeName))
                {
                    if (emittedServiceTypes.Add(reg.ServiceTypeName))
                    {
                        allRegistrations.Add(reg);
                    }
                }
                else
                {
                    allRegistrations.Add(reg);
                }
            }
        }

        return allRegistrations;
    }

    /// <summary>
    /// Emits the C# source for the <c>PulseRegistrationExtensions</c> extension method that
    /// registers all discovered handlers into the DI container.
    /// </summary>
    /// <param name="registrations">The deduplicated list of registrations to emit.</param>
    /// <param name="rootNamespace">
    /// The target project's root namespace, or <see langword="null"/> to use the default.
    /// </param>
    /// <param name="assemblyName">The target assembly name used to derive the generated method name.</param>
    /// <returns>The generated C# source text.</returns>
    private static string GenerateSource(
        List<HandlerRegistration> registrations,
        string? rootNamespace,
        string? assemblyName
    )
    {
        var targetNamespace = string.IsNullOrWhiteSpace(rootNamespace) ? "NetEvolve.Pulse.Generated" : rootNamespace;
        var methodName = GetMethodName(assemblyName);
        var generatorVersion = typeof(PulseHandlerGenerator).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        var assemblyLabel = string.IsNullOrEmpty(assemblyName) ? "GeneratedAssembly" : assemblyName;

        var cb = new CSharpCodeBuilder(512)
            .AppendLine("// <auto-generated />")
            .AppendLine("#nullable enable")
            .AppendLine()
            .AppendLine("using global::System.CodeDom.Compiler;")
            .AppendLine("using global::Microsoft.Extensions.DependencyInjection;")
            .AppendLine("using global::Microsoft.Extensions.DependencyInjection.Extensions;")
            .AppendLine()
            .AppendLine($"namespace {targetNamespace};")
            .AppendLine()
            .AppendXmlDocSummary([
                "Auto-generated extension method to register Pulse handlers annotated",
                $"with <c>[PulseHandler]</c> inside the Assembly <c>{assemblyLabel}</c>.",
            ])
            .AppendLine($"[GeneratedCode(\"NetEvolve.Pulse.SourceGeneration\", \"{generatorVersion}\")]");

        using (cb.ScopeLine("public static partial class PulseRegistrationExtensions"))
        {
            _ = cb.AppendXmlDocSummary(
                    $"Registers all <c>[PulseHandler]</c>-annotated handlers from the assembly <c>{assemblyLabel}</c> into the DI container."
                )
                .AppendXmlDocParam("services", "The service collection to add registrations to.")
                .AppendXmlDocReturns("The same <see cref=\"IServiceCollection\"/> instance for chaining.");

            using (cb.ScopeLine($"public static IServiceCollection {methodName}(this IServiceCollection services)"))
            {
                foreach (var reg in registrations)
                {
                    var lifetimeMethodName = GetLifetimeMethodName(reg.Lifetime);
                    _ = cb.AppendLine(
                        $"services.{lifetimeMethodName}<{reg.ServiceTypeName}, {reg.HandlerTypeName}>();"
                    );
                }

                _ = cb.AppendLine().AppendLine("return services;");
            }
        }

        return cb.ToString();
    }

    /// <summary>
    /// Maps a <c>PulseServiceLifetime</c> integer value to the corresponding DI registration method name.
    /// </summary>
    private static string GetLifetimeMethodName(int lifetime) =>
        lifetime switch
        {
            0 => "TryAddSingleton",
            2 => "TryAddTransient",
            _ => "TryAddScoped",
        };

    /// <summary>
    /// Derives the generated extension method name from the assembly name by removing dots.
    /// For example, <c>NetEvolve.Pulse</c> becomes <c>AddNetEvolvePulseHandlers</c>.
    /// </summary>
    private static string GetMethodName(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            return "AddGeneratedPulseHandlers";
        }

        return "Add" + assemblyName!.Replace(".", string.Empty) + "Handlers";
    }

    /// <summary>
    /// Reads the <c>Lifetime</c> named argument from the <c>[PulseHandler]</c> attribute data.
    /// Returns <see cref="DefaultLifetime"/> when no explicit value is set.
    /// </summary>
    private static int ReadLifetime(ImmutableArray<AttributeData> attributes)
    {
        var lifetime = DefaultLifetime;

        foreach (var attr in attributes)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (string.Equals(namedArg.Key, "Lifetime", StringComparison.Ordinal) && !namedArg.Value.IsNull)
                {
                    // When IsNull is false, Value is guaranteed to be non-null by the Roslyn API contract.
                    lifetime = (int)namedArg.Value.Value!;
                }
            }
        }

        return lifetime;
    }

    /// <summary>
    /// Resolves the <see cref="HandlerKind"/> for a given interface metadata name.
    /// Returns <see langword="true"/> when the metadata name matches a known Pulse handler interface.
    /// </summary>
    private static bool TryGetHandlerKind(string metadataName, out HandlerKind kind)
    {
        if (
            string.Equals(metadataName, CommandHandlerInterfaceName, StringComparison.Ordinal)
            || string.Equals(metadataName, CommandHandlerSingleInterfaceName, StringComparison.Ordinal)
        )
        {
            kind = HandlerKind.Command;
            return true;
        }

        if (string.Equals(metadataName, QueryHandlerInterfaceName, StringComparison.Ordinal))
        {
            kind = HandlerKind.Query;
            return true;
        }

        if (string.Equals(metadataName, EventHandlerInterfaceName, StringComparison.Ordinal))
        {
            kind = HandlerKind.Event;
            return true;
        }

        if (string.Equals(metadataName, StreamQueryHandlerInterfaceName, StringComparison.Ordinal))
        {
            kind = HandlerKind.StreamQuery;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>
    /// Builds the list of <see cref="HandlerRegistration"/> entries for all recognized Pulse handler
    /// interfaces implemented by <paramref name="classSymbol"/>.
    /// </summary>
    private static List<HandlerRegistration> BuildHandlerRegistrations(INamedTypeSymbol classSymbol, int lifetime)
    {
        var registrations = new List<HandlerRegistration>();
        var handlerTypeName = GetFullyQualifiedName(classSymbol);

        foreach (var iface in classSymbol.AllInterfaces)
        {
            var metadataName = GetFullMetadataName(iface.OriginalDefinition);

            if (TryGetHandlerKind(metadataName, out var kind))
            {
                registrations.Add(
                    new HandlerRegistration(
                        handlerTypeName: handlerTypeName,
                        serviceTypeName: GetFullyQualifiedName(iface),
                        kind: kind,
                        lifetime: lifetime
                    )
                );
            }
        }

        return registrations;
    }

    /// <summary>
    /// Returns the fully qualified name of a type symbol using the <c>global::</c> prefix,
    /// suitable for emitting in generated source.
    /// </summary>
    private static string GetFullyQualifiedName(ITypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// Returns the metadata name including containing namespaces (e.g. <c>Ns.IFoo`2</c>).
    /// </summary>
    private static string GetFullMetadataName(INamedTypeSymbol symbol)
    {
        var parts = new List<string>();
        var current = symbol;
        while (current is not null)
        {
            parts.Add(current.MetadataName);
            current = current.ContainingType;
        }

        parts.Reverse();
        var typePart = string.Join("+", parts);

        var ns = symbol.ContainingNamespace;
        if (ns?.IsGlobalNamespace == false)
        {
            return ns.ToDisplayString() + "." + typePart;
        }

        return typePart;
    }

    /// <summary>
    /// Lightweight model captured per annotated class for the pipeline.
    /// </summary>
    private readonly struct HandlerInfo : IEquatable<HandlerInfo>
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
}
