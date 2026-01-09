namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a command that performs an action without returning a response value.
/// This interface is a specialized version of <see cref="ICommand{TResponse}"/> that returns <see cref="Void"/>.
/// Commands are used to modify state or trigger actions in the system.
/// </summary>
public interface ICommand : ICommand<Void>;
