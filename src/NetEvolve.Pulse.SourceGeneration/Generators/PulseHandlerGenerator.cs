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
using static NetEvolve.Pulse.SourceGeneration.WellKnownTypeNames;

/// <summary>
/// Roslyn incremental source generator that emits DI registrations for classes annotated with
/// <c>[PulseHandler]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PulseHandlerGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Default service lifetime value matching <c>PulseServiceLifetime.Scoped</c>.
    /// </summary>
    private const int DefaultLifetime = 1; // PulseServiceLifetime.Scoped

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var regularHandlerInfos = BuildRegularHandlerInfosPipeline(context);
        var explicitHandlerInfos = BuildExplicitHandlerInfosPipeline(context);
        var genericHandlerInfos = BuildGenericHandlerInfosPipeline(context);

        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _1) =>
            {
                _ = provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns);
                return ns;
            }
        );

        var assemblyName = context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName);

        var allCollected = regularHandlerInfos
            .Collect()
            .Combine(explicitHandlerInfos.Collect())
            .Combine(genericHandlerInfos.Collect())
            .Select(
                static (pair, _) =>
                {
                    var result = ImmutableArray<HandlerInfo>.Empty;
                    if (!pair.Left.Left.IsDefaultOrEmpty)
                    {
                        result = [.. result, .. pair.Left.Left];
                    }

                    if (!pair.Left.Right.IsDefaultOrEmpty)
                    {
                        result = [.. result, .. pair.Left.Right];
                    }

                    if (!pair.Right.IsDefaultOrEmpty)
                    {
                        result = [.. result, .. pair.Right];
                    }

                    return result;
                }
            );

        var combined = allCollected.Combine(rootNamespace).Combine(assemblyName);

        context.RegisterSourceOutput(
            combined,
            static (spc, data) => Execute(spc, data.Left.Left, data.Left.Right, data.Right)
        );

        RegisterOpenGenericDiagnosticPipeline(context);
        RegisterUnannotatedHandlerDiagnosticPipeline(context);
        RegisterExplicitMessageTypeDiagnosticPipeline(context);
    }

    /// <summary>
    /// Builds the incremental pipeline that collects <c>[PulseHandler]</c>-annotated (non-generic
    /// attribute) types and returns a provider of <see cref="HandlerInfo"/> values.
    /// </summary>
    private static IncrementalValuesProvider<HandlerInfo> BuildRegularHandlerInfosPipeline(
        IncrementalGeneratorInitializationContext context
    ) =>
        context
            .SyntaxProvider.ForAttributeWithMetadataName(
                PulseHandlerAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ExtractHandlerInfo(ctx, ct)
            )
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);

    /// <summary>
    /// Builds the incremental pipeline that collects <c>[PulseHandler&lt;T&gt;]</c>-annotated types
    /// and returns a provider of <see cref="HandlerInfo"/> values derived from the explicit message
    /// type arguments.
    /// </summary>
    private static IncrementalValuesProvider<HandlerInfo> BuildExplicitHandlerInfosPipeline(
        IncrementalGeneratorInitializationContext context
    ) =>
        context
            .SyntaxProvider.ForAttributeWithMetadataName(
                PulseHandlerGenericAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ExtractExplicitHandlerInfo(ctx, ct)
            )
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);

    /// <summary>
    /// Builds the incremental pipeline that collects <c>[PulseGenericHandler]</c>-annotated open
    /// generic types and returns a provider of <see cref="HandlerInfo"/> values for open-generic
    /// DI registrations.
    /// </summary>
    private static IncrementalValuesProvider<HandlerInfo> BuildGenericHandlerInfosPipeline(
        IncrementalGeneratorInitializationContext context
    ) =>
        context
            .SyntaxProvider.ForAttributeWithMetadataName(
                PulseGenericHandlerAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ExtractPureGenericHandlerInfo(ctx, ct)
            )
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);

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
    /// Registers the pipeline that reports PULSE005 and PULSE006 for invalid or incompatible
    /// message type arguments passed to <c>[PulseHandler&lt;T&gt;]</c>.
    /// </summary>
    private static void RegisterExplicitMessageTypeDiagnosticPipeline(IncrementalGeneratorInitializationContext context)
    {
        var errors = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                PulseHandlerGenericAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ExtractExplicitTypeErrors(ctx, ct)
            )
            .Where(static arr => !arr.IsDefaultOrEmpty)
            .SelectMany(static (arr, _) => arr);

        context.RegisterSourceOutput(
            errors,
            static (spc, error) =>
                spc.ReportDiagnostic(
                    Diagnostic.Create(
                        error.IsPulse005
                            ? DiagnosticDescriptors.InvalidExplicitMessageType
                            : DiagnosticDescriptors.IncompatibleExplicitMessageType,
                        error.Location,
                        error.MessageTypeName,
                        error.HandlerTypeName
                    )
                )
        );
    }

    /// <summary>
    /// Extracts handler registration info from a class annotated with <c>[PulseHandler&lt;T&gt;]</c>.
    /// Returns <c>null</c> when no valid registration can be produced (errors are reported separately).
    /// </summary>
    private static HandlerInfo? ExtractExplicitHandlerInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        var location = ctx.TargetNode.GetLocation();
        var registrations = new List<HandlerRegistration>();

        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass?.TypeArguments.Length != 1)
            {
                continue;
            }

            if (attr.AttributeClass.TypeArguments[0] is not INamedTypeSymbol messageType)
            {
                continue;
            }

            var lifetime = ReadLifetimeFromSingleAttr(attr);
            var reg = TryBuildExplicitRegistration(classSymbol, messageType, lifetime);
            if (reg.HasValue)
            {
                registrations.Add(reg.Value);
            }
        }

        if (registrations.Count == 0)
        {
            return null;
        }

        return new HandlerInfo(GetFullyQualifiedName(classSymbol), [.. registrations], location);
    }

    /// <summary>
    /// Extracts <see cref="ExplicitTypeError"/> diagnostics for each invalid or incompatible
    /// message type argument passed to <c>[PulseHandler&lt;T&gt;]</c> on the annotated class.
    /// </summary>
    private static ImmutableArray<ExplicitTypeError> ExtractExplicitTypeErrors(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return [];
        }

        var handlerTypeName = GetFullyQualifiedName(classSymbol);
        var location = ctx.TargetNode.GetLocation();
        var errors = ImmutableArray.CreateBuilder<ExplicitTypeError>();

        foreach (var attrClass in ctx.Attributes.Select(attr => attr.AttributeClass))
        {
            ct.ThrowIfCancellationRequested();

            if (attrClass?.TypeArguments.Length != 1)
            {
                continue;
            }

            if (attrClass.TypeArguments[0] is not INamedTypeSymbol messageType)
            {
                continue;
            }

            var messageTypeName = messageType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (!TryGetMessageInfo(messageType, out _, out _, out _))
            {
                errors.Add(new ExplicitTypeError(messageTypeName, handlerTypeName, location, isPulse005: true));
                continue;
            }

            if (!TryBuildExplicitRegistration(classSymbol, messageType, DefaultLifetime).HasValue)
            {
                errors.Add(new ExplicitTypeError(messageTypeName, handlerTypeName, location, isPulse005: false));
            }
        }

        return errors.ToImmutable();
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
    /// Extracts an open-generic <see cref="HandlerInfo"/> from a class annotated with
    /// <c>[PulseGenericHandler]</c>. The handler and service type names use the unbound generic
    /// syntax (e.g. <c>global::Ns.MyHandler&lt;,&gt;</c>) so that the emitter can produce
    /// <c>typeof()</c>-based DI registrations. Returns <c>null</c> when the symbol is not an
    /// open generic or cannot be resolved.
    /// </summary>
    private static HandlerInfo? ExtractPureGenericHandlerInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        // [PulseGenericHandler] is only meaningful for open generic types.
        if (classSymbol.TypeParameters.Length == 0)
        {
            return null;
        }

        var lifetime = ReadLifetime(ctx.Attributes);
        var location = ctx.TargetNode.GetLocation();
        var registrations = BuildOpenGenericHandlerRegistrations(classSymbol, lifetime);

        return new HandlerInfo(GetOpenGenericTypeName(classSymbol), [.. registrations], location);
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

        // Skip classes already annotated with [PulseHandler], [PulseHandler<T>], or [PulseGenericHandler].
        if (
            classSymbol
                .GetAttributes()
                .Any(attr =>
                    attr.AttributeClass is not null
                    && (
                        string.Equals(
                            GetFullMetadataName(attr.AttributeClass),
                            PulseHandlerAttributeFullName,
                            StringComparison.Ordinal
                        )
                        || string.Equals(
                            GetFullMetadataName(attr.AttributeClass.OriginalDefinition),
                            PulseHandlerGenericAttributeFullName,
                            StringComparison.Ordinal
                        )
                        || string.Equals(
                            GetFullMetadataName(attr.AttributeClass),
                            PulseGenericHandlerAttributeFullName,
                            StringComparison.Ordinal
                        )
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
            allRegistrations.AddRange(
                handler.Registrations.Where(reg =>
                    !duplicatedServiceTypes.Contains(reg.ServiceTypeName)
                    || emittedServiceTypes.Add(reg.ServiceTypeName)
                )
            );
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
                // Group by concrete handler type (preserving order of first occurrence) so that
                // handlers implementing multiple interfaces share a single instance per lifetime.
                var groups = new List<(string HandlerTypeName, List<HandlerRegistration> Regs)>();
                var lookup = new Dictionary<string, List<HandlerRegistration>>(StringComparer.Ordinal);
                foreach (var reg in registrations)
                {
                    if (!lookup.TryGetValue(reg.HandlerTypeName, out var list))
                    {
                        list = [];
                        lookup[reg.HandlerTypeName] = list;
                        groups.Add((reg.HandlerTypeName, list));
                    }
                    list.Add(reg);
                }

                foreach (var (handlerTypeName, regs) in groups)
                {
                    var lifetimeMethodName = GetLifetimeMethodName(regs[0].Lifetime);

                    if (regs[0].IsOpenGeneric)
                    {
                        // Open-generic registrations use typeof() syntax; no shared-instance
                        // optimization applies because DI resolves each closed type independently.
                        foreach (var reg in regs)
                        {
                            _ = cb.AppendLine(
                                $"services.{lifetimeMethodName}(typeof({reg.ServiceTypeName}), typeof({handlerTypeName}));"
                            );
                        }
                    }
                    else if (regs.Count == 1)
                    {
                        _ = cb.AppendLine(
                            $"services.{lifetimeMethodName}<{regs[0].ServiceTypeName}, {handlerTypeName}>();"
                        );
                    }
                    else
                    {
                        // Register the concrete type once so all interface resolutions share
                        // the same instance within the same scope/lifetime.
                        _ = cb.AppendLine($"services.{lifetimeMethodName}<{handlerTypeName}>();");
                        foreach (var reg in regs)
                        {
                            _ = cb.AppendLine(
                                $"services.{lifetimeMethodName}<{reg.ServiceTypeName}>(static sp => sp.GetRequiredService<{handlerTypeName}>());"
                            );
                        }
                    }
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

        return $"Add{assemblyName!.Replace(".", string.Empty)}PulseHandlers";
    }

    /// <summary>
    /// Reads the <c>Lifetime</c> named argument from the <c>[PulseHandler]</c> attribute data.
    /// Returns <see cref="DefaultLifetime"/> when no explicit value is set.
    /// </summary>
    private static int ReadLifetime(ImmutableArray<AttributeData> attributes)
    {
        var firstAttribute = attributes.FirstOrDefault();

        if (firstAttribute is null)
        {
            return DefaultLifetime;
        }

        return ReadLifetimeFromSingleAttr(firstAttribute);
    }

    /// <summary>
    /// Reads the <c>Lifetime</c> named argument from a single <c>[PulseHandler&lt;T&gt;]</c>
    /// attribute instance. Returns <see cref="DefaultLifetime"/> when no explicit value is set.
    /// </summary>
    private static int ReadLifetimeFromSingleAttr(AttributeData attr)
    {
        foreach (var namedArg in attr.NamedArguments)
        {
            if (string.Equals(namedArg.Key, "Lifetime", StringComparison.Ordinal) && !namedArg.Value.IsNull)
            {
                return (int)namedArg.Value.Value!;
            }
        }

        return DefaultLifetime;
    }

    /// <summary>
    /// Determines which Pulse handler interface kind and expected result type correspond to the
    /// message interface implemented by <paramref name="messageType"/>.
    /// Returns <see langword="true"/> when a known Pulse message interface is found.
    /// </summary>
    private static bool TryGetMessageInfo(
        INamedTypeSymbol messageType,
        out string expectedHandlerInterfaceName,
        out HandlerKind kind,
        out ITypeSymbol? resultType
    )
    {
        foreach (var iface in messageType.AllInterfaces)
        {
            var metadataName = GetFullMetadataName(iface.OriginalDefinition);

            if (string.Equals(metadataName, CommandMessageInterfaceName, StringComparison.Ordinal))
            {
                expectedHandlerInterfaceName = CommandHandlerInterfaceName;
                kind = HandlerKind.Command;
                resultType = iface.TypeArguments[0];
                return true;
            }

            if (string.Equals(metadataName, QueryMessageInterfaceName, StringComparison.Ordinal))
            {
                expectedHandlerInterfaceName = QueryHandlerInterfaceName;
                kind = HandlerKind.Query;
                resultType = iface.TypeArguments[0];
                return true;
            }

            if (string.Equals(metadataName, EventMessageInterfaceName, StringComparison.Ordinal))
            {
                expectedHandlerInterfaceName = EventHandlerInterfaceName;
                kind = HandlerKind.Event;
                resultType = null;
                return true;
            }

            if (string.Equals(metadataName, StreamQueryMessageInterfaceName, StringComparison.Ordinal))
            {
                expectedHandlerInterfaceName = StreamQueryHandlerInterfaceName;
                kind = HandlerKind.StreamQuery;
                resultType = iface.TypeArguments[0];
                return true;
            }
        }

        expectedHandlerInterfaceName = null!;
        kind = default;
        resultType = null;
        return false;
    }

    /// <summary>
    /// Attempts to build a single <see cref="HandlerRegistration"/> for an explicit message type
    /// argument from <c>[PulseHandler&lt;T&gt;]</c>. Returns <see langword="null"/> when the
    /// registration cannot be constructed.
    /// </summary>
    private static HandlerRegistration? TryBuildExplicitRegistration(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol messageType,
        int lifetime
    )
    {
        if (!TryGetMessageInfo(messageType, out var expectedHandlerIfaceName, out var kind, out var resultType))
        {
            return null;
        }

        // Find the matching open handler interface in the class's AllInterfaces.
        var matchingIface = classSymbol.AllInterfaces.FirstOrDefault(iface =>
            string.Equals(
                GetFullMetadataName(iface.OriginalDefinition),
                expectedHandlerIfaceName,
                StringComparison.Ordinal
            )
        );

        if (matchingIface is null)
        {
            return null;
        }

        // For concrete (non-generic) classes, verify the interface uses the expected message type.
        if (classSymbol.TypeParameters.Length == 0)
        {
            if (
                matchingIface.TypeArguments.Length == 0
                || !SymbolEqualityComparer.Default.Equals(matchingIface.TypeArguments[0], messageType)
            )
            {
                return null;
            }

            return new HandlerRegistration(
                GetFullyQualifiedName(classSymbol),
                GetFullyQualifiedName(matchingIface),
                kind,
                lifetime
            );
        }

        // Build the type argument substitution for classSymbol.Construct.
        // matchingIface.TypeArguments are references to classSymbol.TypeParameters (for open generics).
        // Position 0 → message type, position 1 (if present) → result type.
        var handlerTypeArgs = new ITypeSymbol[classSymbol.TypeParameters.Length];
        var filled = new bool[classSymbol.TypeParameters.Length];

        for (var i = 0; i < matchingIface.TypeArguments.Length; i++)
        {
            if (matchingIface.TypeArguments[i] is not ITypeParameterSymbol tp)
            {
                continue;
            }

            var substitution = i == 0 ? (ITypeSymbol)messageType : resultType;
            if (substitution is null)
            {
                continue;
            }

            for (var j = 0; j < classSymbol.TypeParameters.Length; j++)
            {
                if (SymbolEqualityComparer.Default.Equals(classSymbol.TypeParameters[j], tp))
                {
                    handlerTypeArgs[j] = substitution;
                    filled[j] = true;
                    break;
                }
            }
        }

        // All class type parameters must be filled to produce a valid closed generic.
        for (var j = 0; j < filled.Length; j++)
        {
            if (!filled[j])
            {
                return null;
            }
        }

        var closedHandler = classSymbol.Construct(handlerTypeArgs);

        // Build the closed service interface type arguments.
        var serviceTypeArgs = new ITypeSymbol[matchingIface.TypeArguments.Length];
        for (var i = 0; i < matchingIface.TypeArguments.Length; i++)
        {
#pragma warning disable S3358 // Ternary operators should not be nested
            serviceTypeArgs[i] =
                matchingIface.TypeArguments[i] is ITypeParameterSymbol
                    ? (i == 0 ? messageType : resultType!)
                    : matchingIface.TypeArguments[i];
#pragma warning restore S3358 // Ternary operators should not be nested
        }

        var closedService = matchingIface.OriginalDefinition.Construct(serviceTypeArgs);

        return new HandlerRegistration(
            GetFullyQualifiedName(closedHandler),
            GetFullyQualifiedName(closedService),
            kind,
            lifetime
        );
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
    /// Builds the list of <see cref="HandlerRegistration"/> entries for all recognized Pulse handler
    /// interfaces implemented by <paramref name="classSymbol"/>, using the unbound open-generic
    /// type names suitable for <c>typeof()</c>-based DI registration.
    /// </summary>
    private static List<HandlerRegistration> BuildOpenGenericHandlerRegistrations(
        INamedTypeSymbol classSymbol,
        int lifetime
    )
    {
        var registrations = new List<HandlerRegistration>();
        var handlerTypeName = GetOpenGenericTypeName(classSymbol);

        foreach (var typeDefinition in classSymbol.AllInterfaces.Select(x => x.OriginalDefinition))
        {
            var metadataName = GetFullMetadataName(typeDefinition);

            if (TryGetHandlerKind(metadataName, out var kind))
            {
                registrations.Add(
                    new HandlerRegistration(
                        handlerTypeName: handlerTypeName,
                        serviceTypeName: GetOpenGenericTypeName(typeDefinition),
                        kind: kind,
                        lifetime: lifetime,
                        isOpenGeneric: true
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
    /// Returns the unbound open-generic fully qualified name of a named type symbol using the
    /// <c>global::</c> prefix (e.g. <c>global::Ns.MyHandler&lt;,&gt;</c>), suitable for use
    /// inside <c>typeof()</c> expressions in generated source. Falls back to
    /// <see cref="GetFullyQualifiedName"/> for non-generic symbols.
    /// </summary>
    private static string GetOpenGenericTypeName(INamedTypeSymbol symbol) =>
        symbol.IsGenericType
            ? symbol.ConstructUnboundGenericType().ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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
}
