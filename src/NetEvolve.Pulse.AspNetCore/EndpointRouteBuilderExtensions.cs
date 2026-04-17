namespace NetEvolve.Pulse;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
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
    private const string NdjsonContentType = "application/x-ndjson";

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

    /// <summary>
    /// Maps a streaming query to a <c>GET</c> HTTP endpoint that streams results as
    /// Server-Sent Events (SSE) or newline-delimited JSON (NDJSON), depending on the
    /// <c>Accept</c> request header. When the <c>Accept</c> header contains
    /// <c>application/x-ndjson</c>, each item is serialized to JSON and written as a
    /// line followed by a newline character using <see cref="TypedResults.Stream(Func{Stream,Task},string?,string?,DateTimeOffset?,Microsoft.Net.Http.Headers.EntityTagHeaderValue?)"/>.
    /// Otherwise, items are streamed as SSE with <c>Content-Type: text/event-stream</c>.
    /// </summary>
    /// <typeparam name="TQuery">
    /// The query type. Must implement <see cref="IStreamQuery{TResponse}"/>.
    /// </typeparam>
    /// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the endpoint to.</param>
    /// <param name="pattern">The route pattern for the endpoint.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> to further configure the endpoint.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapStreamQuery&lt;GetOrdersStreamQuery, OrderDto&gt;("/orders/stream");
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapStreamQuery<TQuery, TResponse>(
        [NotNull] this IEndpointRouteBuilder endpoints,
        [NotNull] string pattern
    )
        where TQuery : IStreamQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        return endpoints.MapGet(
            pattern,
            (
                [AsParameters] TQuery query,
                IMediator mediator,
                HttpRequest request,
                CancellationToken cancellationToken
            ) =>
            {
                var items = mediator.StreamQueryAsync<TQuery, TResponse>(query, cancellationToken);

                if (request.Headers.Accept.Contains(NdjsonContentType))
                {
                    return (IResult)
                        TypedResults.Stream(
                            async outputStream =>
                            {
                                try
                                {
                                    await foreach (var item in items.ConfigureAwait(false))
                                    {
                                        var json = JsonSerializer.SerializeToUtf8Bytes(item);
                                        await outputStream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
                                        await outputStream
                                            .WriteAsync(new byte[] { (byte)'\n' }, cancellationToken)
                                            .ConfigureAwait(false);
                                        await outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    // Client disconnected cleanly; do not re-throw.
                                }
                            },
                            contentType: NdjsonContentType
                        );
                }

#if NET10_0_OR_GREATER
                return TypedResults.ServerSentEvents(items);
#else
                return TypedResults.Stream(
                    async outputStream =>
                    {
                        try
                        {
                            await foreach (var item in items.ConfigureAwait(false))
                            {
                                var line = Encoding.UTF8.GetBytes($"data: {JsonSerializer.Serialize(item)}\n\n");
                                await outputStream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
                                await outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Client disconnected cleanly; do not re-throw.
                        }
                    },
                    contentType: "text/event-stream"
                );
#endif
            }
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
