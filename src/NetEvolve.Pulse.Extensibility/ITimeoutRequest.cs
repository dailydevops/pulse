namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Marker interface for requests that enforce a per-request deadline using a <see cref="System.Threading.CancellationTokenSource"/>.
/// Implement this interface alongside <see cref="ICommand{TResponse}"/> or <see cref="IQuery{TResponse}"/> to opt in to
/// built-in timeout enforcement without any external dependencies.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// When a request implements <see cref="ITimeoutRequest"/>, the <c>TimeoutRequestInterceptor</c>
/// will create a linked <see cref="System.Threading.CancellationTokenSource"/> using the effective timeout.
/// If the handler does not complete within that deadline, a <see cref="System.TimeoutException"/> is thrown.
/// <para><strong>Timeout Resolution:</strong></para>
/// <list type="number">
/// <item><description>If <see cref="Timeout"/> is non-<see langword="null"/>, that value is used as the deadline.</description></item>
/// <item><description>If <see cref="Timeout"/> is <see langword="null"/>, the globally configured fallback timeout is used (if set).</description></item>
/// <item><description>If neither is set, the interceptor is a transparent pass-through.</description></item>
/// </list>
/// Requests that do not implement <see cref="ITimeoutRequest"/> are always passed through without any timeout.
/// <para><strong>Distinguishing Timeout from User Cancellation:</strong></para>
/// The interceptor correctly distinguishes between a timeout-triggered cancellation and a caller-initiated
/// cancellation, re-throwing a <see cref="System.TimeoutException"/> only in the former case.
/// </remarks>
/// <example>
/// <code>
/// // Explicit per-request timeout
/// public record ProcessOrderCommand(string OrderId) : ICommand&lt;OrderResult&gt;, ITimeoutRequest
/// {
///     public string? CorrelationId { get; set; }
///     public TimeSpan? Timeout =&gt; TimeSpan.FromSeconds(10);
/// }
///
/// // Defer to the global fallback configured via AddRequestTimeout(globalTimeout: ...)
/// public record GetStatusQuery(string Id) : IQuery&lt;Status&gt;, ITimeoutRequest
/// {
///     public string? CorrelationId { get; set; }
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
    /// <see cref="System.TimeoutException"/> is raised.
    /// When <see langword="null"/>, the globally configured fallback timeout is applied if set;
    /// otherwise the interceptor is a transparent pass-through for this request.
    /// </summary>
    TimeSpan? Timeout { get; }
}
