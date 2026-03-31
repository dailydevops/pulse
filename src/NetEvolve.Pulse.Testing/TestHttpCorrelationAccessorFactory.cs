namespace NetEvolve.Pulse.Testing;

using System.Reflection;
using Microsoft.AspNetCore.Http;
using NetEvolve.Http.Correlation.Abstractions;
using NetEvolve.Http.Correlation.AspNetCore;

/// <summary>
/// Factory helper that creates controllable <see cref="IHttpCorrelationAccessor"/> instances for testing.
/// </summary>
/// <remarks>
/// The <see cref="IHttpCorrelationAccessor"/> interface has <c>internal set</c> accessors, making it
/// impossible to implement from external assemblies. This factory creates instances of the concrete
/// <c>HttpCorrelationAccessor</c> class from <c>NetEvolve.Http.Correlation.AspNetCore</c> via reflection,
/// then sets the <c>_correlationId</c> field directly to control the value returned by
/// <see cref="IHttpCorrelationAccessor.CorrelationId"/>.
/// </remarks>
public static class TestHttpCorrelationAccessorFactory
{
    private static readonly Assembly _aspNetCoreAsm = typeof(ServiceCollectionExtensions).Assembly;

    private static readonly Type _accessorType = _aspNetCoreAsm.GetType(
        "NetEvolve.Http.Correlation.AspNetCore.HttpCorrelationAccessor",
        throwOnError: true
    )!;

    private static readonly FieldInfo _correlationIdField = _accessorType.GetField(
        "_correlationId",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    /// <summary>
    /// Creates a test <see cref="IHttpCorrelationAccessor"/> that returns the specified correlation ID.
    /// </summary>
    /// <param name="correlationId">The correlation ID to return, or <see langword="null"/> / empty to simulate no value.</param>
    /// <returns>An <see cref="IHttpCorrelationAccessor"/> instance with the specified correlation ID.</returns>
    public static IHttpCorrelationAccessor Create(string? correlationId)
    {
        var httpContextAccessor = new EmptyHttpContextAccessor();
        var instance = (IHttpCorrelationAccessor)Activator.CreateInstance(_accessorType, httpContextAccessor)!;
        _correlationIdField.SetValue(instance, correlationId ?? string.Empty);
        return instance;
    }

    private sealed class EmptyHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
