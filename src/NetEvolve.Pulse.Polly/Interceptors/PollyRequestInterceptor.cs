namespace NetEvolve.Pulse.Interceptors;

using System;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using Polly;

/// <summary>
/// Request interceptor that applies Polly resilience policies to command and query handlers.
/// Integrates Polly v8 ResiliencePipeline with the Pulse mediator interceptor pipeline,
/// enabling retry, circuit breaker, timeout, bulkhead, and fallback strategies.
/// </summary>
/// <typeparam name="TRequest">The type of request to intercept, which must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Execution Model:</strong></para>
/// This interceptor wraps the handler execution in a Polly resilience pipeline,
/// allowing policies to control retry behavior, timeouts, circuit breaking, and resource isolation.
/// <para><strong>Policy Types Supported:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Retry:</strong> Automatically retry failed operations with configurable backoff strategies</description></item>
/// <item><description><strong>Circuit Breaker:</strong> Prevent cascading failures by temporarily blocking requests when a threshold is reached</description></item>
/// <item><description><strong>Timeout:</strong> Enforce maximum execution time for operations</description></item>
/// <item><description><strong>Bulkhead:</strong> Limit concurrent executions to prevent resource exhaustion</description></item>
/// <item><description><strong>Fallback:</strong> Provide alternative response when primary handler fails</description></item>
/// </list>
/// <para><strong>Interceptor Ordering:</strong></para>
/// Due to LIFO execution order, policy interceptors registered last execute first.
/// Consider the desired execution order when registering multiple interceptors:
/// <code>
/// services.AddPulse(config => config
///     .AddCommandHandler&lt;CreateOrder, Result, CreateOrderHandler&gt;()
///     .AddPollyRequestPolicies&lt;CreateOrder, Result&gt;(...)  // Executes second
///     .AddActivityAndMetrics());                      // Executes first (outermost)
/// </code>
/// <para><strong>Best Practices:</strong></para>
/// <list type="bullet">
/// <item><description>Register pipelines with Singleton lifetime for optimal performance</description></item>
/// <item><description>Configure appropriate timeout values based on expected handler execution time</description></item>
/// <item><description>Use circuit breakers for external dependencies (databases, APIs)</description></item>
/// <item><description>Combine retry with exponential backoff for transient failures</description></item>
/// <item><description>Monitor Polly telemetry for policy effectiveness</description></item>
/// </list>
/// </remarks>
/// <example>
/// <para><strong>Basic usage with retry policy:</strong></para>
/// <code>
/// services.AddPulse(config => config
///     .AddCommandHandler&lt;CreateOrder, OrderResult, CreateOrderHandler&gt;()
///     .AddPollyRequestPolicies&lt;CreateOrder, OrderResult&gt;(pipeline => pipeline
///         .AddRetry(new RetryStrategyOptions
///         {
///             MaxRetryAttempts = 3,
///             Delay = TimeSpan.FromSeconds(1),
///             BackoffType = DelayBackoffType.Exponential
///         })));
/// </code>
/// <para><strong>Combined policies with circuit breaker and timeout:</strong></para>
/// <code>
/// services.AddPulse(config => config
///     .AddQueryHandler&lt;GetUserQuery, User, GetUserQueryHandler&gt;()
///     .AddPollyRequestPolicies&lt;GetUserQuery, User&gt;(pipeline => pipeline
///         .AddTimeout(TimeSpan.FromSeconds(30))
///         .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
///         .AddCircuitBreaker(new CircuitBreakerStrategyOptions
///         {
///             FailureRatio = 0.5,
///             MinimumThroughput = 10,
///             BreakDuration = TimeSpan.FromSeconds(30)
///         })));
/// </code>
/// </example>
public sealed class PollyRequestInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ResiliencePipeline<TResponse> _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollyRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve keyed or global pipeline.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no pipeline is registered for <typeparamref name="TRequest"/> or globally.</exception>
    public PollyRequestInterceptor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        // Try to resolve keyed pipeline first (per-request), then fallback to global
        _pipeline =
            serviceProvider.GetKeyedService<ResiliencePipeline<TResponse>>(typeof(TRequest))
            ?? serviceProvider.GetService<ResiliencePipeline<TResponse>>()
            ?? throw new InvalidOperationException(
                $"No ResiliencePipeline<{typeof(TResponse).Name}> registered for {typeof(TRequest).Name} or globally."
            );
    }

    /// <summary>
    /// Executes the request handler through the Polly resilience pipeline.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <param name="handler">The delegate that represents the next step in the interceptor chain.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, containing the response from the handler.</returns>
    /// <remarks>
    /// The handler execution is wrapped in the Polly pipeline, which applies configured policies
    /// such as retry, circuit breaker, timeout, and bulkhead. The pipeline may execute the handler
    /// multiple times (retry), prevent execution (circuit breaker), or throw timeout exceptions.
    /// </remarks>
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, Task<TResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return await _pipeline
            .ExecuteAsync(async _ => await handler(request).ConfigureAwait(false), CancellationToken.None)
            .ConfigureAwait(false);
    }
}
