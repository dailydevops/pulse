namespace NetEvolve.Pulse.Extensibility;

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines a write-only subset of the mediator pattern, restricted to command dispatch and event publishing.
/// Use this interface in services that should only perform write operations and must never access read operations (queries or streaming queries).
/// </summary>
/// <remarks>
/// <para><strong>CQRS Enforcement:</strong></para>
/// In strict CQRS contexts, write-side services (background services, repositories, sagas) should have no access to read operations.
/// Injecting <see cref="IMediatorSendOnly"/> instead of <see cref="IMediator"/> enforces this constraint at compile time.
/// <para><strong>Registration:</strong></para>
/// <see cref="IMediatorSendOnly"/> is automatically registered alongside <see cref="IMediator"/> when calling <c>services.AddPulse()</c>.
/// </remarks>
/// <example>
/// <code>
/// public class OrderBackgroundService : BackgroundService
/// {
///     private readonly IMediatorSendOnly _mediator;
///
///     public OrderBackgroundService(IMediatorSendOnly mediator)
///     {
///         _mediator = mediator;
///     }
///
///     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
///     {
///         var command = new ProcessOrderCommand(orderId: 42);
///         await _mediator.SendAsync(command, stoppingToken);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IMediator"/>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="IEvent"/>
public interface IMediatorSendOnly
{
    /// <summary>
    /// Asynchronously publishes an event to all registered handlers.
    /// All handlers execute in parallel, and exceptions in individual handlers don't prevent others from executing.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to publish.</typeparam>
    /// <param name="message">The event to publish.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// ⚠️ Event handlers should be idempotent. The mediator automatically sets <see cref="IEvent.PublishedAt"/>.
    /// </remarks>
    Task PublishAsync<TEvent>([NotNull] TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// Asynchronously sends a command for execution and returns the result.
    /// Commands are operations that change state or trigger side effects.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute.</typeparam>
    /// <typeparam name="TResponse">The type of response returned by the command.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The command result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the command type.</exception>
    /// <remarks>
    /// ⚠️ Exactly one handler must be registered for each command type.
    /// </remarks>
    Task<TResponse> SendAsync<TCommand, TResponse>(
        [NotNull] TCommand command,
        CancellationToken cancellationToken = default
    )
        where TCommand : ICommand<TResponse>;

    /// <summary>
    /// Asynchronously sends a command for execution without a return value.
    /// Convenience method for commands that return <see cref="Void"/>.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the command type.</exception>
    /// <remarks>
    /// This default implementation forwards the call to <see cref="SendAsync{TCommand, TResponse}"/> with <see cref="Void"/> as the response type.
    /// </remarks>
    Task SendAsync<TCommand>([NotNull] TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand => SendAsync<TCommand, Void>(command, cancellationToken);
}
