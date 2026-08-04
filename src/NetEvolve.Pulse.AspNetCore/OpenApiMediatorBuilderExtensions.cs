namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Provides extension methods for enabling automatic OpenAPI metadata generation for endpoints
/// mapped via <see cref="EndpointRouteBuilderExtensions"/>.
/// </summary>
/// <seealso cref="AspNetCoreOptions"/>
public static class OpenApiMediatorBuilderExtensions
{
    /// <summary>
    /// Enables automatic OpenAPI metadata (summary and produced response types) for endpoints
    /// mapped via <see cref="EndpointRouteBuilderExtensions.MapCommand{TCommand, TResponse}"/>,
    /// <see cref="EndpointRouteBuilderExtensions.MapCommand{TCommand}"/>,
    /// <see cref="EndpointRouteBuilderExtensions.MapQuery{TQuery, TResponse}"/>, and
    /// <see cref="EndpointRouteBuilderExtensions.MapStreamQuery{TQuery, TResponse}"/>.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// services.AddPulse(builder =>
    /// {
    ///     builder.EnableOpenApiMetadata();
    /// });
    /// </code>
    /// </example>
    public static IMediatorBuilder EnableOpenApiMetadata(this IMediatorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Services.Configure<AspNetCoreOptions>(options => options.OpenApiMetadataEnabled = true);

        return builder;
    }
}
