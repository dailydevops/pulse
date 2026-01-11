namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines an interceptor for events of type <typeparamref name="TEvent"/>.
/// Event interceptors enable cross-cutting concerns such as logging, auditing, or enrichment to be applied before event handlers are invoked.
/// Multiple interceptors can be chained together to form a processing pipeline.
/// </summary>
/// <typeparam name="TEvent">The type of event to intercept, which must implement <see cref="IEvent"/>.</typeparam>
/// /// <remarks>
/// <para><strong>Execution Model:</strong></para>
/// Event interceptors execute sequentially before the event handlers are invoked. Unlike request interceptors,
/// event interceptors work with a <c>Func&lt;TEvent, Task&gt;</c> handler delegate (void return) since events don't produce responses.
/// <para><strong>⚠️ WARNING:</strong> Event interceptors should be fast and non-blocking. Heavy processing should be avoided
/// as it delays all event handlers from executing. Consider async operations carefully.</para>
/// <para><strong>Common Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>Event logging and auditing</description></item>
/// <item><description>Setting metadata (timestamps, correlation IDs)</description></item>
/// <item><description>Event enrichment with additional context</description></item>
/// <item><description>Event filtering or routing</description></item>
/// <item><description>Metrics and monitoring</description></item>
/// </list>
/// <para><strong>Best Practices:</strong></para>
/// <list type="bullet">
/// <item><description>Keep interceptors lightweight</description></item>
/// <item><description>Always call the handler delegate unless intentionally filtering the event</description></item>
/// <item><description>Use try-catch to handle errors gracefully</description></item>
/// <item><description>Don't modify event state unless it's specifically for enrichment</description></item>
/// </list>
/// <para><strong>NOTE:</strong> The <see cref="IEvent.PublishedAt"/> property is automatically set by the mediator before
/// interceptors run, providing a reliable timestamp of when the event entered the system.</para>
/// </remarks>
/// <example>
/// <code>
/// // Logging interceptor for events
/// public class EventLoggingInterceptor&lt;TEvent&gt; : IEventInterceptor&lt;TEvent&gt;
///     where TEvent : IEvent
/// {
///     private readonly ILogger&lt;EventLoggingInterceptor&lt;TEvent&gt;&gt; _logger;
///
///     public EventLoggingInterceptor(ILogger&lt;EventLoggingInterceptor&lt;TEvent&gt;&gt; logger)
///     {
///         _logger = logger;
///     }
///
///     public async Task HandleAsync(TEvent message, Func&lt;TEvent, Task&gt; handler, CancellationToken cancellationToken = default)
///     {
///         _logger.LogInformation(
///             "Publishing event {EventType} with ID {EventId} at {PublishedAt}",
///             typeof(TEvent).Name,
///             message.Id,
///             message.PublishedAt
///         );
///
///         var stopwatch = Stopwatch.StartNew();
///         try
///         {
///             await handler(message);
///             stopwatch.Stop();
///             _logger.LogInformation(
///                 "Event {EventType} ({EventId}) processed in {ElapsedMs}ms",
///                 typeof(TEvent).Name,
///                 message.Id,
///                 stopwatch.ElapsedMilliseconds
///             );
///         }
///         catch (Exception ex)
///         {
///             stopwatch.Stop();
///             _logger.LogError(
///                 ex,
///                 "Error processing event {EventType} ({EventId}) after {ElapsedMs}ms",
///                 typeof(TEvent).Name,
///                 message.Id,
///                 stopwatch.ElapsedMilliseconds
///             );
///             throw;
///         }
///     }
/// }
///
/// // Audit interceptor for events
/// public class EventAuditInterceptor&lt;TEvent&gt; : IEventInterceptor&lt;TEvent&gt;
///     where TEvent : IEvent
/// {
///     private readonly IAuditService _auditService;
///     private readonly ICurrentUser _currentUser;
///
///     public EventAuditInterceptor(IAuditService auditService, ICurrentUser currentUser)
///     {
///         _auditService = auditService;
///         _currentUser = currentUser;
///     }
///
///     public async Task HandleAsync(TEvent message, Func&lt;TEvent, Task&gt; handler, CancellationToken cancellationToken = default)
///     {
///         // Create audit entry before handlers execute
///         var auditEntry = new AuditEntry
///         {
///             EventType = typeof(TEvent).Name,
///             EventId = message.Id,
///             PublishedAt = message.PublishedAt ?? DateTimeOffset.UtcNow,
///             UserId = _currentUser.Id,
///             EventData = JsonSerializer.Serialize(message)
///         };
///
///         await _auditService.RecordAsync(auditEntry);
///
///         // Continue to handlers
///         await handler(message);
///     }
/// }
///
/// // Correlation ID enrichment interceptor
/// public class CorrelationIdInterceptor&lt;TEvent&gt; : IEventInterceptor&lt;TEvent&gt;
///     where TEvent : IEvent, IHasCorrelationId
/// {
///     private readonly ICorrelationContext _correlationContext;
///
///     public CorrelationIdInterceptor(ICorrelationContext correlationContext)
///     {
///         _correlationContext = correlationContext;
///     }
///
///     public async Task HandleAsync(TEvent message, Func&lt;TEvent, Task&gt; handler, CancellationToken cancellationToken = default)
///     {
///         // Enrich event with correlation ID
///         if (string.IsNullOrEmpty(message.CorrelationId))
///         {
///             message.CorrelationId = _correlationContext.CorrelationId
///                 ?? Guid.NewGuid().ToString();
///         }
///
///         await handler(message);
///     }
/// }
///
/// // Register event interceptors
/// services.AddScoped(typeof(IEventInterceptor&lt;&gt;), typeof(EventLoggingInterceptor&lt;&gt;));
/// services.AddScoped(typeof(IEventInterceptor&lt;&gt;), typeof(EventAuditInterceptor&lt;&gt;));
/// </code>
/// </example>
/// <seealso cref="IEvent" />
/// <seealso cref="IEventHandler{TEvent}" />
/// <seealso cref="IMediator.PublishAsync{TEvent}" />
public interface IEventInterceptor<TEvent>
    where TEvent : IEvent
{
    /// <summary>
    /// Asynchronously intercepts the specified event, allowing pre- and post-processing around the handler invocation.
    /// The interceptor is responsible for calling the <paramref name="handler"/> delegate to continue the pipeline.
    /// </summary>
    /// <param name="message">The event being processed.</param>
    /// <param name="handler">The next handler in the pipeline to invoke. Must be called to continue execution.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(TEvent message, Func<TEvent, Task> handler, CancellationToken cancellationToken = default);
}
