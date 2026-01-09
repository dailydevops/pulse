namespace NetEvolve.Pulse.Internals;

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Pulse.Extensibility;

internal sealed partial class PulseMediator : IMediator
{
    private readonly ILogger<PulseMediator> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;

    public PulseMediator(ILogger<PulseMediator> logger, IServiceProvider serviceProvider, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _logger = logger;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
    }

    public Task PublishAsync<TEvent>([NotNull] TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        // Retrieve all handlers for the event type
        var handlers = _serviceProvider.GetServices<IEventHandler<TEvent>>();

        // If there are no handlers, simply return
        if (handlers?.Any() != true)
        {
            return Task.CompletedTask;
        }

        message.PublishedAt = _timeProvider.GetUtcNow();

        return ExecuteAsync(
            message,
            msg =>
                Parallel.ForEachAsync(
                    handlers,
                    cancellationToken,
                    async (handler, ct) =>
                    {
                        try
                        {
                            await handler.HandleAsync(msg, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            LogErrorPublish(message.Id, ex);
                        }
                    }
                )
        );
    }

    public Task<TResponse> QueryAsync<TQuery, TResponse>(
        [NotNull] TQuery query,
        CancellationToken cancellationToken = default
    )
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(query);

        // Resolve the appropriate handler for the query
        var handler = _serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();

        return ExecuteAsync(query, q => handler.HandleAsync(q, cancellationToken));
    }

    public Task<TResponse> SendAsync<TCommand, TResponse>(
        [NotNull] TCommand command,
        CancellationToken cancellationToken = default
    )
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(command);

        // Resolve the appropriate handler for the command
        var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();

        return ExecuteAsync(command, c => handler.HandleAsync(c, cancellationToken));
    }

    private Task ExecuteAsync<TEvent>(TEvent msg, Func<TEvent, Task> handler)
        where TEvent : IEvent
    {
        var interceptors = _serviceProvider.GetServices<IEventInterceptor<TEvent>>().Reverse().ToArray();
        if (interceptors.Length == 0)
        {
            return handler(msg);
        }

        var next = handler;

        foreach (var interceptor in interceptors)
        {
            var currentInterceptor = interceptor;
            var nextCopy = next;
            next = req => currentInterceptor.HandleAsync(req, nextCopy);
        }

        return next(msg);
    }

    private Task<TResponse> ExecuteAsync<TRequest, TResponse>(TRequest request, Func<TRequest, Task<TResponse>> handler)
        where TRequest : IRequest<TResponse>
    {
        var interceptors = _serviceProvider.GetServices<IRequestInterceptor<TRequest, TResponse>>().Reverse().ToArray();

        if (interceptors.Length == 0)
        {
            return handler(request);
        }

        var next = handler;

        foreach (var interceptor in interceptors)
        {
            var currentInterceptor = interceptor;
            var nextCopy = next;
            next = req => currentInterceptor.HandleAsync(req, nextCopy);
        }

        return next(request);
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = $"{nameof(PublishAsync)}: Unexpected error occurred. EventId: {{EventId}}",
        SkipEnabledCheck = true
    )]
    private partial void LogErrorPublish(string eventId, Exception exception);
}
