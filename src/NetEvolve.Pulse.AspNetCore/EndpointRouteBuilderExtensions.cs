namespace NetEvolve.Pulse;

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to map Pulse mediator
/// commands and queries directly to Minimal API HTTP endpoints.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a command to an HTTP endpoint. The command is bound from the request body,
    /// dispatched via <see cref="IMediatorSendOnly.SendAsync{TCommand, TResponse}"/>, and the result
    /// is returned as <c>200 OK</c>.
    /// </summary>
    /// <typeparam name="TCommand">The command type. Must implement <see cref="ICommand{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The response type returned by the command.</typeparam>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the endpoint to.</param>
    /// <param name="pattern">The route pattern for the endpoint.</param>
    /// <param name="httpMethod">
    /// The HTTP method to use for the endpoint. Defaults to <see cref="CommandHttpMethod.Post"/>.
    /// </param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> to further configure the endpoint.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="httpMethod"/> is not a defined <see cref="CommandHttpMethod"/> value.
    /// </exception>
    /// <example>
    /// <code>
    /// // Default POST
    /// app.MapCommand&lt;CreateOrderCommand, OrderResult&gt;("/orders");
    ///
    /// // Custom method
    /// app.MapCommand&lt;UpdateOrderCommand, OrderResult&gt;("/orders/{id}", CommandHttpMethod.Put);
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapCommand<TCommand, TResponse>(
        [NotNull] this IEndpointRouteBuilder endpoints,
        [NotNull] string pattern,
        CommandHttpMethod httpMethod = CommandHttpMethod.Post
    )
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        return endpoints.MapMethods(
            pattern,
            [httpMethod.ToHttpMethodString()],
            async ([FromBody] TCommand command, IMediator mediator, CancellationToken cancellationToken) =>
                TypedResults.Ok(
                    await mediator.SendAsync<TCommand, TResponse>(command, cancellationToken).ConfigureAwait(false)
                )
        );
    }

    /// <summary>
    /// Maps a void command to an HTTP endpoint. The command is bound from the request body,
    /// dispatched via <see cref="IMediatorSendOnly.SendAsync{TCommand}"/>, and returns <c>204 No Content</c>.
    /// </summary>
    /// <typeparam name="TCommand">The command type. Must implement <see cref="ICommand"/>.</typeparam>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the endpoint to.</param>
    /// <param name="pattern">The route pattern for the endpoint.</param>
    /// <param name="httpMethod">
    /// The HTTP method to use for the endpoint. Defaults to <see cref="CommandHttpMethod.Post"/>.
    /// </param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> to further configure the endpoint.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="httpMethod"/> is not a defined <see cref="CommandHttpMethod"/> value.
    /// </exception>
    /// <example>
    /// <code>
    /// // Default POST
    /// app.MapCommand&lt;DeleteOrderCommand&gt;("/orders/{id}");
    ///
    /// // Custom method
    /// app.MapCommand&lt;DeleteOrderCommand&gt;("/orders/{id}", CommandHttpMethod.Delete);
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapCommand<TCommand>(
        [NotNull] this IEndpointRouteBuilder endpoints,
        [NotNull] string pattern,
        CommandHttpMethod httpMethod = CommandHttpMethod.Post
    )
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        return endpoints.MapMethods(
            pattern,
            [httpMethod.ToHttpMethodString()],
            async ([FromBody] TCommand command, IMediator mediator, CancellationToken cancellationToken) =>
            {
                await mediator.SendAsync(command, cancellationToken).ConfigureAwait(false);
                return TypedResults.NoContent();
            }
        );
    }

    /// <summary>
    /// Maps a query to a <c>GET</c> HTTP endpoint. The query is bound from route parameters and
    /// query string using <c>[AsParameters]</c> binding, dispatched via
    /// <see cref="IMediator.QueryAsync{TQuery, TResponse}"/>, and the result is returned as <c>200 OK</c>.
    /// </summary>
    /// <typeparam name="TQuery">The query type. Must implement <see cref="IQuery{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The response type returned by the query.</typeparam>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the endpoint to.</param>
    /// <param name="pattern">The route pattern for the endpoint.</param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> to further configure the endpoint.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapQuery&lt;GetOrderQuery, OrderDto&gt;("/orders/{id}");
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapQuery<TQuery, TResponse>(
        [NotNull] this IEndpointRouteBuilder endpoints,
        [NotNull] string pattern
    )
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        return endpoints.MapGet(
            pattern,
            async ([AsParameters] TQuery query, IMediator mediator, CancellationToken cancellationToken) =>
                TypedResults.Ok(
                    await mediator.QueryAsync<TQuery, TResponse>(query, cancellationToken).ConfigureAwait(false)
                )
        );
    }

    private static string ToHttpMethodString(this CommandHttpMethod method) =>
        method switch
        {
            CommandHttpMethod.Post => "POST",
            CommandHttpMethod.Put => "PUT",
            CommandHttpMethod.Patch => "PATCH",
            CommandHttpMethod.Delete => "DELETE",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Invalid CommandHttpMethod value."),
        };
}
