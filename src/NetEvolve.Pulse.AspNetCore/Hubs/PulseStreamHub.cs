namespace NetEvolve.Pulse;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// SignalR hub that exposes a single streaming query through a native SignalR server-to-client
/// stream. Clients invoke the <c>StreamAsync</c> hub method with a <typeparamref name="TQuery"/>
/// payload and receive every item yielded by
/// <see cref="IMediator.StreamQueryAsync{TQuery, TResponse}(TQuery, CancellationToken)"/> as a stream item.
/// </summary>
/// <typeparam name="TQuery">
/// The query type. Must implement <see cref="IStreamQuery{TResponse}"/> and be deserializable by
/// the configured SignalR hub protocol.
/// </typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
/// <remarks>
/// <para>
/// SignalR does not support generic hub methods, so the query and response types are closed on the
/// hub class. Map one hub per query type via
/// <see cref="EndpointRouteBuilderExtensions.MapStreamQueryHub{TQuery, TResponse}(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder, string)"/>.
/// </para>
/// <para>
/// Cancellation: SignalR triggers the <see cref="CancellationToken"/> of a streaming hub method when the
/// client unsubscribes from the stream (for example <c>subscription.dispose()</c> in JavaScript or
/// cancelling the token passed to <c>HubConnection.StreamAsync</c> in .NET) or disconnects. The hub then
/// ends the stream without raising an exception.
/// </para>
/// </remarks>
public class PulseStreamHub<TQuery, TResponse> : Hub
    where TQuery : IStreamQuery<TResponse>
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseStreamHub{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="mediator">The mediator used to execute the streaming query.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="mediator"/> is <see langword="null"/>.</exception>
    public PulseStreamHub([NotNull] IMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);

        _mediator = mediator;
    }

    /// <summary>
    /// Executes the streaming query and streams every item to the calling client.
    /// </summary>
    /// <param name="query">The streaming query to execute.</param>
    /// <param name="cancellationToken">
    /// A token that SignalR cancels when the client unsubscribes from the stream or disconnects.
    /// </param>
    /// <returns>An asynchronous sequence of result items that SignalR streams to the caller.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="query"/> is <see langword="null"/>.</exception>
    public IAsyncEnumerable<TResponse> StreamAsync([NotNull] TQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return StreamCoreAsync(
            _mediator.StreamQueryAsync<TQuery, TResponse>(query, cancellationToken),
            cancellationToken
        );
    }

    private static async IAsyncEnumerable<TResponse> StreamCoreAsync(
        IAsyncEnumerable<TResponse> items,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var enumerator = items.GetAsyncEnumerator(cancellationToken);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        yield break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Client unsubscribed or disconnected; end the stream cleanly.
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }
}
