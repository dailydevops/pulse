namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines an interceptor for queries of type <typeparamref name="TQuery"/> that return responses of type <typeparamref name="TResponse"/>.
/// Query interceptors allow cross-cutting concerns such as caching, logging, or authorization to be applied to query execution.
/// Multiple interceptors can be chained together to form a pipeline.
/// </summary>
/// <typeparam name="TQuery">The type of query to intercept, which must implement <see cref="IQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the query.</typeparam>/// <remarks>
/// <para><strong>Usage:</strong></para>
/// This interface is a marker interface that extends <see cref="IRequestInterceptor{TRequest, TResponse}"/>.
/// It provides type-safe interceptor registration specifically for queries, enabling query-specific concerns.
/// <para><strong>Common Use Cases for Query Interceptors:</strong></para>
/// <list type="bullet">
/// <item><description>Response caching</description></item>
/// <item><description>Read authorization checks</description></item>
/// <item><description>Performance monitoring for slow queries</description></item>
/// <item><description>Data masking or filtering based on user context</description></item>
/// <item><description>Query result transformation</description></item>
/// </list>
/// <para><strong>⚠️ WARNING:</strong> Query interceptors should maintain the side-effect free nature of queries.
/// Don't modify state in query interceptors - only read, cache, or transform data.</para>
/// <para><strong>NOTE:</strong> For implementation details and examples, see <see cref="IRequestInterceptor{TRequest, TResponse}"/>.
/// This interface inherits all behavior from the base request interceptor.</para>
/// </remarks>
/// <example>
/// <code>
/// // Caching interceptor for queries
/// public class QueryCacheInterceptor&lt;TQuery, TResponse&gt;
///     : IQueryInterceptor&lt;TQuery, TResponse&gt;
///     where TQuery : IQuery&lt;TResponse&gt;
/// {
///     private readonly IDistributedCache _cache;
///     private readonly ILogger&lt;QueryCacheInterceptor&lt;TQuery, TResponse&gt;&gt; _logger;
///
///     public QueryCacheInterceptor(
///         IDistributedCache cache,
///         ILogger&lt;QueryCacheInterceptor&lt;TQuery, TResponse&gt;&gt; logger)
///     {
///         _cache = cache;
///         _logger = logger;
///     }
///
///     public async Task&lt;TResponse&gt; HandleAsync(
///         TQuery request,
///         Func&lt;TQuery, Task&lt;TResponse&gt;&gt; handler)
///     {
///         // Generate cache key from query
///         var cacheKey = $"{typeof(TQuery).Name}:{JsonSerializer.Serialize(request)}";
///
///         // Try to get from cache
///         var cachedData = await _cache.GetStringAsync(cacheKey);
///         if (cachedData != null)
///         {
///             _logger.LogDebug("Cache hit for {QueryType}", typeof(TQuery).Name);
///             return JsonSerializer.Deserialize&lt;TResponse&gt;(cachedData);
///         }
///
///         _logger.LogDebug("Cache miss for {QueryType}, executing query", typeof(TQuery).Name);
///
///         // Execute query
///         var response = await handler(request);
///
///         // Cache the result
///         var serializedResponse = JsonSerializer.Serialize(response);
///         await _cache.SetStringAsync(
///             cacheKey,
///             serializedResponse,
///             new DistributedCacheEntryOptions
///             {
///                 AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
///             }
///         );
///
///         return response;
///     }
/// }
///
/// // Authorization interceptor for queries
/// public class QueryAuthorizationInterceptor&lt;TQuery, TResponse&gt;
///     : IQueryInterceptor&lt;TQuery, TResponse&gt;
///     where TQuery : IQuery&lt;TResponse&gt;
/// {
///     private readonly ICurrentUser _currentUser;
///     private readonly IAuthorizationService _authService;
///
///     public QueryAuthorizationInterceptor(
///         ICurrentUser currentUser,
///         IAuthorizationService authService)
///     {
///         _currentUser = currentUser;
///         _authService = authService;
///     }
///
///     public async Task&lt;TResponse&gt; HandleAsync(
///         TQuery request,
///         Func&lt;TQuery, Task&lt;TResponse&gt;&gt; handler)
///     {
///         // Check if user has read permission
///         var authResult = await _authService.AuthorizeAsync(
///             _currentUser,
///             request,
///             "ReadPolicy"
///         );
///
///         if (!authResult.Succeeded)
///         {
///             throw new UnauthorizedAccessException(
///                 $"User does not have permission to execute {typeof(TQuery).Name}"
///             );
///         }
///
///         return await handler(request);
///     }
/// }
///
/// // Register query-specific interceptors
/// services.AddScoped(typeof(IQueryInterceptor&lt;,&gt;), typeof(QueryCacheInterceptor&lt;,&gt;));
/// services.AddScoped(typeof(IQueryInterceptor&lt;,&gt;), typeof(QueryAuthorizationInterceptor&lt;,&gt;));
/// </code>
/// </example>
/// <seealso cref="IQuery{TResponse}" />
/// <seealso cref="IQueryHandler{TQuery, TResponse}" />
/// <seealso cref="IRequestInterceptor{TRequest, TResponse}" />
public interface IQueryInterceptor<TQuery, TResponse> : IRequestInterceptor<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
