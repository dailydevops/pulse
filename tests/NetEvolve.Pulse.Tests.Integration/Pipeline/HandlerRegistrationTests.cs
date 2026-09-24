namespace NetEvolve.Pulse.Tests.Integration.Pipeline;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// End-to-end integration tests for the generic custom-interceptor registration methods on
/// <see cref="HandlerRegistrationExtensions"/> (<c>AddRequestInterceptor</c>, <c>AddCommandInterceptor</c>,
/// <c>AddQueryInterceptor</c>, <c>AddEventInterceptor</c>, <c>AddStreamQueryInterceptor</c>). Unlike the
/// built-in cross-cutting features (logging, timeout, caching, ...), which register their interceptors
/// internally, these methods are the public API surface for registering user-authored interceptors and
/// were previously never exercised end-to-end.
/// </summary>
[TestGroup("Pipeline")]
public sealed class HandlerRegistrationTests
{
    private static async Task<IHost> BuildHostAsync(
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var host = new HostBuilder().ConfigureServices(configureServices).Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        return host;
    }

    [Test]
    public async Task AddRequestInterceptor_WrapsQueryHandling(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new List<string>();

        using var host = await BuildHostAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(builder =>
                            builder
                                .AddQueryHandler<MarkerQuery, string, MarkerQueryHandler>()
                                .AddRequestInterceptor<MarkerQuery, string, MarkerRequestInterceptor>()
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var result = await mediator
                .QueryAsync<MarkerQuery, string>(new MarkerQuery(), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("handled");
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(tracker).IsEquivalentTo(["RequestInterceptor:before", "RequestInterceptor:after"]);
    }

    [Test]
    public async Task AddCommandInterceptor_WrapsCommandHandling(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new List<string>();

        using var host = await BuildHostAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(builder =>
                            builder
                                .AddCommandHandler<MarkerCommand, string, MarkerCommandHandler>()
                                .AddCommandInterceptor<MarkerCommand, string, MarkerCommandInterceptor>()
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var result = await mediator
                .SendAsync<MarkerCommand, string>(new MarkerCommand(), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("handled");
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(tracker).IsEquivalentTo(["CommandInterceptor:before", "CommandInterceptor:after"]);
    }

    [Test]
    public async Task AddQueryInterceptor_WrapsQueryHandling(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new List<string>();

        using var host = await BuildHostAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(builder =>
                            builder
                                .AddQueryHandler<MarkerQuery, string, MarkerQueryHandler>()
                                .AddQueryInterceptor<MarkerQuery, string, MarkerQueryInterceptor>()
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var result = await mediator
                .QueryAsync<MarkerQuery, string>(new MarkerQuery(), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("handled");
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(tracker).IsEquivalentTo(["QueryInterceptor:before", "QueryInterceptor:after"]);
    }

    [Test]
    public async Task AddEventInterceptor_WrapsEventHandling(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new List<string>();

        using var host = await BuildHostAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(builder =>
                            builder
                                .AddEventHandler<MarkerEvent, MarkerEventHandler>()
                                .AddEventInterceptor<MarkerEvent, MarkerEventInterceptor>()
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            await mediator.PublishAsync(new MarkerEvent(), cancellationToken).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(tracker).IsEquivalentTo(["EventInterceptor:before", "handled", "EventInterceptor:after"]);
    }

    [Test]
    public async Task AddStreamQueryInterceptor_WrapsStreamQueryHandling(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new List<string>();

        using var host = await BuildHostAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(builder =>
                            builder
                                .AddStreamQueryHandler<MarkerStreamQuery, int, MarkerStreamQueryHandler>()
                                .AddStreamQueryInterceptor<MarkerStreamQuery, int, MarkerStreamQueryInterceptor>()
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var items = new List<int>();

            await foreach (
                var item in mediator.StreamQueryAsync<MarkerStreamQuery, int>(
                    new MarkerStreamQuery(),
                    cancellationToken
                )
            )
            {
                items.Add(item);
            }

            _ = await Assert.That(items).IsEquivalentTo([1, 2]);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert
            .That(tracker)
            .IsEquivalentTo(["StreamQueryInterceptor:before", "StreamQueryInterceptor:after"]);
    }

    private sealed record MarkerQuery : IQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class MarkerQueryHandler : IQueryHandler<MarkerQuery, string>
    {
        public Task<string> HandleAsync(MarkerQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult("handled");
    }

    private sealed class MarkerRequestInterceptor(List<string> tracker) : IRequestInterceptor<MarkerQuery, string>
    {
        public async Task<string> HandleAsync(
            MarkerQuery request,
            Func<MarkerQuery, CancellationToken, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            tracker.Add("RequestInterceptor:before");
            var result = await handler(request, cancellationToken).ConfigureAwait(false);
            tracker.Add("RequestInterceptor:after");
            return result;
        }
    }

    private sealed class MarkerQueryInterceptor(List<string> tracker) : IQueryInterceptor<MarkerQuery, string>
    {
        public async Task<string> HandleAsync(
            MarkerQuery request,
            Func<MarkerQuery, CancellationToken, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            tracker.Add("QueryInterceptor:before");
            var result = await handler(request, cancellationToken).ConfigureAwait(false);
            tracker.Add("QueryInterceptor:after");
            return result;
        }
    }

    private sealed record MarkerCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class MarkerCommandHandler : ICommandHandler<MarkerCommand, string>
    {
        public Task<string> HandleAsync(MarkerCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult("handled");
    }

    private sealed class MarkerCommandInterceptor(List<string> tracker) : ICommandInterceptor<MarkerCommand, string>
    {
        public async Task<string> HandleAsync(
            MarkerCommand request,
            Func<MarkerCommand, CancellationToken, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            tracker.Add("CommandInterceptor:before");
            var result = await handler(request, cancellationToken).ConfigureAwait(false);
            tracker.Add("CommandInterceptor:after");
            return result;
        }
    }

    private sealed record MarkerEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class MarkerEventHandler(List<string> tracker) : IEventHandler<MarkerEvent>
    {
        public Task HandleAsync(MarkerEvent message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            tracker.Add("handled");
            return Task.CompletedTask;
        }
    }

    private sealed class MarkerEventInterceptor(List<string> tracker) : IEventInterceptor<MarkerEvent>
    {
        public async Task HandleAsync(
            MarkerEvent message,
            Func<MarkerEvent, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            tracker.Add("EventInterceptor:before");
            await handler(message, cancellationToken).ConfigureAwait(false);
            tracker.Add("EventInterceptor:after");
        }
    }

    private sealed record MarkerStreamQuery : IStreamQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class MarkerStreamQueryHandler : IStreamQueryHandler<MarkerStreamQuery, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            MarkerStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return 1;
            await Task.Yield();
            yield return 2;
        }
    }

    private sealed class MarkerStreamQueryInterceptor(List<string> tracker)
        : IStreamQueryInterceptor<MarkerStreamQuery, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            MarkerStreamQuery request,
            Func<MarkerStreamQuery, CancellationToken, IAsyncEnumerable<int>> handler,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            tracker.Add("StreamQueryInterceptor:before");
            await foreach (var item in handler(request, cancellationToken).WithCancellation(cancellationToken))
            {
                yield return item;
            }
            tracker.Add("StreamQueryInterceptor:after");
        }
    }
}
