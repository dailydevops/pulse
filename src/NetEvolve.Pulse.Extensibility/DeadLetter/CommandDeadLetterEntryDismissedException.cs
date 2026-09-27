namespace NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Exception thrown by <see cref="ICommandDeadLetterManagement.ReplayAsync"/> when the requested dead letter entry
/// has been dismissed and therefore must not be replayed.
/// </summary>
/// <remarks>
/// <para><strong>When Is This Thrown:</strong></para>
/// <see cref="ICommandDeadLetterManagement.ReplayAsync"/> throws this exception when the entry identified by
/// <see cref="EntryId"/> has <see cref="CommandDeadLetterStatus.Dismissed"/> status. The check runs before the
/// status is changed, so the stored command is not dispatched and the entry stays dismissed.
/// <para><strong>Handling Recommendations:</strong></para>
/// Catch this type instead of <see cref="InvalidOperationException"/> to distinguish a dismissed entry from an
/// <see cref="InvalidOperationException"/> raised by the replayed command handler, for example to map only the
/// former to <c>409 Conflict</c>.
/// </remarks>
public sealed class CommandDeadLetterEntryDismissedException : InvalidOperationException
{
    /// <summary>
    /// Gets the identifier of the dismissed dead letter entry.
    /// </summary>
    public Guid EntryId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterEntryDismissedException"/> class
    /// with a default message.
    /// </summary>
    /// <remarks>This constructor exists to satisfy the standard exception pattern (CA1032). Prefer
    /// <see cref="CommandDeadLetterEntryDismissedException(Guid)"/> to preserve the entry identifier.</remarks>
    public CommandDeadLetterEntryDismissedException()
        : base("The command dead letter entry was dismissed and cannot be replayed.") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterEntryDismissedException"/> class
    /// for the dead letter entry identified by <paramref name="entryId"/>.
    /// </summary>
    /// <param name="entryId">The identifier of the dismissed dead letter entry.</param>
    public CommandDeadLetterEntryDismissedException(Guid entryId)
        : base($"CommandDeadLetterEntry '{entryId}' was dismissed and cannot be replayed.") => EntryId = entryId;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterEntryDismissedException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <remarks>This constructor exists to satisfy the standard exception pattern (CA1032). Prefer
    /// <see cref="CommandDeadLetterEntryDismissedException(Guid)"/> to preserve the entry identifier.</remarks>
    public CommandDeadLetterEntryDismissedException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterEntryDismissedException"/> class
    /// with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <remarks>This constructor exists to satisfy the standard exception pattern (CA1032). Prefer
    /// <see cref="CommandDeadLetterEntryDismissedException(Guid)"/> to preserve the entry identifier.</remarks>
    public CommandDeadLetterEntryDismissedException(string message, Exception innerException)
        : base(message, innerException) { }
}
