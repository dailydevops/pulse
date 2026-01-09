namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a base request that produces a response of type <typeparamref name="TResponse"/>.
/// This is the root interface for both commands and queries in the mediator pattern.
/// Typically, you should implement <see cref="ICommand{TResponse}"/> or <see cref="IQuery{TResponse}"/> instead of this interface directly.
/// </summary>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>/// <remarks>
/// <para><strong>Design Pattern:</strong></para>
/// This interface serves as the base abstraction for the request/response pattern in the mediator.
/// It enables generic interceptors and handlers to work with both commands and queries uniformly.
/// <para><strong>⚠️ WARNING:</strong> Don't implement this interface directly for your requests. Instead, use the more
/// specific <see cref="ICommand{TResponse}"/> or <see cref="IQuery{TResponse}"/> interfaces to indicate intent.</para>
/// <para><strong>Use Cases for Direct Implementation:</strong></para>
/// Direct implementation is only appropriate when:
/// <list type="bullet">
/// <item><description>Creating custom request types beyond commands and queries</description></item>
/// <item><description>Building framework extensions that need to work with all request types</description></item>
/// <item><description>Implementing generic interceptors that apply to both commands and queries</description></item>
/// </list>
/// <para><strong>CQRS Separation:</strong></para>
/// The separation into <see cref="ICommand{TResponse}"/> and <see cref="IQuery{TResponse}"/> provides better semantics
/// and enables type-specific interceptors and policies.
/// </remarks>
/// <example>
/// <para><strong>Correct usage (use specific interfaces):</strong></para>
/// <code>
/// // For state-changing operations
/// public record CreateUserCommand(string Name, string Email) : ICommand&lt;string&gt;;
///
/// // For read-only operations
/// public record GetUserQuery(string UserId) : IQuery&lt;UserDto&gt;;
/// </code>
/// <para><strong>Generic interceptor example (uses IRequest):</strong></para>
/// <code>
/// // An interceptor that works with all request types
/// public class PerformanceMonitorInterceptor&lt;TRequest, TResponse&gt;
///     : IRequestInterceptor&lt;TRequest, TResponse&gt;
///     where TRequest : IRequest&lt;TResponse&gt;
/// {
///     private readonly IPerformanceMonitor _monitor;
///
///     public PerformanceMonitorInterceptor(IPerformanceMonitor monitor)
///     {
///         _monitor = monitor;
///     }
///
///     public async Task&lt;TResponse&gt; HandleAsync(
///         TRequest request,
///         Func&lt;TRequest, Task&lt;TResponse&gt;&gt; handler)
///     {
///         using var measure = _monitor.Measure(typeof(TRequest).Name);
///         return await handler(request);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="ICommand{TResponse}" />
/// <seealso cref="IQuery{TResponse}" />
/// <seealso cref="IRequestInterceptor{TRequest, TResponse}" />
public interface IRequest<TResponse>;
