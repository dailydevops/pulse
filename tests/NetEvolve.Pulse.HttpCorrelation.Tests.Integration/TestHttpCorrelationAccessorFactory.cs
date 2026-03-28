namespace NetEvolve.Pulse.HttpCorrelation.Tests.Integration;

using System.Reflection;
using Microsoft.AspNetCore.Http;
using NetEvolve.Http.Correlation.Abstractions;
using NetEvolve.Http.Correlation.AspNetCore;

/// <summary>
/// Factory helper that creates controllable <see cref="IHttpCorrelationAccessor"/> instances for integration testing.
/// </summary>
internal static class TestHttpCorrelationAccessorFactory
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

    internal static IHttpCorrelationAccessor Create(string? correlationId)
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
