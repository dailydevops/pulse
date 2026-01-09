namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a command that performs an action without returning a response value.
/// Commands are used to modify state or trigger actions in the system.
/// </summary>
/// <remarks>
/// This is a specialized version of <see cref="ICommand{TResponse}"/> that returns <see cref="Void"/>.
/// Use for operations like deletes or notifications that don't return data.
/// </remarks>
/// <example>
/// <code>
/// public record SendEmailCommand(string To, string Subject, string Body) : ICommand;
///
/// public class SendEmailCommandHandler : ICommandHandler&lt;SendEmailCommand, Void&gt;
/// {
///     private readonly IEmailService _emailService;
///
///     public async Task&lt;Void&gt; HandleAsync(SendEmailCommand command, CancellationToken cancellationToken)
///     {
///         await _emailService.SendAsync(command.To, command.Subject, command.Body, cancellationToken);
///         return Void.Completed;
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="Void"/>
public interface ICommand : ICommand<Void>;
