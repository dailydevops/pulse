namespace NetEvolve.Pulse.Extensibility;

using System.Diagnostics.CodeAnalysis;

public interface IMediator
{
    Task PublishAsync<TEvent>([NotNull] TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    Task<TResponse> QueryAsync<TQuery, TResponse>([NotNull] TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResponse>;

    Task<TResponse> SendAsync<TCommand, TResponse>(
        [NotNull] TCommand command,
        CancellationToken cancellationToken = default
    )
        where TCommand : ICommand<TResponse>;

    Task SendAsync<TCommand>([NotNull] TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand => SendAsync<TCommand, Void>(command, cancellationToken);
}
