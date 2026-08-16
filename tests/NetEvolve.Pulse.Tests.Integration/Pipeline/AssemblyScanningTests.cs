namespace NetEvolve.Pulse.Tests.Integration.Pipeline;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;

/// <summary>
/// End-to-end integration tests for the reflection-based assembly-scanning registration methods on
/// <see cref="AssemblyScanningExtensions"/>. Only <c>AddHandlersFromAssemblyContaining</c> (via a
/// different test file) was previously exercised — the remaining public entry points
/// (<c>AddHandlersFromAssembly</c>, <c>AddHandlersFromAssemblies</c>, and the
/// <c>AddInterceptorsFrom*</c> family) had never been called through a real host. The
/// <c>*FromCallingAssembly</c>/<c>*FromEntryAssembly</c>/<c>*FromExecutingAssembly</c> overloads are
/// marked <c>[ExcludeFromCodeCoverage]</c> in source and are intentionally not covered here.
/// </summary>
[TestGroup("Pipeline")]
public sealed class AssemblyScanningTests
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
    public async Task AddHandlersFromAssembly_Discovers_And_Invokes_Handler(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new List<string>();

        using var host = await BuildHostAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(config => config.AddHandlersFromAssembly(typeof(AssemblyScanningTests).Assembly)),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Confirm the scanned handler type was actually the one discovered and registered by
            // reflection, not merely a coincidentally-named handler.
            var handlers = scope.ServiceProvider.GetServices<IEventHandler<ScannedAssemblyEvent>>().ToArray();
            _ = await Assert.That(handlers).HasSingleItem();
            _ = await Assert.That(handlers[0]).IsTypeOf<ScannedAssemblyHandler>();

            await mediator.PublishAsync(new ScannedAssemblyEvent(), cancellationToken).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(tracker).IsEquivalentTo(["ScannedAssemblyHandler"]);
    }

    [Test]
    public void AddHandlersFromAssembly_WithNullAssembly_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = Assert.Throws<ArgumentNullException>(() => configurator.AddHandlersFromAssembly(null!));
    }

    [Test]
    public void AddHandlersFromAssemblies_WithNullArray_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = Assert.Throws<ArgumentNullException>(() => configurator.AddHandlersFromAssemblies(null!));
    }

    [Test]
    public async Task AddHandlersFromAssemblies_Discovers_Handlers_And_Skips_NullEntries(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new List<string>();

        using var host = await BuildHostAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(config =>
                            config.AddHandlersFromAssemblies([
                                typeof(AssemblyScanningTests).Assembly,
                                null!,
                                typeof(IMediator).Assembly,
                            ])
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            await mediator.PublishAsync(new ScannedAssemblyEvent(), cancellationToken).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(tracker).IsEquivalentTo(["ScannedAssemblyHandler"]);
    }

    [Test]
    public async Task AddHandlersFromExecutingAssembly_DoesNotThrow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await BuildHostAsync(
                services => services.AddPulse(config => config.AddHandlersFromExecutingAssembly()),
                cancellationToken
            )
            .ConfigureAwait(false);

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task AddInterceptorsFromAssemblyContaining_Discovers_And_Invokes_Interceptor(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new List<string>();

        using var host = await BuildHostAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(config =>
                            config
                                .AddQueryHandler<ScannedInterceptorQuery, string, ScannedInterceptorQueryHandler>()
                                .AddInterceptorsFromAssemblyContaining<AssemblyScanningTests>()
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Confirm the scanned interceptor type was actually the one discovered and registered by
            // reflection, not merely a coincidentally-named interceptor.
            var interceptors = scope
                .ServiceProvider.GetServices<IRequestInterceptor<ScannedInterceptorQuery, string>>()
                .ToArray();
            _ = await Assert.That(interceptors).HasSingleItem();
            _ = await Assert.That(interceptors[0]).IsTypeOf<ScannedInterceptor>();

            var result = await mediator
                .QueryAsync<ScannedInterceptorQuery, string>(new ScannedInterceptorQuery(), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("handled");
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(tracker).IsEquivalentTo(["ScannedInterceptor"]);
    }

    [Test]
    public void AddInterceptorsFromAssembly_WithNullAssembly_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = Assert.Throws<ArgumentNullException>(() => configurator.AddInterceptorsFromAssembly(null!));
    }

    [Test]
    public void AddInterceptorsFromAssemblies_WithNullArray_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = Assert.Throws<ArgumentNullException>(() => configurator.AddInterceptorsFromAssemblies(null!));
    }

    [Test]
    public async Task AddInterceptorsFromAssemblies_Discovers_Interceptor_And_Skips_NullEntries(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new List<string>();

        using var host = await BuildHostAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(config =>
                            config
                                .AddQueryHandler<ScannedInterceptorQuery, string, ScannedInterceptorQueryHandler>()
                                .AddInterceptorsFromAssemblies([
                                    typeof(AssemblyScanningTests).Assembly,
                                    null!,
                                    typeof(IMediator).Assembly,
                                ])
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var result = await mediator
                .QueryAsync<ScannedInterceptorQuery, string>(new ScannedInterceptorQuery(), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("handled");
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(tracker).IsEquivalentTo(["ScannedInterceptor"]);
    }

    [Test]
    public async Task AddInterceptorsFromExecutingAssembly_DoesNotThrow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await BuildHostAsync(
                services => services.AddPulse(config => config.AddInterceptorsFromExecutingAssembly()),
                cancellationToken
            )
            .ConfigureAwait(false);

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ScannedAssemblyEvent : IEvent
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class ScannedAssemblyHandler(List<string> tracker) : IEventHandler<ScannedAssemblyEvent>
    {
        public Task HandleAsync(ScannedAssemblyEvent message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            tracker.Add("ScannedAssemblyHandler");
            return Task.CompletedTask;
        }
    }

    private sealed record ScannedInterceptorQuery : IQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class ScannedInterceptorQueryHandler : IQueryHandler<ScannedInterceptorQuery, string>
    {
        public Task<string> HandleAsync(
            ScannedInterceptorQuery request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult("handled");
    }

    private sealed class ScannedInterceptor(List<string> tracker) : IRequestInterceptor<ScannedInterceptorQuery, string>
    {
        public async Task<string> HandleAsync(
            ScannedInterceptorQuery request,
            Func<ScannedInterceptorQuery, CancellationToken, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            tracker.Add("ScannedInterceptor");
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
