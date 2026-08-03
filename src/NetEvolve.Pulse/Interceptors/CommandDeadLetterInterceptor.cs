namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
/// <item><description>The original exception is always rethrown, whether or not a store is registered - this interceptor never swallows a command failure, it only optionally records it first.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddCommandDeadLetter()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="ICommandDeadLetterStore"/>
/// <seealso cref="IPayloadSerializer"/>
internal sealed class CommandDeadLetterInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPayloadSerializer _payloadSerializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the optional <see cref="ICommandDeadLetterStore"/>.</param>
    /// <param name="payloadSerializer">The serializer used to serialize the failed command's payload.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> or <paramref name="payloadSerializer"/> is <see langword="null"/>.</exception>
    public CommandDeadLetterInterceptor(IServiceProvider serviceProvider, IPayloadSerializer payloadSerializer)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(payloadSerializer);

        _serviceProvider = serviceProvider;
        _payloadSerializer = payloadSerializer;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
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
            var store = _serviceProvider.GetService<ICommandDeadLetterStore>();
            if (store is not null)
            {
                var payload = _payloadSerializer.Serialize(request);
                await store
                    .StoreAsync(typeof(TRequest).AssemblyQualifiedName!, payload, ex, cancellationToken)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }
}
