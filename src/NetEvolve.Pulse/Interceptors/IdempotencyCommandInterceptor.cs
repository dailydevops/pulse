namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Request interceptor that enforces idempotency for commands implementing
/// <see cref="IIdempotentCommand{TResponse}"/> by reserving the idempotency key in an
/// <see cref="IIdempotencyStore"/> before handler execution.
/// </summary>
/// <typeparam name="TRequest">The type of request being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>If the request does not implement <see cref="IIdempotentCommand{TResponse}"/>, the interceptor passes through without any store interaction.</description></item>
/// <item><description>If <see cref="IIdempotencyStore"/> is not registered in the DI container, the interceptor passes through without any store interaction.</description></item>
/// <item><description>If <see cref="IIdempotencyStore.TryReserveAsync"/> returns <see langword="false"/> (the key is already present), an <see cref="IdempotencyConflictException"/> is thrown.</description></item>
/// <item><description>Otherwise, the key is reserved BEFORE the handler executes, so concurrent duplicate submissions are rejected while the handler is still running.</description></item>
/// </list>
/// <para><strong>Semantics:</strong></para>
/// Reservation before execution provides at-most-once semantics: a command whose handler fails
/// keeps its key reserved, and retries with the same key are rejected with
/// <see cref="IdempotencyConflictException"/>. Strict atomicity of the reservation itself depends on
/// the registered <see cref="IIdempotencyStore"/> implementation of
/// <see cref="IIdempotencyStore.TryReserveAsync"/>; the non-atomic default leaves a small window
/// between the existence check and the store operation.
/// <para><strong>Registration:</strong></para>
/// Use <c>AddIdempotency()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
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

        if (!await store.TryReserveAsync(key, cancellationToken).ConfigureAwait(false))
        {
            throw new IdempotencyConflictException(key);
        }

        return await handler(request, cancellationToken).ConfigureAwait(false);
    }
}
