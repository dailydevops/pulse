namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a void or empty response type for commands that don't return meaningful data.
/// This type is used as a marker to indicate successful completion without a specific return value.
/// Similar to <see cref="Task"/> vs <see cref="Task{TResult}"/>.
/// </summary>
public readonly record struct Void
{
    /// <summary>
    /// Gets a completed <see cref="Void"/> instance representing successful operation completion.
    /// </summary>
    public static Void Completed => default;
}
