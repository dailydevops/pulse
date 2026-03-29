namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Request interceptor that enforces idempotency for commands implementing
/// <see cref="IIdempotentCommand{TResponse}"/> by checking and updating an
/// <see cref="IIdempotencyStore"/> before and after handler execution.
/// </summary>
/// <typeparam name="TRequest">The type of request being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>If the request does not implement <see cref="IIdempotentCommand{TResponse}"/>, the interceptor passes through without any store interaction.</description></item>
/// <item><description>If <see cref="IIdempotencyStore"/> is not registered in the DI container, the interceptor passes through without any store interaction.</description></item>
/// <item><description>If <see cref="IIdempotencyStore.ExistsAsync"/> returns <see langword="true"/>, an <see cref="IdempotencyConflictException"/> is thrown.</description></item>
/// <item><description>Otherwise, the handler is executed and the key is stored via <see cref="IIdempotencyStore.StoreAsync"/> after successful completion.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddIdempotency()</c> on the <see cref="IMediatorConfigurator"/> to register this interceptor.
/// </remarks>
/// <seealso cref="IIdempotentCommand{TResponse}"/>
/// <seealso cref="IIdempotencyStore"/>
/// <seealso cref="IdempotencyConflictException"/>
internal sealed class IdempotencyCommandInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyCommandInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve <see cref="IIdempotencyStore"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public IdempotencyCommandInterceptor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (request is not IIdempotentCommand<TResponse> idempotentCommand)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        var store = _serviceProvider.GetService<IIdempotencyStore>();
        if (store is null)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        var key = idempotentCommand.IdempotencyKey;

        if (await store.ExistsAsync(key, cancellationToken).ConfigureAwait(false))
        {
            throw new IdempotencyConflictException(key);
        }

        var result = await handler(request, cancellationToken).ConfigureAwait(false);

        await store.StoreAsync(key, cancellationToken).ConfigureAwait(false);

        return result;
    }
}
