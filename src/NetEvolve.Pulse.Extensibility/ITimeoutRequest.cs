namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Marker interface for requests that enforce a per-request deadline using a <see cref="System.Threading.CancellationTokenSource"/>.
/// Implement this interface alongside <see cref="ICommand{TResponse}"/> or <see cref="IQuery{TResponse}"/> to opt in to
/// built-in timeout enforcement without any external dependencies.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// When a request implements <see cref="ITimeoutRequest"/>, the <c>TimeoutRequestInterceptor</c>
/// will create a linked <see cref="System.Threading.CancellationTokenSource"/> using the value returned by <see cref="Timeout"/>.
/// If the handler does not complete within that deadline, a <see cref="System.TimeoutException"/> is thrown.
/// <para><strong>Precedence:</strong></para>
/// The per-request <see cref="Timeout"/> value takes precedence over any globally configured fallback timeout.
/// <para><strong>Distinguishing Timeout from User Cancellation:</strong></para>
/// The interceptor correctly distinguishes between a timeout-triggered cancellation and a caller-initiated
/// cancellation, re-throwing a <see cref="System.TimeoutException"/> only in the former case.
/// </remarks>
/// <example>
/// <code>
/// public record ProcessOrderCommand(string OrderId) : ICommand&lt;OrderResult&gt;, ITimeoutRequest
/// {
///     public string? CorrelationId { get; set; }
///     public TimeSpan Timeout =&gt; TimeSpan.FromSeconds(10);
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
    /// </summary>
    TimeSpan Timeout { get; }
}
