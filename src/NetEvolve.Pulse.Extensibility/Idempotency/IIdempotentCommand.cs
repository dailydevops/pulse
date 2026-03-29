namespace NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// Extends <see cref="ICommand"/> (void response) with an <see cref="IIdempotentCommand{TResponse}.IdempotencyKey"/> property,
/// marking a void command as requiring idempotency enforcement by the mediator pipeline.
/// </summary>
/// <remarks>
/// This is a convenience interface equivalent to <see cref="IIdempotentCommand{TResponse}"/> with
/// <c>TResponse = <see cref="Void"/></c>. Use it for commands that perform an action without returning data.
/// </remarks>
/// <example>
/// <code>
/// // The idempotency key must be a stable, client-supplied value — not generated per instance.
/// public record SendEmailCommand(string To, string Subject, string Body, string IdempotencyKey) : IIdempotentCommand;
/// </code>
/// </example>
/// <seealso cref="ICommand"/>
/// <seealso cref="IIdempotentCommand{TResponse}"/>
/// <seealso cref="IIdempotencyStore"/>
public interface IIdempotentCommand : IIdempotentCommand<Void>, ICommand;
