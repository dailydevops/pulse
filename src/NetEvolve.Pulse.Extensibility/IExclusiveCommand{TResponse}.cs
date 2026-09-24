namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Marks a command as requiring exclusive (non-concurrent) execution within the same process.
/// When registered with <c>ConcurrentCommandGuardInterceptor</c>, only one instance of the
/// same command type runs at a time, enforced via a <see cref="SemaphoreSlim"/>(1,1).
/// </summary>
/// <typeparam name="TResponse">The type of response returned after executing the command.</typeparam>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Use this interface for commands that operate on shared mutable state and are not safe to run
/// concurrently within the same process (e.g., counter increments, credit adjustments).
/// <para><strong>Scope:</strong></para>
/// Exclusivity is scoped to the current process. This does not provide distributed locking across
/// multiple instances or processes.
/// <para><strong>Performance:</strong></para>
/// Commands that do not implement this interface incur zero overhead from the guard interceptor.
/// </remarks>
/// <example>
/// <code>
/// public record IncrementCounterCommand(string CounterId) : IExclusiveCommand&lt;int&gt;;
/// </code>
/// </example>
/// <seealso cref="IExclusiveCommand"/>
/// <seealso cref="ICommand{TResponse}"/>
public interface IExclusiveCommand<TResponse> : ICommand<TResponse>;
