namespace NetEvolve.Pulse;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Grpc.Core;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Base class for gRPC services that expose a Pulse streaming query as a server-streaming RPC.
/// </summary>
/// <typeparam name="TQuery">The streaming query type dispatched through the mediator.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the query and written to the response stream.</typeparam>
/// <remarks>
/// Derived classes carry a <c>BindServiceMethodAttribute</c> and expose a <see langword="public"/> <see langword="virtual"/>
/// RPC method (named like the gRPC method, declared on the bound type) that maps the incoming request to
/// <typeparamref name="TQuery"/> and calls <see cref="StreamAsync(TQuery, IServerStreamWriter{TResponse}, ServerCallContext)"/>.
/// Register the derived service with <see cref="PulseGrpcEndpointRouteBuilderExtensions.MapStreamQueryGrpc{TService}"/>.
/// </remarks>
public abstract class PulseGrpcStreamService<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PulseGrpcStreamService{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="mediator">The mediator used to execute the streaming query.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="mediator"/> is <see langword="null"/>.</exception>
    protected PulseGrpcStreamService([NotNull] IMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);

        Mediator = mediator;
    }

    /// <summary>
    /// Gets the mediator used to execute the streaming query.
    /// </summary>
    protected IMediator Mediator { get; }

    /// <summary>
    /// Executes <paramref name="query"/> through <see cref="IMediator.StreamQueryAsync{TQuery, TResponse}"/> and writes
    /// every yielded item, in order, to <paramref name="responseStream"/>.
    /// </summary>
    /// <param name="query">The streaming query to execute.</param>
    /// <param name="responseStream">The gRPC response stream to write the items to.</param>
    /// <param name="context">
    /// The server call context; its <see cref="ServerCallContext.CancellationToken"/> stops the stream.
    /// </param>
    /// <returns>A task that completes once all items have been written.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="query"/>, <paramref name="responseStream"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the call is cancelled, for example by the client.</exception>
    [SuppressMessage(
        "Usage",
        "S8949:Pass the cancellation token",
        Justification = "IAsyncStreamWriter.WriteAsync(T, CancellationToken) throws NotSupportedException for writers that only implement WriteAsync(T); the token is checked before each write instead."
    )]
    protected async Task StreamAsync(
        [NotNull] TQuery query,
        [NotNull] IServerStreamWriter<TResponse> responseStream,
        [NotNull] ServerCallContext context
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        var cancellationToken = context.CancellationToken;

        await foreach (
            var item in Mediator
                .StreamQueryAsync<TQuery, TResponse>(query, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // Handlers may ignore the token, so check it before every write.
            cancellationToken.ThrowIfCancellationRequested();

            await responseStream.WriteAsync(item).ConfigureAwait(false);
        }
    }
}
