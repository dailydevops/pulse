namespace NetEvolve.Pulse;

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Provides extension methods for mapping Pulse gRPC streaming services on an <see cref="IEndpointRouteBuilder"/>.
/// </summary>
public static class PulseGrpcEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the gRPC service <typeparamref name="TService"/>, typically derived from
    /// <see cref="PulseGrpcStreamService{TQuery, TResponse}"/>, so its server-streaming RPCs are served.
    /// </summary>
    /// <typeparam name="TService">
    /// The concrete, bindable gRPC service type (annotated with <c>BindServiceMethodAttribute</c>).
    /// </typeparam>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the gRPC service to.</param>
    /// <returns>A <see cref="GrpcServiceEndpointConventionBuilder"/> to further configure the service endpoints.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The method takes the service type instead of <c>&lt;TQuery, TResponse&gt;</c> because ASP.NET Core gRPC can only
    /// bind a concrete service type. Requires <c>services.AddGrpc()</c>.
    /// The service is mapped without authorization; call <c>RequireAuthorization(...)</c> on the returned builder
    /// or annotate the service with <c>[Authorize]</c> to secure it.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddGrpc();
    /// app.MapStreamQueryGrpc&lt;OrderStreamService&gt;();
    /// </code>
    /// </example>
    public static GrpcServiceEndpointConventionBuilder MapStreamQueryGrpc<
        [DynamicallyAccessedMembers(GrpcServiceMembers)] TService
    >([NotNull] this IEndpointRouteBuilder endpoints)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGrpcService<TService>();
    }

    // Must cover the annotation on Grpc.AspNetCore's MapGrpcService<TService> (IL2091 otherwise).
    private const DynamicallyAccessedMemberTypes GrpcServiceMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicMethods
        | DynamicallyAccessedMemberTypes.NonPublicMethods;
}
