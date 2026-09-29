namespace NetEvolve.Pulse;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
#if NET10_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

#if !NET10_0_OR_GREATER
using Microsoft.AspNetCore.Http.Features;
#endif

/// <summary>
/// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to map Pulse mediator
/// commands and queries directly to Minimal API HTTP endpoints.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    private const string NdjsonContentType = "application/x-ndjson";
    private const string SseContentType = "text/event-stream";

    private static readonly byte[] NdjsonNewLine = [(byte)'\n'];

#if !NET10_0_OR_GREATER
    private static readonly byte[] SseDataPrefix = Encoding.UTF8.GetBytes("data: ");
    private static readonly byte[] SseSuffix = Encoding.UTF8.GetBytes("\n\n");
#endif

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
    [RequiresUnreferencedCode(
        "Minimal API endpoint mapping uses RequestDelegateFactory, which reflects over the handler signature and the bound request types."
    )]
    [RequiresDynamicCode(
        "Minimal API endpoint mapping can generate code at runtime to bind parameters and write results."
    )]
    public static IEndpointConventionBuilder MapCommand<TCommand, TResponse>(
        [NotNull] this IEndpointRouteBuilder endpoints,
        [NotNull] string pattern,
        CommandHttpMethod httpMethod = CommandHttpMethod.Post
    )
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        var routeBuilder = endpoints.MapMethods(
            pattern,
            [httpMethod.ToHttpMethodString()],
            async ([FromBody] TCommand command, IMediator mediator, CancellationToken cancellationToken) =>
                TypedResults.Ok(
                    await mediator.SendAsync<TCommand, TResponse>(command, cancellationToken).ConfigureAwait(false)
                )
        );

        ApplyOpenApiMetadata<TCommand, TResponse>(endpoints, routeBuilder);

        return routeBuilder;
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
    [RequiresUnreferencedCode(
        "Minimal API endpoint mapping uses RequestDelegateFactory, which reflects over the handler signature and the bound request types."
    )]
    [RequiresDynamicCode(
        "Minimal API endpoint mapping can generate code at runtime to bind parameters and write results."
    )]
    public static IEndpointConventionBuilder MapCommand<TCommand>(
        [NotNull] this IEndpointRouteBuilder endpoints,
        [NotNull] string pattern,
        CommandHttpMethod httpMethod = CommandHttpMethod.Post
    )
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        var routeBuilder = endpoints.MapMethods(
            pattern,
            [httpMethod.ToHttpMethodString()],
            async ([FromBody] TCommand command, IMediator mediator, CancellationToken cancellationToken) =>
            {
                await mediator.SendAsync(command, cancellationToken).ConfigureAwait(false);
                return TypedResults.NoContent();
            }
        );

        ApplyOpenApiMetadata<TCommand>(endpoints, routeBuilder);

        return routeBuilder;
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
    [RequiresUnreferencedCode(
        "Minimal API endpoint mapping uses RequestDelegateFactory, which reflects over the handler signature and the bound request types."
    )]
    [RequiresDynamicCode(
        "Minimal API endpoint mapping can generate code at runtime to bind parameters and write results."
    )]
    public static IEndpointConventionBuilder MapQuery<TQuery, TResponse>(
        [NotNull] this IEndpointRouteBuilder endpoints,
        [NotNull] string pattern
    )
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        var routeBuilder = endpoints.MapGet(
            pattern,
            async ([AsParameters] TQuery query, IMediator mediator, CancellationToken cancellationToken) =>
                TypedResults.Ok(
                    await mediator.QueryAsync<TQuery, TResponse>(query, cancellationToken).ConfigureAwait(false)
                )
        );

        ApplyOpenApiMetadata<TQuery, TResponse>(endpoints, routeBuilder);

        return routeBuilder;
    }

    /// <summary>
    /// Maps a streaming query to a <c>GET</c> HTTP endpoint that streams results as
    /// Server-Sent Events (SSE) or newline-delimited JSON (NDJSON), depending on the
    /// <c>Accept</c> request header. When the <c>Accept</c> header gives
    /// <c>application/x-ndjson</c> a higher quality value than <c>text/event-stream</c>, each item
    /// is serialized to JSON and written as a line followed by a newline character using
    /// <see cref="TypedResults.Stream(Func{Stream,Task},string?,string?,DateTimeOffset?,Microsoft.Net.Http.Headers.EntityTagHeaderValue?)"/>.
    /// Otherwise, items are streamed as SSE with <c>Content-Type: text/event-stream</c>.
    /// </summary>
    /// <remarks>
    /// The quality value of each media type comes from the most specific matching media range
    /// (<c>type/subtype</c> over <c>type/*</c> over <c>*/*</c>), as defined by RFC 9110 §12.5.1.
    /// A range without <c>q</c> weighs 1, and <c>q=0</c> marks a media type as not acceptable.
    /// SSE is used on ties, when the header is missing or cannot be parsed, and when neither media
    /// type is acceptable; in that last case the endpoint disregards the header, as RFC 9110 §12.5.1
    /// permits, instead of answering <c>406 Not Acceptable</c>. The response carries
    /// <c>Vary: Accept</c>.
    /// <para>
    /// Every item is serialized with the application's HTTP JSON options (<c>ConfigureHttpJsonOptions</c>), the
    /// same contract as <c>MapQuery</c> and <c>MapCommand</c>, with indentation disabled. Each item is therefore a
    /// single-line JSON text: one NDJSON line, or one SSE <c>data:</c> line. <see cref="string"/> items are written
    /// as quoted JSON strings in both formats.
    /// </para>
    /// </remarks>
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
    [RequiresUnreferencedCode(
        "Minimal API endpoint mapping uses RequestDelegateFactory, which reflects over the handler signature and the bound request types."
    )]
    [RequiresDynamicCode(
        "Minimal API endpoint mapping can generate code at runtime to bind parameters and write results."
    )]
    public static IEndpointConventionBuilder MapStreamQuery<TQuery, TResponse>(
        [NotNull] this IEndpointRouteBuilder endpoints,
        [NotNull] string pattern
    )
        where TQuery : IStreamQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        var routeBuilder = endpoints.MapGet(
            pattern,
            (
                [AsParameters] TQuery query,
                IMediator mediator,
                IOptions<HttpJsonOptions> jsonOptions,
                HttpRequest request,
                CancellationToken cancellationToken
            ) =>
            {
                var items = mediator.StreamQueryAsync<TQuery, TResponse>(query, cancellationToken);

                // RFC 9110 §8.3.1 makes media types case-insensitive, and §12.5.1 lets the Accept
                // header carry comma-separated media ranges with q-values (e.g.
                // "text/event-stream, application/x-ndjson;q=0"). Weigh both candidates instead of
                // checking whether NDJSON is merely mentioned. The representation depends on
                // Accept, so announce that to caches (RFC 9110 §12.5.5).
                request.HttpContext.Response.Headers.Append(HeaderNames.Vary, HeaderNames.Accept);
                if (PrefersNdjson(request.Headers.Accept))
                {
                    return (IResult)
                        TypedResults.Stream(
                            ExecuteStreamReadNdjson(items, jsonOptions, cancellationToken),
                            contentType: NdjsonContentType
                        );
                }

#if NET10_0_OR_GREATER
                // Pre-serialized strings take the raw path of ServerSentEventsResult, so every TFM writes
                // the same single-line JSON text per event.
                return TypedResults.ServerSentEvents(SerializeItemsAsync(items, jsonOptions, cancellationToken));
#else
                // Mirror ServerSentEventsResult on .NET 10: no caching, no compression, no buffering.
                var response = request.HttpContext.Response;
                response.Headers.CacheControl = "no-cache,no-store";
                response.Headers.Pragma = "no-cache";
                response.Headers.ContentEncoding = "identity";
                request.HttpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();

                return TypedResults.Stream(
                    ExecuteStreamReadServerSentEvents(items, jsonOptions, cancellationToken),
                    contentType: SseContentType
                );
#endif
            }
        );

        ApplyStreamOpenApiMetadata<TQuery>(endpoints, routeBuilder);

        return routeBuilder;
    }

    /// <summary>
    /// Maps a <see cref="PulseStreamHub{TQuery, TResponse}"/> for the specified streaming query to
    /// <paramref name="path"/>. Clients invoke the <c>StreamAsync</c> hub method as a SignalR
    /// server-to-client stream and receive every item yielded by the query.
    /// </summary>
    /// <typeparam name="TQuery">
    /// The query type. Must implement <see cref="IStreamQuery{TResponse}"/>.
    /// </typeparam>
    /// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the hub to.</param>
    /// <param name="path">The request path of the hub, for example <c>/hubs/orders</c>.</param>
    /// <returns>A <see cref="HubEndpointConventionBuilder"/> to further configure the hub endpoints.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="endpoints"/> or <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if SignalR services have not been registered via <c>services.AddSignalR()</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SignalR services MUST be registered with <c>services.AddSignalR()</c> before calling this method.
    /// </para>
    /// <para>
    /// The hub accepts anonymous connections unless authorization is applied, for example via
    /// <c>.RequireAuthorization()</c> on the returned builder. The query payload is supplied by the client and
    /// is untrusted; handlers or interceptors must validate it and scope it to the calling user.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddSignalR();
    /// // ...
    /// app.MapStreamQueryHub&lt;GetOrdersStreamQuery, OrderDto&gt;("/hubs/orders").RequireAuthorization();
    /// </code>
    /// </example>
    public static HubEndpointConventionBuilder MapStreamQueryHub<TQuery, TResponse>(
        [NotNull] this IEndpointRouteBuilder endpoints,
        [NotNull] string path
    )
        where TQuery : IStreamQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(path);

        return endpoints.MapHub<PulseStreamHub<TQuery, TResponse>>(path);
    }

    private static void ApplyOpenApiMetadata<TRequest, TResponse>(
        IEndpointRouteBuilder endpoints,
        RouteHandlerBuilder routeBuilder
    )
    {
        var options = endpoints.ServiceProvider.GetService<IOptions<AspNetCoreOptions>>();
        if (options?.Value.OpenApiMetadataEnabled == true)
        {
            _ = routeBuilder.WithPulseSummary<TRequest>();
            _ = routeBuilder.WithPulseProduces<TResponse>();
        }
    }

    private static void ApplyOpenApiMetadata<TRequest>(
        IEndpointRouteBuilder endpoints,
        RouteHandlerBuilder routeBuilder
    )
    {
        var options = endpoints.ServiceProvider.GetService<IOptions<AspNetCoreOptions>>();
        if (options?.Value.OpenApiMetadataEnabled == true)
        {
            _ = routeBuilder.WithPulseSummary<TRequest>();
        }
    }

    private static void ApplyStreamOpenApiMetadata<TQuery>(
        IEndpointRouteBuilder endpoints,
        RouteHandlerBuilder routeBuilder
    )
    {
        var options = endpoints.ServiceProvider.GetService<IOptions<AspNetCoreOptions>>();
        if (options?.Value.OpenApiMetadataEnabled == true)
        {
            _ = routeBuilder.WithPulseSummary<TQuery>();
            _ = routeBuilder.WithPulseStreamProduces();
        }
    }

    private static bool PrefersNdjson(StringValues acceptHeader)
    {
        if (!MediaTypeHeaderValue.TryParseList(acceptHeader, out var mediaRanges) || mediaRanges is null)
        {
            return false;
        }

        var ndjsonQuality = GetEffectiveQuality(mediaRanges, "application", NdjsonContentType);
        var sseQuality = GetEffectiveQuality(mediaRanges, "text", SseContentType);

        return ndjsonQuality > 0 && ndjsonQuality > sseQuality;
    }

    // RFC 9110 §12.5.1: the most specific matching range (type/subtype over type/* over */*)
    // determines the weight. A range without "q" weighs 1, no matching range means q=0.
    private static double GetEffectiveQuality(IList<MediaTypeHeaderValue> mediaRanges, string type, string mediaType)
    {
        var bestSpecificity = -1;
        var quality = 0d;

        foreach (var range in mediaRanges)
        {
            var specificity = GetSpecificity(range, type, mediaType);
            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                quality = range.Quality ?? 1d;
            }
        }

        return quality;
    }

    private static int GetSpecificity(MediaTypeHeaderValue range, string type, string mediaType)
    {
        if (range.MatchesAllTypes)
        {
            return 0;
        }

        if (!range.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        if (range.MatchesAllSubTypes)
        {
            return 1;
        }

        return range.MediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
    }

#if NET10_0_OR_GREATER
    private static async IAsyncEnumerable<string> SerializeItemsAsync<TResponse>(
        IAsyncEnumerable<TResponse> items,
        IOptions<HttpJsonOptions> jsonOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await foreach (var item in items.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return PulseStreamJsonOptions.Serialize(item, jsonOptions);
        }
    }
#else
    private static Func<Stream, Task> ExecuteStreamReadServerSentEvents<TResponse>(
        IAsyncEnumerable<TResponse> items,
        IOptions<HttpJsonOptions> jsonOptions,
        CancellationToken cancellationToken
    ) =>
        async outputStream =>
        {
            try
            {
                await foreach (var item in items.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    await outputStream.WriteAsync(SseDataPrefix, cancellationToken).ConfigureAwait(false);
                    var bytes = PulseStreamJsonOptions.SerializeToUtf8Bytes(item, jsonOptions);
                    await outputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await outputStream.WriteAsync(SseSuffix, cancellationToken).ConfigureAwait(false);
                    await outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Client disconnected cleanly; do not re-throw.
            }
        };
#endif

    private static Func<Stream, Task> ExecuteStreamReadNdjson<TResponse>(
        IAsyncEnumerable<TResponse> items,
        IOptions<HttpJsonOptions> jsonOptions,
        CancellationToken cancellationToken
    ) =>
        async outputStream =>
        {
            try
            {
                await foreach (var item in items.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    var bytes = PulseStreamJsonOptions.SerializeToUtf8Bytes(item, jsonOptions);
                    await outputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await outputStream.WriteAsync(NdjsonNewLine, cancellationToken).ConfigureAwait(false);
                    await outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Client disconnected cleanly; do not re-throw.
            }
        };

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
