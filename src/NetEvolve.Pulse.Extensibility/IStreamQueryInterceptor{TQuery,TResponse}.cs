namespace NetEvolve.Pulse.Extensibility;

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Defines an interceptor for streaming queries of type <typeparamref name="TQuery"/> that yield items of type <typeparamref name="TResponse"/>.
/// Streaming query interceptors allow cross-cutting concerns such as logging or authorization to be applied to streaming query execution.
/// Multiple interceptors can be chained together to form a pipeline.
/// </summary>
/// <typeparam name="TQuery">The type of streaming query to intercept, which must implement <see cref="IStreamQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// Streaming query interceptors wrap the streaming handler with pre- and post-processing logic.
/// The interceptor receives the streaming handler as a delegate and is responsible for calling it to continue the pipeline.
/// <para><strong>Common Use Cases for Streaming Query Interceptors:</strong></para>
/// <list type="bullet">
/// <item><description>Logging and auditing</description></item>
/// <item><description>Authorization checks before streaming begins</description></item>
/// <item><description>Performance monitoring</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class LoggingStreamQueryInterceptor&lt;TQuery, TResponse&gt;
///     : IStreamQueryInterceptor&lt;TQuery, TResponse&gt;
///     where TQuery : IStreamQuery&lt;TResponse&gt;
/// {
///     private readonly ILogger _logger;
///
///     public LoggingStreamQueryInterceptor(ILogger logger) => _logger = logger;
///
///     public async IAsyncEnumerable&lt;TResponse&gt; HandleAsync(
///         TQuery request,
///         Func&lt;TQuery, CancellationToken, IAsyncEnumerable&lt;TResponse&gt;&gt; handler,
///         [EnumeratorCancellation] CancellationToken cancellationToken)
///     {
///         _logger.LogInformation("Streaming {QueryType}", typeof(TQuery).Name);
///         await foreach (var item in handler(request, cancellationToken).WithCancellation(cancellationToken))
///         {
///             yield return item;
///         }
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IStreamQuery{TResponse}" />
/// <seealso cref="IStreamQueryHandler{TQuery, TResponse}" />
public interface IStreamQueryInterceptor<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    /// <summary>
    /// Asynchronously intercepts the specified streaming query, allowing pre- and post-processing around the handler invocation.
    /// The interceptor is responsible for iterating over the <paramref name="handler"/> delegate to continue the pipeline.
    /// </summary>
    /// <param name="request">The streaming query being processed.</param>
    /// <param name="handler">The next handler in the pipeline to invoke.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An asynchronous sequence of result items.</returns>
    IAsyncEnumerable<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        CancellationToken cancellationToken = default
    );
}
