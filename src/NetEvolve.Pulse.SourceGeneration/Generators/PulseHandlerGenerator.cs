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
    private const string PulseHandlerAttributeFullName = "NetEvolve.Pulse.Attributes.PulseHandlerAttribute";

    private const string CommandHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.ICommandHandler`2";

    private const string CommandHandlerSingleInterfaceName = "NetEvolve.Pulse.Extensibility.ICommandHandler`1";

    private const string QueryHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IQueryHandler`2";

    private const string EventHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IEventHandler`1";

    private const string StreamQueryHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IStreamQueryHandler`2";

    /// <summary>
    /// Default service lifetime value matching <c>PulseServiceLifetime.Scoped</c>.
    /// </summary>
    private const int DefaultLifetime = 1; // PulseServiceLifetime.Scoped

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect annotated classes/records via ForAttributeWithMetadataName for efficient filtering.
        var handlerInfos = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                PulseHandlerAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ExtractHandlerInfo(ctx, ct)
            )
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);

        var collected = handlerInfos.Collect();

        // Resolve the target project's root namespace for the generated code.
        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _1) =>
            {
                _ = provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns);
                return ns;
            }
        );

        // Read the assembly name from the compilation for the generated method name.
        var assemblyName = context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName);

        var combined = collected.Combine(rootNamespace).Combine(assemblyName);

        context.RegisterSourceOutput(
            combined,
            static (spc, data) => Execute(spc, data.Left.Left, data.Left.Right, data.Right)
        );

        // Report PULSE003 for classes/records that implement a handler interface but lack [PulseHandler].
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

        // Read the Lifetime property from the attribute.
        var lifetime = DefaultLifetime;
        foreach (var attr in ctx.Attributes)
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

        var registrations = new List<HandlerRegistration>();
        var location = ctx.TargetNode.GetLocation();

        foreach (var iface in classSymbol.AllInterfaces)
        {
            var originalDef = iface.OriginalDefinition;
            var metadataName = GetFullMetadataName(originalDef);

            HandlerKind? kind = null;

            if (
                string.Equals(metadataName, CommandHandlerInterfaceName, StringComparison.Ordinal)
                || string.Equals(metadataName, CommandHandlerSingleInterfaceName, StringComparison.Ordinal)
            )
            {
                kind = HandlerKind.Command;
            }
            else if (string.Equals(metadataName, QueryHandlerInterfaceName, StringComparison.Ordinal))
            {
                kind = HandlerKind.Query;
            }
            else if (string.Equals(metadataName, EventHandlerInterfaceName, StringComparison.Ordinal))
            {
                kind = HandlerKind.Event;
            }
            else if (string.Equals(metadataName, StreamQueryHandlerInterfaceName, StringComparison.Ordinal))
            {
                kind = HandlerKind.StreamQuery;
            }

            if (kind.HasValue)
            {
                registrations.Add(
                    new HandlerRegistration(
                        handlerTypeName: GetFullyQualifiedName(classSymbol),
                        serviceTypeName: GetFullyQualifiedName(iface),
                        kind: kind.Value,
                        lifetime: lifetime
                    )
                );
            }
        }

        return new HandlerInfo(GetFullyQualifiedName(classSymbol), [.. registrations], location);
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
            var metadataName = GetFullMetadataName(iface.OriginalDefinition);

            if (
                string.Equals(metadataName, CommandHandlerInterfaceName, StringComparison.Ordinal)
                || string.Equals(metadataName, CommandHandlerSingleInterfaceName, StringComparison.Ordinal)
                || string.Equals(metadataName, QueryHandlerInterfaceName, StringComparison.Ordinal)
                || string.Equals(metadataName, EventHandlerInterfaceName, StringComparison.Ordinal)
                || string.Equals(metadataName, StreamQueryHandlerInterfaceName, StringComparison.Ordinal)
            )
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
        // Report PULSE001 for handlers with no recognized interfaces.
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

        // Detect duplicate single-handler contracts (command & query).
        var singleHandlerContracts = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var handler in handlers)
        {
            foreach (var reg in handler.Registrations)
            {
                if (reg.Kind == HandlerKind.Event)
                {
                    continue;
                }

                if (!singleHandlerContracts.TryGetValue(reg.ServiceTypeName, out var handlerNames))
                {
                    handlerNames = [];
                    singleHandlerContracts[reg.ServiceTypeName] = handlerNames;
                }

                handlerNames.Add(reg.HandlerTypeName);
            }
        }

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
            }
        }

        // Collect all registrations across all handlers.
        var allRegistrations = new List<HandlerRegistration>();
        foreach (var handler in handlers)
        {
            allRegistrations.AddRange(handler.Registrations);
        }

        if (allRegistrations.Count == 0)
        {
            return;
        }

        var source = GenerateSource(allRegistrations, rootNamespace, assemblyName);
        spc.AddSource("PulseHandlerRegistrations.g.cs", source);
    }

    private static string GenerateSource(
        List<HandlerRegistration> registrations,
        string? rootNamespace,
        string? assemblyName
    )
    {
        var targetNamespace = string.IsNullOrWhiteSpace(rootNamespace) ? "NetEvolve.Pulse.Generated" : rootNamespace;
        var methodName = GetMethodName(assemblyName);
        var generatorVersion = typeof(PulseHandlerGenerator).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

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
                $"with <c>[PulseHandler]</c> inside of the Assembly <c>{assemblyName}</c>.",
            ])
            .AppendLine($"[GeneratedCode(\"NetEvolve.Pulse.SourceGeneration\", \"{generatorVersion}\")]");

        using (cb.ScopeLine("public static partial class PulseRegistrationExtensions"))
        {
            _ = cb.AppendXmlDocSummary(
                    $"Registers all <c>[PulseHandler]</c>-annotated handlers from the assembly <c>{assemblyName}</c> into the DI container."
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
        public string HandlerTypeName { get; }
        public HandlerRegistration[] Registrations { get; }
        public Location Location { get; }

        public HandlerInfo(string handlerTypeName, HandlerRegistration[] registrations, Location location)
        {
            HandlerTypeName = handlerTypeName;
            Registrations = registrations;
            Location = location;
        }

        public bool Equals(HandlerInfo other) =>
            string.Equals(HandlerTypeName, other.HandlerTypeName, StringComparison.Ordinal)
            && RegistrationsEqual(Registrations, other.Registrations);

        public override bool Equals(object obj) => obj is HandlerInfo other && Equals(other);

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
