namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a handler for processing events of type <typeparamref name="TEvent"/>.
/// Multiple handlers can be registered for the same event type and all execute in parallel.
/// </summary>
/// <typeparam name="TEvent">The type of event to handle.</typeparam>
/// <remarks>
/// ⚠️ Event handlers should be idempotent and handle failures gracefully.
/// Exceptions in one handler don't affect others.
/// </remarks>
/// <example>
/// <code>
/// public record UserRegisteredEvent : IEvent
/// {
///     public string Id { get; init; } = Guid.NewGuid().ToString();
///     public DateTimeOffset? PublishedAt { get; set; }
///     public string UserId { get; init; }
///     public string Email { get; init; }
/// }
///
/// public class UserRegisteredEmailHandler : IEventHandler&lt;UserRegisteredEvent&gt;
/// {
///     private readonly IEmailService _emailService;
///
///     public async Task HandleAsync(UserRegisteredEvent message, CancellationToken cancellationToken)
///     {
///         await _emailService.SendWelcomeEmailAsync(message.Email, cancellationToken);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IEvent" />
/// <seealso cref="IMediator.PublishAsync{TEvent}" />
public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    /// <summary>
    /// Asynchronously handles the specified event.
    /// </summary>
    /// <param name="message">The event to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(TEvent message, CancellationToken cancellationToken = default);
}
