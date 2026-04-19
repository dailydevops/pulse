namespace NetEvolve.Pulse.Extensibility;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A fluent builder that captures a sequence of commands for batched sequential execution.
/// </summary>
/// <remarks>
/// <para><strong>Execution:</strong></para>
/// Commands are executed sequentially in the order they were added.
/// On the first exception, remaining commands are skipped and the exception propagates unchanged.
/// There is no rollback; compensation is the caller's responsibility.
/// </remarks>
/// <example>
/// <code>
/// var batch = new CommandBatch()
///     .Add(new CreateOrderCommand(items))
///     .Add(new ReserveInventoryCommand(items))
///     .Add(new SendConfirmationCommand(customerId));
///
/// await mediator.SendBatchAsync(batch, cancellationToken);
/// </code>
/// </example>
/// <seealso cref="MediatorSendOnlyExtensions.SendBatchAsync"/>
public sealed class CommandBatch
{
    private readonly List<Func<IMediatorSendOnly, CancellationToken, Task>> _commands = [];

    /// <summary>
    /// Gets the number of commands currently in the batch.
    /// </summary>
    public int Count => _commands.Count;

    /// <summary>
    /// Adds a command to the batch.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to add.</typeparam>
    /// <param name="command">The command instance to add.</param>
    /// <returns>The current <see cref="CommandBatch"/> instance to enable fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="command"/> is <see langword="null"/>.</exception>
    public CommandBatch Add<TCommand>([NotNull] TCommand command)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(command);

        _commands.Add((mediator, ct) => mediator.SendAsync(command, ct));

        return this;
    }

    internal IReadOnlyList<Func<IMediatorSendOnly, CancellationToken, Task>> Commands => _commands;
}
