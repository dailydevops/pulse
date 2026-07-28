namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using Polly;

/// <summary>
/// Stream query interceptor that applies Polly resilience policies to the entire stream execution.
/// Integrates Polly v8 <see cref="ResiliencePipeline"/> with the Pulse mediator interceptor pipeline,
/// enabling retry, circuit breaker, timeout, and bulkhead strategies for stream queries.
/// </summary>
/// <typeparam name="TQuery">The type of streaming query to intercept, which must implement <see cref="IStreamQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
/// <remarks>
/// <para><strong>Execution Model:</strong></para>
/// The handler is enumerated <em>inside</em> the Polly pipeline, so resilience strategies observe
/// failures thrown at any point during enumeration — including deferred async iterators whose body
/// only runs on the first <c>MoveNextAsync</c>. Items are forwarded to the consumer as they are produced.
/// <para><strong>Retry Semantics:</strong></para>
/// A retry restarts the enumeration from the beginning. Items already yielded to the consumer before
/// the failure are <em>not</em> withdrawn; after a retry, the restarted enumeration yields its items
/// in addition to those already observed. Configure retries only for handlers whose enumeration is
/// idempotent or fails before yielding items.
/// <para><strong>Timeout Semantics:</strong></para>
/// A timeout strategy limits the duration of the entire enumeration, not only the stream open phase.
/// <para><strong>Transparent Pass-Through:</strong></para>
/// If no <see cref="ResiliencePipeline"/> is registered for <typeparamref name="TQuery"/>
/// (either as a keyed or global service), the interceptor passes through transparently
/// without applying any resilience strategy.
/// <para><strong>Policy Types Supported:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Retry:</strong> Restart the enumeration when it throws</description></item>
/// <item><description><strong>Circuit Breaker:</strong> Block requests when the failure threshold is reached</description></item>
/// <item><description><strong>Timeout:</strong> Enforce maximum duration for the entire enumeration</description></item>
/// <item><description><strong>Bulkhead:</strong> Limit concurrent stream executions</description></item>
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
    /// Intercepts the streaming query, executing the handler enumeration inside the configured Polly pipeline.
    /// Items are forwarded to the consumer as they are produced, without buffering the whole stream.
    /// </summary>
    /// <param name="request">The streaming query to process.</param>
    /// <param name="handler">The delegate representing the next step in the interceptor chain.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An asynchronous sequence of result items.</returns>
    /// <remarks>
    /// If no <see cref="ResiliencePipeline"/> is registered for <typeparamref name="TQuery"/>,
    /// the interceptor delegates directly to <paramref name="handler"/> without wrapping.
    /// When a pipeline is configured, a retry restarts the enumeration from the beginning;
    /// items yielded before the failure are not withdrawn.
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

        return IterateAsync(_pipeline, request, handler, cancellationToken);
    }

    private static async IAsyncEnumerable<TResponse> IterateAsync(
        ResiliencePipeline pipeline,
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        // The handler is enumerated inside the pipeline so that resilience strategies observe
        // failures thrown at any point during enumeration. Items flow to the consumer through
        // a bounded channel, preserving backpressure.
        var channel = Channel.CreateBounded<TResponse>(
            new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true }
        );

        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = ProduceAsync(pipeline, request, handler, channel.Writer, linkedTokenSource.Token);

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            await linkedTokenSource.CancelAsync().ConfigureAwait(false);
            await producer.ConfigureAwait(false);
        }
    }

    private static async Task ProduceAsync(
        ResiliencePipeline pipeline,
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        ChannelWriter<TResponse> writer,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await pipeline
                .ExecuteAsync(
                    static async (state, token) =>
                    {
                        await foreach (
                            var item in state
                                .Handler(state.Request, token)
                                .WithCancellation(token)
                                .ConfigureAwait(false)
                        )
                        {
                            await state.Writer.WriteAsync(item, token).ConfigureAwait(false);
                        }
                    },
                    (Handler: handler, Request: request, Writer: writer),
                    cancellationToken
                )
                .ConfigureAwait(false);

            _ = writer.TryComplete();
        }
#pragma warning disable CA1031 // The failure is propagated to the consumer through the channel.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _ = writer.TryComplete(ex);
        }
    }
}
