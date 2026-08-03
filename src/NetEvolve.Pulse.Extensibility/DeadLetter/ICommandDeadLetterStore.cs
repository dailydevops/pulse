namespace NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Defines the contract for persisting commands that failed processing to the dead letter store.
/// </summary>
/// <remarks>
/// <para><strong>Implementation Guidelines:</strong></para>
/// Implementations MUST persist a new <see cref="CommandDeadLetterEntry"/> with the following values:
/// <list type="bullet">
/// <item><description><see cref="CommandDeadLetterEntry.Id"/>: a freshly generated <see cref="Guid"/>.</description></item>
/// <item><description><see cref="CommandDeadLetterEntry.Status"/>: <see cref="CommandDeadLetterStatus.New"/>.</description></item>
/// <item><description><see cref="CommandDeadLetterEntry.OccurredAt"/>: the current timestamp.</description></item>
/// <item><description><see cref="CommandDeadLetterEntry.AttemptCount"/>: <c>1</c>.</description></item>
/// <item><description><see cref="CommandDeadLetterEntry.ExceptionType"/>: <c>exception.GetType().AssemblyQualifiedName</c>.</description></item>
/// <item><description><see cref="CommandDeadLetterEntry.ExceptionMessage"/>: <c>exception.Message</c>.</description></item>
/// </list>
/// </remarks>
public interface ICommandDeadLetterStore
{
    /// <summary>
    /// Stores a command that failed processing as a new dead letter entry.
    /// </summary>
    /// <param name="commandType">The assembly-qualified type name of the command that failed processing.</param>
    /// <param name="payload">The JSON serialized command payload.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StoreAsync(
        string commandType,
        string payload,
        Exception exception,
        CancellationToken cancellationToken = default
    );
}
