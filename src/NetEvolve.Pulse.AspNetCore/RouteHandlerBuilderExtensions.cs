namespace NetEvolve.Pulse;

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NetEvolve.Pulse.Internals;

/// <summary>
/// Provides opt-in extension methods for <see cref="RouteHandlerBuilder"/> to attach OpenAPI
/// metadata to endpoints mapped via <see cref="EndpointRouteBuilderExtensions"/>.
/// </summary>
public static class RouteHandlerBuilderExtensions
{
    /// <summary>
    /// Applies an OpenAPI summary derived from the <c>&lt;summary&gt;</c> XML documentation
    /// comment of <typeparamref name="T"/>. When no XML documentation is found, falls back to
    /// <c><see langword="typeof"/>(T).Name</c>.
    /// </summary>
    /// <typeparam name="T">The type whose XML documentation summary is used.</typeparam>
    /// <param name="builder">The <see cref="RouteHandlerBuilder"/> to configure.</param>
    /// <returns>The <paramref name="builder"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// app.MapCommand&lt;CreateOrderCommand, OrderResult&gt;("/orders")
    ///     .WithPulseSummary&lt;CreateOrderCommand&gt;();
    /// </code>
    /// </example>
    public static RouteHandlerBuilder WithPulseSummary<T>([NotNull] this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var summary = XmlDocumentationReader.TryGetSummary(typeof(T), out var xmlSummary)
            ? xmlSummary!
            : typeof(T).Name;

        return builder.WithSummary(summary);
    }

    /// <summary>
    /// Applies an OpenAPI description to the endpoint.
    /// </summary>
    /// <param name="builder">The <see cref="RouteHandlerBuilder"/> to configure.</param>
    /// <param name="description">The description text.</param>
    /// <returns>The <paramref name="builder"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="builder"/> or <paramref name="description"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapCommand&lt;CreateOrderCommand, OrderResult&gt;("/orders")
    ///     .WithPulseDescription("Creates a new order.");
    /// </code>
    /// </example>
    public static RouteHandlerBuilder WithPulseDescription(
        [NotNull] this RouteHandlerBuilder builder,
        [NotNull] string description
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(description);

        return builder.WithDescription(description);
    }

    /// <summary>
    /// Applies an OpenAPI tag to the endpoint.
    /// </summary>
    /// <param name="builder">The <see cref="RouteHandlerBuilder"/> to configure.</param>
    /// <param name="tag">The tag name.</param>
    /// <returns>The <paramref name="builder"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="builder"/> or <paramref name="tag"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapCommand&lt;CreateOrderCommand, OrderResult&gt;("/orders")
    ///     .WithPulseTag("Orders");
    /// </code>
    /// </example>
    public static RouteHandlerBuilder WithPulseTag([NotNull] this RouteHandlerBuilder builder, [NotNull] string tag)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tag);

        return builder.WithTags(tag);
    }

    /// <summary>
    /// Declares that the endpoint produces <typeparamref name="TResponse"/> with status code
    /// <c>200 OK</c>.
    /// </summary>
    /// <typeparam name="TResponse">The response type produced by the endpoint.</typeparam>
    /// <param name="builder">The <see cref="RouteHandlerBuilder"/> to configure.</param>
    /// <returns>The <paramref name="builder"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// app.MapCommand&lt;CreateOrderCommand, OrderResult&gt;("/orders")
    ///     .WithPulseProduces&lt;OrderResult&gt;();
    /// </code>
    /// </example>
    public static RouteHandlerBuilder WithPulseProduces<TResponse>([NotNull] this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Produces<TResponse>(StatusCodes.Status200OK);
    }

    /// <summary>
    /// Declares that the endpoint produces <c>text/event-stream</c> or <c>application/x-ndjson</c>
    /// content with status code <c>200 OK</c>, matching the content negotiation performed by
    /// <see cref="EndpointRouteBuilderExtensions.MapStreamQuery{TQuery, TResponse}"/>.
    /// </summary>
    /// <param name="builder">The <see cref="RouteHandlerBuilder"/> to configure.</param>
    /// <returns>The <paramref name="builder"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// app.MapStreamQuery&lt;GetOrdersStreamQuery, OrderDto&gt;("/orders/stream")
    ///     .WithPulseStreamProduces();
    /// </code>
    /// </example>
    public static RouteHandlerBuilder WithPulseStreamProduces([NotNull] this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        return builder.Produces(StatusCodes.Status200OK, contentType: "application/x-ndjson");
    }
}
