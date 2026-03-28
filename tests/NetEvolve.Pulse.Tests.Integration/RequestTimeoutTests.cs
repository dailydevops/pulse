namespace NetEvolve.Pulse.Tests.Integration;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

public sealed class RequestTimeoutTests
{
    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        return services;
    }

    [Test]
    public async Task SendAsync_WithTimeoutRequest_WhenCompletesWithinDeadline_ReturnsResult()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddRequestTimeout())
            .AddScoped<ICommandHandler<FastTimeoutCommand, string>, FastTimeoutCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new FastTimeoutCommand(TimeSpan.FromSeconds(5));
        var result = await mediator.SendAsync<FastTimeoutCommand, string>(command).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("fast-result");
    }

    [Test]
    public async Task SendAsync_WithTimeoutRequest_WhenExceedsDeadline_ThrowsTimeoutException()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddRequestTimeout())
            .AddScoped<ICommandHandler<SlowTimeoutCommand, string>, SlowTimeoutCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new SlowTimeoutCommand(TimeSpan.FromMilliseconds(50));

        _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await mediator.SendAsync<SlowTimeoutCommand, string>(command).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendAsync_WithTimeoutRequest_WhenOriginalTokenCancelled_ThrowsOperationCanceledException()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddRequestTimeout())
            .AddScoped<ICommandHandler<SlowTimeoutCommand, string>, SlowTimeoutCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var command = new SlowTimeoutCommand(TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await mediator.SendAsync<SlowTimeoutCommand, string>(command, cts.Token).ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsNotTypeOf<TimeoutException>();
    }

    [Test]
    public async Task SendAsync_WithGlobalTimeout_WhenNonTimeoutRequestCompletesWithinDeadline_ReturnsResult()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddRequestTimeout(TimeSpan.FromSeconds(5)))
            .AddScoped<ICommandHandler<PlainCommand, string>, PlainCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new PlainCommand();
        var result = await mediator.SendAsync<PlainCommand, string>(command).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("plain-result");
    }

    [Test]
    public async Task SendAsync_WithGlobalTimeout_WhenNonTimeoutRequestExceedsDeadline_ThrowsTimeoutException()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddRequestTimeout(TimeSpan.FromMilliseconds(50)))
            .AddScoped<ICommandHandler<SlowPlainCommand, string>, SlowPlainCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new SlowPlainCommand();

        _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await mediator.SendAsync<SlowPlainCommand, string>(command).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendAsync_WithNoTimeoutConfigured_ForNonTimeoutRequest_PassesThrough()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddRequestTimeout())
            .AddScoped<ICommandHandler<PlainCommand, string>, PlainCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new PlainCommand();
        var result = await mediator.SendAsync<PlainCommand, string>(command).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("plain-result");
    }

    private sealed record FastTimeoutCommand(TimeSpan Timeout) : ICommand<string>, ITimeoutRequest
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record SlowTimeoutCommand(TimeSpan Timeout) : ICommand<string>, ITimeoutRequest
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record PlainCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record SlowPlainCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class FastTimeoutCommandHandler : ICommandHandler<FastTimeoutCommand, string>
    {
        public Task<string> HandleAsync(FastTimeoutCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult("fast-result");
    }

    private sealed class SlowTimeoutCommandHandler : ICommandHandler<SlowTimeoutCommand, string>
    {
        public async Task<string> HandleAsync(SlowTimeoutCommand command, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            return "slow-result";
        }
    }

    private sealed class PlainCommandHandler : ICommandHandler<PlainCommand, string>
    {
        public Task<string> HandleAsync(PlainCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult("plain-result");
    }

    private sealed class SlowPlainCommandHandler : ICommandHandler<SlowPlainCommand, string>
    {
        public async Task<string> HandleAsync(SlowPlainCommand command, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            return "slow-plain-result";
        }
    }
}
