namespace NetEvolve.Pulse.Extensibility;

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Extension methods for <see cref="IMediatorSendOnly"/> that provide batch command execution.
/// </summary>
public static class MediatorSendOnlyExtensions
{
    /// <summary>
    /// Executes all commands in the <paramref name="batch"/> sequentially via the mediator.
    /// </summary>
    /// <param name="mediator">The mediator used to send each command.</param>
    /// <param name="batch">The batch of commands to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous batch execution.</returns>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown if <paramref name="mediator"/> or <paramref name="batch"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Commands are executed sequentially in the order they were added to the batch.
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
    /// <seealso cref="CommandBatch"/>
    public static async Task SendBatchAsync(
        [NotNull] this IMediatorSendOnly mediator,
        [NotNull] CommandBatch batch,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var command in batch.Commands)
        {
            await command(mediator, cancellationToken).ConfigureAwait(false);
        }
    }
}
