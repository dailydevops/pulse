namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// An emitted test assembly loaded into a collectible <see cref="AssemblyLoadContext"/>. The context resolves the
/// referenced assemblies from the default context, so Pulse and DI types are shared with the test code.
/// </summary>
internal sealed class LoadedAssembly : IDisposable
{
    private readonly AssemblyLoadContext _context;

    public LoadedAssembly(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            );
        }

        _ = stream.Seek(0, SeekOrigin.Begin);
        _context = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        Assembly = _context.LoadFromStream(stream);
    }

    public Assembly Assembly { get; }

    /// <summary>Returns the type with the given full name from the loaded assembly.</summary>
    public Type GetTypeByName(string fullName) => Assembly.GetType(fullName, throwOnError: true)!;

    /// <summary>Invokes the generated <c>Add*PulseHandlers</c> method on <paramref name="services"/>.</summary>
    public IServiceCollection AddPulseHandlers(IServiceCollection services)
    {
        var method = Assembly
            .GetTypes()
            .Single(type => string.Equals(type.Name, "PulseRegistrationExtensions", StringComparison.Ordinal))
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name.EndsWith("PulseHandlers", StringComparison.Ordinal));

        return (IServiceCollection)method.Invoke(null, [services])!;
    }

    public void Dispose() => _context.Unload();
}
