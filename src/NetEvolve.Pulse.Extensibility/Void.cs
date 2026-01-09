namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a void or empty response type for commands that don't return meaningful data.
/// This type is used as a marker to indicate successful completion without a specific return value.
/// Similar to <see cref="Task"/> vs <see cref="Task{TResult}"/>.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// In C#, methods must return a value if they are declared with a return type. The <see cref="Void"/> type
/// provides a unit type that represents "no meaningful value" while still satisfying type constraints.
/// <para><strong>Usage:</strong></para>
/// Use <see cref="Void"/> as the response type for commands that perform actions but don't need to return data.
/// Common examples include Delete operations, notifications, or fire-and-forget actions.
/// <para><strong>Design Pattern:</strong></para>
/// This is an implementation of the Unit type from functional programming, representing a type with exactly one value.
/// It's preferred over using <c>object</c>, <c>bool</c>, or nullable types for void returns.
/// <para><strong>Convenience Interface:</strong></para>
/// The <c>ICommand</c> interface uses <see cref="Void"/> as its response type, allowing you to write
/// <c>: ICommand</c> instead of <c>: ICommand&lt;Void&gt;</c> for cleaner syntax.
/// <para><strong>Performance:</strong></para>
/// <see cref="Void"/> is a readonly struct with zero size, resulting in no heap allocation or memory overhead.
/// It's as efficient as possible for representing "no value".
/// </remarks>
/// <example>
/// <code>
/// // Command without return value
/// public record DeleteProductCommand(string ProductId) : ICommand;
///
/// // Handler implementation
/// public class DeleteProductCommandHandler : ICommandHandler&lt;DeleteProductCommand, Void&gt;
/// {
///     private readonly IProductRepository _repository;
///
///     public DeleteProductCommandHandler(IProductRepository repository)
///     {
///         _repository = repository;
///     }
///
///     public async Task&lt;Void&gt; HandleAsync(
///         DeleteProductCommand command,
///         CancellationToken cancellationToken)
///     {
///         await _repository.DeleteAsync(command.ProductId, cancellationToken);
///
///         // Return Void.Completed to indicate success
///         return Void.Completed;
///     }
/// }
///
/// // Usage with mediator (simplified syntax)
/// var command = new DeleteProductCommand("PROD-123");
/// await mediator.SendAsync(command); // No return value to capture
///
/// // Or explicit syntax
/// Void result = await mediator.SendAsync&lt;DeleteProductCommand, Void&gt;(command);
/// </code>
/// <para><strong>Comparison with alternatives:</strong></para>
/// <code>
/// // Using Void (recommended)
/// public record NotifyUserCommand(string UserId) : ICommand;
/// public async Task&lt;Void&gt; HandleAsync(NotifyUserCommand cmd, CancellationToken ct)
/// {
///     await SendNotification(cmd.UserId);
///     return Void.Completed; // Clear intent
/// }
///
/// // Using bool (not recommended - unclear semantics)
/// public async Task&lt;bool&gt; HandleAsync(NotifyUserCommand cmd, CancellationToken ct)
/// {
///     await SendNotification(cmd.UserId);
///     return true; // What does true mean?
/// }
///
/// // Using object (not recommended - boxing, unclear intent)
/// public async Task&lt;object&gt; HandleAsync(NotifyUserCommand cmd, CancellationToken ct)
/// {
///     await SendNotification(cmd.UserId);
///     return null; // Nullable reference issues
/// }
/// </code>
/// </example>
public readonly record struct Void
{
    /// <summary>
    /// Gets a completed <see cref="Void" /> instance representing successful operation completion.
    /// </summary>
    /// <remarks>
    /// <para><strong>Usage:</strong></para>
    /// Return <see cref="Completed"/> from command handlers that don't produce meaningful data.
    /// This provides a consistent, readable way to indicate successful completion.
    /// <para><strong>NOTE:</strong> Since <see cref="Void"/> is a struct with no fields, all instances are equivalent.
    /// <see cref="Completed"/> is provided for readability and to make intent explicit.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public async Task&lt;Void&gt; HandleAsync(UpdateSettingsCommand cmd, CancellationToken ct)
    /// {
    ///     await _repository.UpdateSettingsAsync(cmd.Settings, ct);
    ///     return Void.Completed;
    /// }
    /// </code>
    /// </example>
    public static Void Completed => default;
}
