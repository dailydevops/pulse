namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Marks a void command as requiring exclusive (non-concurrent) execution within the same process.
/// This is a convenience interface equivalent to <see cref="IExclusiveCommand{TResponse}"/> with
/// <c>TResponse = <see cref="Void"/></c>.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Use this interface for commands that perform an action without returning data, and that operate
/// on shared mutable state and are not safe to run concurrently within the same process.
/// <para><strong>Scope:</strong></para>
/// Exclusivity is scoped to the current process. This does not provide distributed locking across
/// multiple instances or processes.
/// </remarks>
/// <example>
/// <code>
/// public record AdjustCreditCommand(string AccountId, decimal Amount) : IExclusiveCommand;
/// </code>
/// </example>
/// <seealso cref="IExclusiveCommand{TResponse}"/>
/// <seealso cref="ICommand"/>
public interface IExclusiveCommand : IExclusiveCommand<Void>, ICommand;
