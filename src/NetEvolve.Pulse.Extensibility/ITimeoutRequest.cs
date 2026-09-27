namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Marker interface for requests that enforce a per-request deadline using a <see cref="CancellationTokenSource"/>.
/// Implement this interface alongside <see cref="ICommand{TResponse}"/> or <see cref="IQuery{TResponse}"/> to opt in to
/// built-in timeout enforcement without any external dependencies.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// When a request implements <see cref="ITimeoutRequest"/>, the <c>TimeoutRequestInterceptor</c>
/// will create a linked <see cref="CancellationTokenSource"/> using the effective timeout.
/// If the handler does not complete within that deadline, a <see cref="TimeoutException"/> is thrown.
/// <para><strong>Timeout Resolution:</strong></para>
/// <list type="number">
/// <item><description>If <see cref="Timeout"/> is non-<see langword="null"/>, that value is used as the deadline.</description></item>
/// <item><description>If <see cref="Timeout"/> is <see langword="null"/>, the globally configured fallback timeout is used (if set).</description></item>
/// <item><description>If neither is set, or the effective value is <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, the interceptor is a transparent pass-through.</description></item>
/// </list>
/// Requests that do not implement <see cref="ITimeoutRequest"/> are always passed through without any timeout.
/// <para><strong>Distinguishing Timeout from User Cancellation:</strong></para>
/// The interceptor correctly distinguishes between a timeout-triggered cancellation and a caller-initiated
/// cancellation, re-throwing a <see cref="TimeoutException"/> only in the former case.
/// <para><strong>Late Completion and Side Effects:</strong></para>
/// The deadline is also enforced after the handler returns: a result that is produced after the deadline
/// (for example because the handler ignores the cancellation token or the deadline callback is delayed under
/// thread-pool starvation) is discarded and a <see cref="TimeoutException"/> is thrown instead. This applies to
/// commands and queries alike. A command handler that finished late may already have performed its work
/// (database writes, published events, external calls), so any retry policy that reacts to the
/// <see cref="TimeoutException"/> must be idempotent.
/// </remarks>
/// <example>
/// <code>
/// // Explicit per-request timeout
/// public record ProcessOrderCommand(string OrderId) : ICommand&lt;OrderResult&gt;, ITimeoutRequest
/// {
///     public string? CorrelationId { get; set; }
///     public string? CausationId { get; set; }
///     public TimeSpan? Timeout =&gt; TimeSpan.FromSeconds(10);
/// }
///
/// // Defer to the global fallback configured via AddRequestTimeout(globalTimeout: ...)
/// public record GetStatusQuery(string Id) : IQuery&lt;Status&gt;, ITimeoutRequest
/// {
///     public string? CorrelationId { get; set; }
///     public string? CausationId { get; set; }
///     public TimeSpan? Timeout =&gt; null;
/// }
/// </code>
/// </example>
/// <seealso cref="IRequest{TResponse}"/>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="IQuery{TResponse}"/>
public interface ITimeoutRequest
{
    /// <summary>
    /// Gets the maximum allowed duration for the handler to complete before a
    /// <see cref="TimeoutException"/> is raised.
    /// When <see langword="null"/>, the globally configured fallback timeout is applied if set;
    /// otherwise the interceptor is a transparent pass-through for this request.
    /// </summary>
    TimeSpan? Timeout { get; }
}
