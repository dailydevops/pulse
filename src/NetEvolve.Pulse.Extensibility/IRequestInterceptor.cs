namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a base interceptor for requests of type <typeparamref name="TRequest"/> that produce responses of type <typeparamref name="TResponse"/>.
/// Request interceptors enable cross-cutting concerns to be applied to both commands and queries in a unified manner.
/// Common use cases include logging, validation, metrics collection, and exception handling.
/// Multiple interceptors can be registered and will be executed in reverse order of registration (last registered runs first).
/// </summary>
/// <typeparam name="TRequest">The type of request to intercept, which must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>/// <remarks>
/// <para><strong>Execution Pipeline:</strong></para>
/// Interceptors form a chain of responsibility where each interceptor can:
/// <list type="bullet">
/// <item><description>Execute code before the handler</description></item>
/// <item><description>Call the next handler in the chain</description></item>
/// <item><description>Execute code after the handler</description></item>
/// <item><description>Short-circuit the pipeline by not calling the handler</description></item>
/// <item><description>Transform the request or response</description></item>
/// </list>
/// <para><strong>⚠️ WARNING:</strong> Interceptors execute in reverse order of registration (LIFO - Last In, First Out).
/// The last registered interceptor runs first. This allows outer interceptors (e.g., logging) to wrap inner ones (e.g., validation).</para>
/// <para><strong>Common Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>Logging and auditing</description></item>
/// <item><description>Validation and authorization</description></item>
/// <item><description>Caching</description></item>
/// <item><description>Performance monitoring and metrics</description></item>
/// <item><description>Exception handling and retry logic</description></item>
/// <item><description>Request/response transformation</description></item>
/// </list>
/// <para><strong>Best Practices:</strong></para>
/// <list type="bullet">
/// <item><description>Keep interceptors focused on a single concern</description></item>
/// <item><description>Always call the handler delegate unless intentionally short-circuiting</description></item>
/// <item><description>Use try-finally for cleanup code to ensure it runs</description></item>
/// <item><description>Consider using typed interceptors (ICommandInterceptor, IQueryInterceptor) for specificity</description></item>
/// <item><description>Register generic interceptors carefully - they apply to all requests</description></item>
/// </list>
/// </remarks>
/// <example>
/// <para><strong>Logging interceptor:</strong></para>
/// <code>
/// public class LoggingInterceptor&lt;TRequest, TResponse&gt;
///     : IRequestInterceptor&lt;TRequest, TResponse&gt;
///     where TRequest : IRequest&lt;TResponse&gt;
/// {
///     private readonly ILogger&lt;LoggingInterceptor&lt;TRequest, TResponse&gt;&gt; _logger;
///
///     public LoggingInterceptor(ILogger&lt;LoggingInterceptor&lt;TRequest, TResponse&gt;&gt; logger)
///     {
///         _logger = logger;
///     }
///
///     public async Task&lt;TResponse&gt; HandleAsync(
///         TRequest request,
///         Func&lt;TRequest, Task&lt;TResponse&gt;&gt; handler,
///         CancellationToken cancellationToken = default)
///     {
///         var requestType = typeof(TRequest).Name;
///         _logger.LogInformation("Executing {RequestType}", requestType);
///
///         var stopwatch = Stopwatch.StartNew();
///         try
///         {
///             var response = await handler(request);
///             stopwatch.Stop();
///             _logger.LogInformation(
///                 "{RequestType} completed in {ElapsedMs}ms",
///                 requestType,
///                 stopwatch.ElapsedMilliseconds
///             );
///             return response;
///         }
///         catch (Exception ex)
///         {
///             stopwatch.Stop();
///             _logger.LogError(
///                 ex,
///                 "{RequestType} failed after {ElapsedMs}ms",
///                 requestType,
///                 stopwatch.ElapsedMilliseconds
///             );
///             throw;
///         }
///     }
/// }
/// </code>
/// <para><strong>Validation interceptor:</strong></para>
/// <code>
/// public class ValidationInterceptor&lt;TRequest, TResponse&gt;
///     : IRequestInterceptor&lt;TRequest, TResponse&gt;
///     where TRequest : IRequest&lt;TResponse&gt;
/// {
///     private readonly IValidator&lt;TRequest&gt; _validator;
///
///     public ValidationInterceptor(IValidator&lt;TRequest&gt; validator)
///     {
///         _validator = validator;
///     }
///
///     public async Task&lt;TResponse&gt; HandleAsync(
///         TRequest request,
///         Func&lt;TRequest, Task&lt;TResponse&gt;&gt; handler,
///         CancellationToken cancellationToken = default)
///     {
///         // Validate before calling handler
///         var validationResult = await _validator.ValidateAsync(request);
///         if (!validationResult.IsValid)
///         {
///             throw new ValidationException(validationResult.Errors);
///         }
///
///         // Continue pipeline
///         return await handler(request);
///     }
/// }
/// </code>
/// <para><strong>Retry interceptor:</strong></para>
/// <code>
/// public class RetryInterceptor&lt;TRequest, TResponse&gt;
///     : IRequestInterceptor&lt;TRequest, TResponse&gt;
///     where TRequest : IRequest&lt;TResponse&gt;
/// {
///     private readonly ILogger&lt;RetryInterceptor&lt;TRequest, TResponse&gt;&gt; _logger;
///     private const int MaxRetries = 3;
///
///     public RetryInterceptor(ILogger&lt;RetryInterceptor&lt;TRequest, TResponse&gt;&gt; logger)
///     {
///         _logger = logger;
///     }
///
///     public async Task&lt;TResponse&gt; HandleAsync(
///         TRequest request,
///         Func&lt;TRequest, Task&lt;TResponse&gt;&gt; handler,
///         CancellationToken cancellationToken = default)
///     {
///         for (int attempt = 1; attempt &lt;= MaxRetries; attempt++)
///         {
///             try
///             {
///                 return await handler(request);
///             }
///             catch (Exception ex) when (attempt &lt; MaxRetries)
///             {
///                 _logger.LogWarning(
///                     ex,
///                     "Attempt {Attempt} of {MaxRetries} failed for {RequestType}, retrying...",
///                     attempt,
///                     MaxRetries,
///                     typeof(TRequest).Name
///                 );
///                 await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
///             }
///         }
///
///         // Final attempt without catching
///         return await handler(request);
///     }
/// }
/// </code>
/// <para><strong>Register interceptors:</strong></para>
/// <code>
/// // Register interceptors (executed in reverse order)
/// services.AddScoped(typeof(IRequestInterceptor&lt;,&gt;), typeof(LoggingInterceptor&lt;,&gt;));
/// services.AddScoped(typeof(IRequestInterceptor&lt;,&gt;), typeof(ValidationInterceptor&lt;,&gt;));
/// services.AddScoped(typeof(IRequestInterceptor&lt;,&gt;), typeof(RetryInterceptor&lt;,&gt;));
///
/// // Execution order: RetryInterceptor -&gt; ValidationInterceptor -&gt; LoggingInterceptor -&gt; Handler
/// </code>
/// </example>
/// <seealso cref="ICommandInterceptor{TCommand, TResponse}" />
/// <seealso cref="IQueryInterceptor{TQuery, TResponse}" />
/// <seealso cref="IRequest{TResponse}" />
/// <seealso href="https://en.wikipedia.org/wiki/Chain-of-responsibility_pattern">Chain of Responsibility Pattern</seealso>
public interface IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Asynchronously intercepts the specified request, allowing pre- and post-processing around the handler invocation.
    /// The interceptor is responsible for calling the <paramref name="handler"/> delegate to continue the pipeline.
    /// Interceptors can short-circuit the pipeline by not calling the handler (e.g., for caching or validation failures).
    /// </summary>
    /// <param name="request">The request being processed.</param>
    /// <param name="handler">The next handler in the pipeline to invoke. Must be called to continue execution unless short-circuiting.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the request response.</returns>
    Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    );
}
