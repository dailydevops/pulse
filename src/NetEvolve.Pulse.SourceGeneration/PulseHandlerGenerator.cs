namespace NetEvolve.Pulse.SourceGeneration;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Roslyn incremental source generator that emits DI registrations for classes annotated with
/// <c>[PulseHandler]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PulseHandlerGenerator : IIncrementalGenerator
{
    private const string PulseHandlerAttributeFullName = "NetEvolve.Pulse.SourceGeneration.PulseHandlerAttribute";

    private const string CommandHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.ICommandHandler`2";

    private const string CommandHandlerSingleInterfaceName = "NetEvolve.Pulse.Extensibility.ICommandHandler`1";

    private const string QueryHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IQueryHandler`2";

    private const string EventHandlerInterfaceName = "NetEvolve.Pulse.Extensibility.IEventHandler`1";

    /// <summary>
    /// Default service lifetime value matching <c>PulseServiceLifetime.Scoped</c>.
    /// </summary>
    private const int DefaultLifetime = 1; // PulseServiceLifetime.Scoped

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect annotated classes via ForAttributeWithMetadataName for efficient filtering.
        var handlerInfos = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                PulseHandlerAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ExtractHandlerInfo(ctx, ct)
            )
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);

        var collected = handlerInfos.Collect();

        // Resolve the target project's root namespace for the generated code.
        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, ct) =>
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

        return new HandlerInfo(GetFullyQualifiedName(classSymbol), registrations.ToArray(), location);
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
                    handlerNames = new List<string>();
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
            foreach (var reg in handler.Registrations)
            {
                allRegistrations.Add(reg);
            }
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
        var targetNamespace = rootNamespace ?? "NetEvolve.Pulse.Generated";
        var methodName = GetMethodName(assemblyName);

        var sb = new StringBuilder();
        _ = sb.AppendLine("// <auto-generated />");
        _ = sb.AppendLine("#nullable enable");
        _ = sb.AppendLine();
        _ = sb.AppendLine("using System.CodeDom.Compiler;");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"namespace {targetNamespace}");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("    /// <summary>");
        _ = sb.AppendLine(
            "    /// Auto-generated extension method to register Pulse handlers annotated with <c>[PulseHandler]</c>."
        );
        _ = sb.AppendLine("    /// </summary>");
        _ = sb.AppendLine("    [GeneratedCode(\"NetEvolve.Pulse.SourceGeneration\", \"1.0.0\")]");
        _ = sb.AppendLine("    public static partial class PulseHandlerRegistrationExtensions");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine("        /// <summary>");
        _ = sb.AppendLine(
            "        /// Registers all Pulse handlers annotated with <c>[PulseHandler]</c> into the service collection."
        );
        _ = sb.AppendLine("        /// </summary>");
        _ = sb.AppendLine(
            "        /// <param name=\"services\">The service collection to add registrations to.</param>"
        );
        _ = sb.AppendLine(
            "        /// <returns>The same <see cref=\"global::Microsoft.Extensions.DependencyInjection.IServiceCollection\"/> instance for chaining.</returns>"
        );
        _ = sb.AppendLine(
            $"        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {methodName}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)"
        );
        _ = sb.AppendLine("        {");

        foreach (var reg in registrations)
        {
            var lifetimeMethodName = GetLifetimeMethodName(reg.Lifetime);
            _ = sb.AppendLine(
                $"            global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.{lifetimeMethodName}<{reg.ServiceTypeName}, {reg.HandlerTypeName}>(services);"
            );
        }

        _ = sb.AppendLine();
        _ = sb.AppendLine("            return services;");
        _ = sb.AppendLine("        }");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
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
        if (ns is not null && !ns.IsGlobalNamespace)
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
