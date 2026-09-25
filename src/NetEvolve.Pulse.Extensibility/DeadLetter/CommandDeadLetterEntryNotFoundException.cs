namespace NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Exception thrown by <see cref="ICommandDeadLetterManagement"/> implementations when no dead letter entry
/// exists for the requested identifier.
/// </summary>
/// <remarks>
/// <para><strong>When Is This Thrown:</strong></para>
/// <see cref="ICommandDeadLetterManagement.ReplayAsync"/> and <see cref="ICommandDeadLetterManagement.DismissAsync"/>
/// throw this exception when the entry identified by <see cref="EntryId"/> does not exist. A replay looks the entry up
/// before dispatching the stored command, so this exception is never thrown by the replayed command handler itself.
/// <para><strong>Handling Recommendations:</strong></para>
/// Catch this type instead of <see cref="KeyNotFoundException"/> to distinguish a missing entry from a
/// <see cref="KeyNotFoundException"/> raised by the replayed command handler, for example to map only the
/// former to <c>404 Not Found</c>. It derives from <see cref="KeyNotFoundException"/>, so existing handlers
/// for the base type keep working.
/// </remarks>
public sealed class CommandDeadLetterEntryNotFoundException : KeyNotFoundException
{
    /// <summary>
    /// Gets the identifier of the dead letter entry that was not found.
    /// </summary>
    public Guid EntryId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterEntryNotFoundException"/> class
    /// with a default message.
    /// </summary>
    /// <remarks>This constructor exists to satisfy the standard exception pattern (CA1032). Prefer
    /// <see cref="CommandDeadLetterEntryNotFoundException(Guid)"/> to preserve the entry identifier.</remarks>
    public CommandDeadLetterEntryNotFoundException()
        : base("The command dead letter entry was not found.") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterEntryNotFoundException"/> class
    /// for the dead letter entry identified by <paramref name="entryId"/>.
    /// </summary>
    /// <param name="entryId">The identifier of the dead letter entry that was not found.</param>
    public CommandDeadLetterEntryNotFoundException(Guid entryId)
        : base($"CommandDeadLetterEntry '{entryId}' was not found.") => EntryId = entryId;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterEntryNotFoundException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <remarks>This constructor exists to satisfy the standard exception pattern (CA1032). Prefer
    /// <see cref="CommandDeadLetterEntryNotFoundException(Guid)"/> to preserve the entry identifier.</remarks>
    public CommandDeadLetterEntryNotFoundException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDeadLetterEntryNotFoundException"/> class
    /// with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <remarks>This constructor exists to satisfy the standard exception pattern (CA1032). Prefer
    /// <see cref="CommandDeadLetterEntryNotFoundException(Guid)"/> to preserve the entry identifier.</remarks>
    public CommandDeadLetterEntryNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }
}
