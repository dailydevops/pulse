namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines an interceptor for events of type <typeparamref name="TEvent"/>.
/// Event interceptors enable cross-cutting concerns such as logging, auditing, or enrichment to be applied before event handlers are invoked.
/// Multiple interceptors can be chained together to form a processing pipeline.
/// </summary>
/// <typeparam name="TEvent">The type of event to intercept, which must implement <see cref="IEvent"/>.</typeparam>
public interface IEventInterceptor<TEvent>
    where TEvent : IEvent
{
    /// <summary>
    /// Asynchronously intercepts the specified event, allowing pre- and post-processing around the handler invocation.
    /// The interceptor is responsible for calling the <paramref name="handler"/> delegate to continue the pipeline.
    /// </summary>
    /// <param name="message">The event being processed.</param>
    /// <param name="handler">The next handler in the pipeline to invoke. Must be called to continue execution.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(TEvent message, Func<TEvent, Task> handler);
}
