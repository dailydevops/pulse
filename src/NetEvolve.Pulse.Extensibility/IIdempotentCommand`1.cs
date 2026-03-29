namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Extends <see cref="ICommand{TResponse}"/> with an <see cref="IdempotencyKey"/> property,
/// marking a command as requiring idempotency enforcement by the mediator pipeline.
/// </summary>
/// <typeparam name="TResponse">The type of response returned after executing the command.</typeparam>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Implement this interface on commands that may be safely retried. When an
/// <c>IdempotencyCommandInterceptor</c> is registered, it checks the <see cref="IIdempotencyStore"/>
/// before delegating to the handler. If the key already exists, an
/// <c>IdempotencyConflictException</c> is thrown instead of re-executing the handler.
/// <para><strong>Key Guidelines:</strong></para>
/// <list type="bullet">
/// <item><description>The key SHOULD be a stable, client-generated value (e.g., a UUID supplied in the request header).</description></item>
/// <item><description>The key SHOULD be unique per logical operation, not per request attempt.</description></item>
/// <item><description>Reusing the same key for different operations WILL cause false-positive duplicate detection.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // The idempotency key must be a stable, client-supplied value — not generated per instance.
/// public record CreateOrderCommand(string CustomerId, decimal Amount, string IdempotencyKey) : IIdempotentCommand&lt;OrderResult&gt;;
/// </code>
/// </example>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="IIdempotencyStore"/>
public interface IIdempotentCommand<TResponse> : ICommand<TResponse>
{
    /// <summary>
    /// Gets the client-supplied idempotency key that uniquely identifies this logical operation.
    /// </summary>
    /// <remarks>
    /// The key MUST be non-<see langword="null"/> and non-empty.
    /// </remarks>
    string IdempotencyKey { get; }
}
