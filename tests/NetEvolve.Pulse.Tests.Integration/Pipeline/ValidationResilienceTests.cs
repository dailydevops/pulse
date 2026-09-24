namespace NetEvolve.Pulse.Tests.Integration.Pipeline;

using System;
using System.Threading;
using System.Threading.Tasks;
using global::FluentValidation;
using global::Polly;
using global::Polly.Retry;
using global::Polly.Timeout;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// End-to-end integration tests for the FluentValidation and Polly pipeline interceptors,
/// exercising real <see cref="AbstractValidator{T}"/> validators and real Polly
/// <see cref="ResiliencePipeline{TResult}"/> instances through a fully built host and the real mediator.
/// </summary>
[TestGroup("Pipeline")]
public sealed class ValidationResilienceTests
{
    private static async Task RunAndVerify(
        Func<IServiceProvider, CancellationToken, Task> testableCode,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = new HostBuilder()
            .ConfigureServices(services => configureServices(services))
            .ConfigureWebHost(webBuilder => _ = webBuilder.UseTestServer().Configure(applicationBuilder => { }))
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        using var server = host.GetTestServer();

        var scope = server.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await testableCode.Invoke(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task FluentValidation_InvalidCommand_ThrowsValidationException(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    _ = await Assert
                        .That(async () =>
                            await mediator
                                .SendAsync<CreateWidgetCommand, string>(new CreateWidgetCommand(""), token)
                                .ConfigureAwait(false)
                        )
                        .Throws<ValidationException>();
                },
                configureServices: services =>
                    services
                        .AddPulse(configurator =>
                            configurator
                                .AddCommandHandler<CreateWidgetCommand, string, CreateWidgetCommandHandler>()
                                .AddFluentValidation()
                        )
                        .AddScoped<IValidator<CreateWidgetCommand>, CreateWidgetCommandValidator>(),
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task FluentValidation_InvalidCommand_ContainsExpectedFailure(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    var exception = await Assert.ThrowsAsync<ValidationException>(async () =>
                        await mediator
                            .SendAsync<CreateWidgetCommand, string>(new CreateWidgetCommand(""), token)
                            .ConfigureAwait(false)
                    );

                    _ = await Assert.That(exception).IsNotNull();
                    _ = await Assert
                        .That(exception.Errors)
                        .Contains(failure => failure.PropertyName == nameof(CreateWidgetCommand.Name));
                },
                configureServices: services =>
                    services
                        .AddPulse(configurator =>
                            configurator
                                .AddCommandHandler<CreateWidgetCommand, string, CreateWidgetCommandHandler>()
                                .AddFluentValidation()
                        )
                        .AddScoped<IValidator<CreateWidgetCommand>, CreateWidgetCommandValidator>(),
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task FluentValidation_ValidCommand_Succeeds(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    var result = await mediator
                        .SendAsync<CreateWidgetCommand, string>(new CreateWidgetCommand("Widget-1"), token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(result).IsEqualTo("Created:Widget-1");
                },
                configureServices: services =>
                    services
                        .AddPulse(configurator =>
                            configurator
                                .AddCommandHandler<CreateWidgetCommand, string, CreateWidgetCommandHandler>()
                                .AddFluentValidation()
                        )
                        .AddScoped<IValidator<CreateWidgetCommand>, CreateWidgetCommandValidator>(),
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task FluentValidation_InvalidStreamQuery_ThrowsValidationException(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    _ = await Assert
                        .That(async () =>
                        {
                            await foreach (
                                var item in mediator.StreamQueryAsync<ListWidgetsQuery, string>(
                                    new ListWidgetsQuery(""),
                                    token
                                )
                            )
                            {
                                _ = item;
                            }
                        })
                        .Throws<ValidationException>();
                },
                configureServices: services =>
                    services
                        .AddPulse(configurator =>
                            configurator
                                .AddStreamQueryHandler<ListWidgetsQuery, string, ListWidgetsQueryHandler>()
                                .AddFluentValidation()
                        )
                        .AddScoped<IValidator<ListWidgetsQuery>, ListWidgetsQueryValidator>(),
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task FluentValidation_ValidStreamQuery_YieldsItems(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    var items = new List<string>();

                    await foreach (
                        var item in mediator.StreamQueryAsync<ListWidgetsQuery, string>(
                            new ListWidgetsQuery("Widget"),
                            token
                        )
                    )
                    {
                        items.Add(item);
                    }

                    _ = await Assert.That(items).IsEquivalentTo(["Widget-1", "Widget-2"]);
                },
                configureServices: services =>
                    services
                        .AddPulse(configurator =>
                            configurator
                                .AddStreamQueryHandler<ListWidgetsQuery, string, ListWidgetsQueryHandler>()
                                .AddFluentValidation()
                        )
                        .AddScoped<IValidator<ListWidgetsQuery>, ListWidgetsQueryValidator>(),
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Polly_Retry_SucceedsAfterTransientFailures(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler = new FlakyCommandHandler(failuresBeforeSuccess: 2);

        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    var result = await mediator
                        .SendAsync<FlakyCommand, string>(new FlakyCommand(), token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(result).IsEqualTo("success");
                    _ = await Assert.That(handler.AttemptCount).IsEqualTo(3);
                },
                configureServices: services =>
                    services
                        .AddSingleton<ICommandHandler<FlakyCommand, string>>(handler)
                        .AddPulse(configurator =>
                            configurator.AddPollyCommandPolicies<FlakyCommand, string>(pipeline =>
                                pipeline.AddRetry(
                                    new RetryStrategyOptions<string>
                                    {
                                        MaxRetryAttempts = 5,
                                        Delay = TimeSpan.FromMilliseconds(5),
                                        BackoffType = DelayBackoffType.Constant,
                                    }
                                )
                            )
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Polly_Timeout_ThrowsTimeoutRejectedException(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    _ = await Assert
                        .That(async () =>
                            await mediator
                                .SendAsync<SlowCommand, string>(new SlowCommand(), token)
                                .ConfigureAwait(false)
                        )
                        .Throws<TimeoutRejectedException>();
                },
                configureServices: services =>
                    services.AddPulse(configurator =>
                        configurator
                            .AddCommandHandler<SlowCommand, string, SlowCommandHandler>()
                            .AddPollyCommandPolicies<SlowCommand, string>(pipeline =>
                                pipeline.AddTimeout(TimeSpan.FromMilliseconds(50))
                            )
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Polly_Event_Retry_SucceedsAfterTransientFailures(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler = new FlakyEventHandler(failuresBeforeSuccess: 2);

        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new FlakyEvent(), token).ConfigureAwait(false);

                    _ = await Assert.That(handler.AttemptCount).IsEqualTo(3);
                },
                configureServices: services =>
                    services
                        .AddSingleton<IEventHandler<FlakyEvent>>(handler)
                        .AddPulse(configurator =>
                            configurator.AddPollyEventPolicies<FlakyEvent>(pipeline =>
                                pipeline.AddRetry(
                                    new RetryStrategyOptions
                                    {
                                        MaxRetryAttempts = 5,
                                        Delay = TimeSpan.FromMilliseconds(5),
                                        BackoffType = DelayBackoffType.Constant,
                                    }
                                )
                            )
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Polly_StreamQuery_Timeout_ThrowsTimeoutRejectedException(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    _ = await Assert
                        .That(async () =>
                        {
                            await foreach (
                                var item in mediator.StreamQueryAsync<SlowStreamQuery, int>(
                                    new SlowStreamQuery(),
                                    token
                                )
                            )
                            {
                                _ = item;
                            }
                        })
                        .Throws<TimeoutRejectedException>();
                },
                configureServices: services =>
                    services.AddPulse(configurator =>
                        configurator
                            .AddStreamQueryHandler<SlowStreamQuery, int, SlowStreamQueryHandler>()
                            .AddPollyStreamQueryPolicies<SlowStreamQuery, int>(pipeline =>
                                pipeline.AddTimeout(TimeSpan.FromMilliseconds(50))
                            )
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

    // Circuit breaker is intentionally skipped here: state is cached per registered pipeline (see
    // PollyExtensions remarks) and each of the tests above builds a fresh host/service provider, so
    // exercising a circuit breaker meaningfully would require driving multiple failures/successes
    // within a single host instance. That behavior is already covered thoroughly (open/half-open/close
    // transitions) by the real ResiliencePipeline in the unit tests
    // (PollyRequestInterceptorTests.HandleAsync_WithCircuitBreaker_BlocksAfterFailureThreshold); adding
    // it here would only re-test Polly's own circuit breaker implementation rather than the Pulse
    // integration, so it is left out to avoid excessive scaffolding for no additional coverage.

    private sealed record CreateWidgetCommand(string Name) : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class CreateWidgetCommandValidator : AbstractValidator<CreateWidgetCommand>
    {
        public CreateWidgetCommandValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    private sealed class CreateWidgetCommandHandler : ICommandHandler<CreateWidgetCommand, string>
    {
        public Task<string> HandleAsync(CreateWidgetCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult($"Created:{command.Name}");
    }

    private sealed record ListWidgetsQuery(string Prefix) : IStreamQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class ListWidgetsQueryValidator : AbstractValidator<ListWidgetsQuery>
    {
        public ListWidgetsQueryValidator() => RuleFor(x => x.Prefix).NotEmpty();
    }

    private sealed class ListWidgetsQueryHandler : IStreamQueryHandler<ListWidgetsQuery, string>
    {
        public async IAsyncEnumerable<string> HandleAsync(
            ListWidgetsQuery request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return $"{request.Prefix}-1";
            await Task.Yield();
            yield return $"{request.Prefix}-2";
        }
    }

    private sealed record FlakyCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class FlakyCommandHandler(int failuresBeforeSuccess) : ICommandHandler<FlakyCommand, string>
    {
        public int AttemptCount { get; private set; }

        public Task<string> HandleAsync(FlakyCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AttemptCount++;

            if (AttemptCount <= failuresBeforeSuccess)
            {
                throw new InvalidOperationException("Transient failure");
            }

            return Task.FromResult("success");
        }
    }

    private sealed record FlakyEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class FlakyEventHandler(int failuresBeforeSuccess) : IEventHandler<FlakyEvent>
    {
        public int AttemptCount { get; private set; }

        public Task HandleAsync(FlakyEvent @event, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AttemptCount++;

            if (AttemptCount <= failuresBeforeSuccess)
            {
                throw new InvalidOperationException("Transient failure");
            }

            return Task.CompletedTask;
        }
    }

    private sealed record SlowStreamQuery : IStreamQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class SlowStreamQueryHandler : IStreamQueryHandler<SlowStreamQuery, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            SlowStreamQuery request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            yield return 1;
        }
    }

    private sealed record SlowCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class SlowCommandHandler : ICommandHandler<SlowCommand, string>
    {
        public async Task<string> HandleAsync(SlowCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            return "done";
        }
    }
}
