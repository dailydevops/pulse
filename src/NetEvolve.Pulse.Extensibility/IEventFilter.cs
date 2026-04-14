namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a filter that determines whether a specific event handler should process an event.
/// </summary>
/// <typeparam name="TEvent">The type of event to filter.</typeparam>
/// <remarks>
/// <para>
/// Filters allow conditional event handling without embedding <c>if</c>-statements inside every handler.
/// They are evaluated before a handler is invoked, keeping handlers pure and filters composable.
/// </para>
/// <para><strong>AND Semantics:</strong></para>
/// <list type="bullet">
/// <item><description>Multiple filters per event type: ALL must return <see langword="true"/> for the handler to execute.</description></item>
/// <item><description>If any filter returns <see langword="false"/>, the handler is skipped silently (no exception is thrown).</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public record OrderCreatedEvent : IEvent
/// {
///     public string Id { get; init; } = Guid.NewGuid().ToString();
///     public DateTimeOffset? PublishedAt { get; set; }
///     public bool IsHighValue { get; init; }
/// }
///
/// public class HighValueOrderFilter : IEventFilter&lt;OrderCreatedEvent&gt;
/// {
///     public ValueTask&lt;bool&gt; ShouldHandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken)
///         =&gt; ValueTask.FromResult(message.IsHighValue);
/// }
/// </code>
/// </example>
/// <seealso cref="IEvent" />
/// <seealso cref="IEventHandler{TEvent}" />
/// <seealso cref="IMediatorSendOnly.PublishAsync{TEvent}" />
public interface IEventFilter<TEvent>
    where TEvent : IEvent
{
    /// <summary>
    /// Asynchronously determines whether the associated event handler should process the specified event.
    /// </summary>
    /// <param name="message">The event to evaluate.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that resolves to <see langword="true"/> if the handler should process the event;
    /// otherwise, <see langword="false"/> to silently skip the handler.
    /// </returns>
    ValueTask<bool> ShouldHandleAsync(TEvent message, CancellationToken cancellationToken = default);
}
