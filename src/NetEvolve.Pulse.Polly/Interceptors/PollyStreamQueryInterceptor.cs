namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using Polly;

/// <summary>
/// Stream query interceptor that applies Polly resilience policies to the stream initialization phase.
/// Integrates Polly v8 <see cref="ResiliencePipeline"/> with the Pulse mediator interceptor pipeline,
/// enabling retry, circuit breaker, timeout, and bulkhead strategies for stream query open operations.
/// </summary>
/// <typeparam name="TQuery">The type of streaming query to intercept, which must implement <see cref="IStreamQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
/// <remarks>
/// <para><strong>Execution Model:</strong></para>
/// This interceptor wraps the <em>handler invocation</em> (stream open) inside the Polly pipeline.
/// Item enumeration happens outside the pipeline, so per-item retry is intentionally out of scope.
/// <para><strong>Transparent Pass-Through:</strong></para>
/// If no <see cref="ResiliencePipeline"/> is registered for <typeparamref name="TQuery"/>
/// (either as a keyed or global service), the interceptor passes through transparently
/// without applying any resilience strategy.
/// <para><strong>Policy Types Supported:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Retry:</strong> Retry a handler that throws during stream initialization</description></item>
/// <item><description><strong>Circuit Breaker:</strong> Block requests when the failure threshold is reached</description></item>
/// <item><description><strong>Timeout:</strong> Enforce maximum wait time before the first item is obtained</description></item>
/// <item><description><strong>Bulkhead:</strong> Limit concurrent stream open operations</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// services.AddPulse(config => config
///     .AddStreamQueryHandler&lt;GetOrdersStreamQuery, OrderDto, GetOrdersStreamQueryHandler&gt;()
///     .AddPollyStreamQueryPolicies&lt;GetOrdersStreamQuery, OrderDto&gt;(pipeline => pipeline
///         .AddRetry(new RetryStrategyOptions
///         {
///             MaxRetryAttempts = 3,
///             Delay = TimeSpan.FromSeconds(1)
///         })));
/// </code>
/// </example>
internal sealed class PollyStreamQueryInterceptor<TQuery, TResponse> : IStreamQueryInterceptor<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    private readonly ResiliencePipeline? _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollyStreamQueryInterceptor{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the resilience pipeline.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public PollyStreamQueryInterceptor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        // Try to resolve keyed pipeline first (per-query), then fallback to global
        // If neither is registered, _pipeline remains null and the interceptor passes through
        _pipeline =
            serviceProvider.GetKeyedService<ResiliencePipeline>(typeof(TQuery))
            ?? serviceProvider.GetService<ResiliencePipeline>();
    }

    /// <summary>
    /// Intercepts the streaming query, wrapping the handler invocation in the configured Polly pipeline.
    /// Items are yielded directly after the pipeline executes without buffering.
    /// </summary>
    /// <param name="request">The streaming query to process.</param>
    /// <param name="handler">The delegate representing the next step in the interceptor chain.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An asynchronous sequence of result items.</returns>
    /// <remarks>
    /// If no <see cref="ResiliencePipeline"/> is registered for <typeparamref name="TQuery"/>,
    /// the interceptor delegates directly to <paramref name="handler"/> without wrapping.
    /// </remarks>
    public IAsyncEnumerable<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_pipeline is null)
        {
            return handler(request, cancellationToken);
        }

        return IterateAsync(request, handler, cancellationToken);
    }

    private async IAsyncEnumerable<TResponse> IterateAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        // Wrap the handler invocation (stream open) inside the pipeline.
        // This protects the stream initialization phase; items are yielded directly afterwards.
        var stream = await _pipeline!
            .ExecuteAsync(
                token => new ValueTask<IAsyncEnumerable<TResponse>>(handler(request, token)),
                cancellationToken
            )
            .ConfigureAwait(false);

        await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
