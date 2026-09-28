namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Request interceptor that records commands whose handler execution failed to an
/// <see cref="ICommandDeadLetterStore"/>, allowing operators to inspect and replay them later.
/// </summary>
/// <typeparam name="TRequest">The type of request being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>If the request does not implement <see cref="ICommand{TResponse}"/>, the interceptor passes through without any try/catch overhead - queries and other non-command requests are never intercepted.</description></item>
/// <item><description>If the handler completes successfully, its result is returned unchanged and no store interaction occurs.</description></item>
/// <item><description>If the handler throws, and <see cref="ICommandDeadLetterStore"/> is registered in the DI container, the command's serialized payload and the exception are recorded via <see cref="ICommandDeadLetterStore.StoreAsync"/> before the original exception is rethrown.</description></item>
/// <item><description>If <see cref="ICommandDeadLetterStore"/> is not registered, the interceptor is a no-op on failure - the original exception is still rethrown unchanged.</description></item>
/// <item><description>If the failed command is the one <see cref="CommandDeadLetterReplayDispatcher"/> is replaying, no new entry is stored - <see cref="ICommandDeadLetterManagement.ReplayAsync"/> records the failure on the replayed entry instead. Other commands sent by the replayed handler are still recorded.</description></item>
/// <item><description>If serializing the payload or <see cref="ICommandDeadLetterStore.StoreAsync"/> throws, that recording failure is logged at <see cref="LogLevel.Error"/> and swallowed, so it never replaces the original exception. No entry is recorded in that case.</description></item>
/// <item><description>The original exception is always rethrown with its stack trace, whether or not a store is registered and whether or not recording succeeds - this interceptor never swallows a command failure, it only optionally records it first.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddCommandDeadLetter()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="ICommandDeadLetterStore"/>
/// <seealso cref="IPayloadSerializer"/>
internal sealed partial class CommandDeadLetterInterceptor<TRequest, TResponse>
    : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPayloadSerializer _payloadSerializer;
    private readonly ILogger<CommandDeadLetterInterceptor<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the optional <see cref="ICommandDeadLetterStore"/>.</param>
    /// <param name="payloadSerializer">The serializer used to serialize the failed command's payload.</param>
    /// <param name="logger">The logger used to report a failure while recording a dead letter.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/>, <paramref name="payloadSerializer"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public CommandDeadLetterInterceptor(
        IServiceProvider serviceProvider,
        IPayloadSerializer payloadSerializer,
        ILogger<CommandDeadLetterInterceptor<TRequest, TResponse>> logger
    )
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceProvider = serviceProvider;
        _payloadSerializer = payloadSerializer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(handler);

        if (request is not ICommand<TResponse>)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed replay is recorded on the replayed entry by ICommandDeadLetterManagement.ReplayAsync.
            var store = CommandDeadLetterReplayDispatcher.IsReplayedCommand(request)
                ? null
                : _serviceProvider.GetService<ICommandDeadLetterStore>();
            if (store is not null)
            {
                try
                {
                    var payload = _payloadSerializer.Serialize(request);
                    await store
                        .StoreAsync(typeof(TRequest).AssemblyQualifiedName!, payload, ex, cancellationToken)
                        .ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A recording failure must never replace the original command exception.
                catch (Exception recordingException)
#pragma warning restore CA1031
                {
                    LogRecordingFailed(_logger, recordingException, typeof(TRequest).FullName);
                }
            }

            throw;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to record failed command '{CommandType}' as dead letter; the original exception is rethrown."
    )]
    private static partial void LogRecordingFailed(ILogger logger, Exception exception, string? commandType);
}
