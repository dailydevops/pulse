namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register the Pulse mediator and its dependencies.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Pulse mediator implementation and its required dependencies into the service collection.
    /// The mediator is registered as a scoped service, allowing for request-scoped handlers and interceptors.
    /// </summary>
    /// <param name="services">The service collection to add the mediator to.</param>
    /// <param name="builder">An optional configuration action for customizing mediator behavior such as adding interceptors.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para><strong>Registration Scope:</strong></para>
    /// The mediator and its handlers are registered as scoped services, meaning a new instance is created per scope (e.g., per HTTP request).
    /// This ensures thread-safety and proper lifetime management for request-scoped dependencies.
    /// <para><strong>⚠️ WARNING:</strong> Handlers and interceptors must also be registered in the service collection for the mediator to discover and execute them.
    /// The mediator will throw <see cref="InvalidOperationException"/> if no handler is found for a command or query.</para>
    /// <para><strong>Best Practices:</strong></para>
    /// <list type="bullet">
    /// <item><description>Register handlers in the same assembly or explicitly add them to the service collection</description></item>
    /// <item><description>Use the configurator to add cross-cutting concerns like metrics and tracing</description></item>
    /// <item><description>Consider using a convention-based registration approach for large codebases</description></item>
    /// </list>
    /// <para><strong>Performance Considerations:</strong></para>
    /// The scoped lifetime ensures minimal memory overhead while maintaining thread-safety.
    /// Event handlers are executed in parallel for optimal throughput.
    /// <para><strong>Additional Resources:</strong></para>
    /// <list type="bullet">
    /// <item><description>Mediator Pattern: https://en.wikipedia.org/wiki/Mediator_pattern</description></item>
    /// <item><description>CQRS Pattern: https://martinfowler.com/bliki/CQRS.html</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para><strong>Basic registration:</strong></para>
    /// <code>
    /// // In Program.cs or Startup.cs
    /// services.AddPulseMediator();
    ///
    /// // Register handlers
    /// services.AddScoped&lt;ICommandHandler&lt;CreateOrderCommand, OrderResult&gt;, CreateOrderCommandHandler&gt;();
    /// services.AddScoped&lt;IQueryHandler&lt;GetOrderQuery, Order&gt;, GetOrderQueryHandler&gt;();
    /// services.AddScoped&lt;IEventHandler&lt;OrderCreatedEvent&gt;, OrderCreatedEventHandler&gt;();
    /// </code>
    /// <para><strong>Advanced registration with configuration:</strong></para>
    /// <code>
    /// services.AddPulseMediator(config =>
    /// {
    ///     // Add OpenTelemetry tracing and Prometheus metrics
    ///     config.AddActivityAndMetrics();
    /// });
    /// </code>
    /// <para><strong>Using in a controller or service:</strong></para>
    /// <code>
    /// public class OrdersController : ControllerBase
    /// {
    ///     private readonly IMediator _mediator;
    ///
    ///     public OrdersController(IMediator mediator)
    ///     {
    ///         _mediator = mediator;
    ///     }
    ///
    ///     [HttpPost]
    ///     public async Task&lt;IActionResult&gt; CreateOrder([FromBody] CreateOrderCommand command)
    ///     {
    ///         var result = await _mediator.SendAsync&lt;CreateOrderCommand, OrderResult&gt;(command);
    ///         return Ok(result);
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="IMediator"/>
    /// <seealso cref="IMediatorConfigurator"/>
    /// <seealso cref="ICommandHandler{TCommand, TResponse}"/>
    /// <seealso cref="IQueryHandler{TQuery, TResponse}"/>
    /// <seealso cref="IEventHandler{TEvent}"/>
    public static IServiceCollection AddPulseMediator(
        this IServiceCollection services,
        Action<IMediatorConfigurator>? builder = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        if (builder is not null)
        {
            var mediatorBuilder = new MediatorConfigurator(services);
            builder.Invoke(mediatorBuilder);
        }

        return services.AddScoped<IMediator, PulseMediator>();
    }
}
