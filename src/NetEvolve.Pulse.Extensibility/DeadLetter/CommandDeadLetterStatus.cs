namespace NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Represents the processing status of a command dead letter entry.
/// </summary>
public enum CommandDeadLetterStatus
{
    /// <summary>
    /// The entry is newly stored and awaiting operator action.
    /// </summary>
    New = 0,

    /// <summary>
    /// The entry's command is currently being replayed.
    /// </summary>
    Replaying = 1,

    /// <summary>
    /// The entry's command was successfully replayed and resolved.
    /// </summary>
    Resolved = 2,

    /// <summary>
    /// The entry was manually dismissed and will not be replayed.
    /// </summary>
    Dismissed = 3,
}
