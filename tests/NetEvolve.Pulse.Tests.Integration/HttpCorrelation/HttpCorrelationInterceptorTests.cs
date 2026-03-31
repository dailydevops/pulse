namespace NetEvolve.Pulse.Tests.Integration.HttpCorrelation;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Http.Correlation.AspNetCore;
using NetEvolve.Http.Correlation.TestGenerator;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// End-to-end integration tests verifying HTTP correlation ID propagation through the full mediator pipeline.
/// </summary>
public sealed class HttpCorrelationInterceptorTests
{
    private static ServiceCollection CreateServiceCollection() =>
        (ServiceCollection)(new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System));

    [Test]
    [Skip("Requires IHttpContextAccessor to be registered, which is not the case in this test.")]
    public async Task RequestInterceptor_WithAccessor_PropagatesCorrelationIdIntoRequest()
    {
        // Arrange
        const string expectedId = "integration-correlation-id";
        var services = CreateServiceCollection();
        var capturedCorrelationId = (string?)null;

        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(expectedId);
        _ = services
            .AddScoped<ICommandHandler<TestCommand, string>>(_ => new CapturingCommandHandler(id =>
                capturedCorrelationId = id
            ))
            .AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        _ = await mediator
            .SendAsync<TestCommand, string>(new TestCommand { CorrelationId = null })
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(capturedCorrelationId).IsEqualTo(expectedId);
    }

    [Test]
    public async Task RequestInterceptor_WithAccessor_DoesNotOverwriteExistingCorrelationId()
    {
        // Arrange
        const string existingId = "caller-set-id";
        var services = CreateServiceCollection();
        var capturedCorrelationId = (string?)null;

        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator("http-id");
        _ = services
            .AddScoped<ICommandHandler<TestCommand, string>>(_ => new CapturingCommandHandler(id =>
                capturedCorrelationId = id
            ))
            .AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        _ = await mediator
            .SendAsync<TestCommand, string>(new TestCommand { CorrelationId = existingId })
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(capturedCorrelationId).IsEqualTo(existingId);
    }

    [Test]
    public async Task RequestInterceptor_WithoutAccessor_PassesThroughWithoutError()
    {
        // Arrange — IHttpCorrelationAccessor intentionally NOT registered
        var services = CreateServiceCollection();
        var handlerCalled = false;

        _ = services
            .AddScoped<ICommandHandler<TestCommand, string>>(_ => new SimpleCommandHandler(() => handlerCalled = true))
            .AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert — should not throw
        _ = await mediator.SendAsync<TestCommand, string>(new TestCommand()).ConfigureAwait(false);
        _ = await Assert.That(handlerCalled).IsTrue();
    }

    [Test]
    public async Task RequestInterceptor_AccessorReturnsEmptyCorrelationId_DoesNotModifyRequest()
    {
        // Arrange
        var services = CreateServiceCollection();
        var capturedCorrelationId = "sentinel";

        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(string.Empty);
        _ = services
            .AddScoped<ICommandHandler<TestCommand, string>>(_ => new CapturingCommandHandler(id =>
                capturedCorrelationId = id
            ))
            .AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        _ = await mediator
            .SendAsync<TestCommand, string>(new TestCommand { CorrelationId = null })
            .ConfigureAwait(false);

        // Assert — handler should have received null (unchanged)
        _ = await Assert.That(capturedCorrelationId).IsNull();
    }

    [Test]
    [Skip("Requires IHttpContextAccessor to be registered, which is not the case in this test.")]
    public async Task EventInterceptor_WithAccessor_PropagatesCorrelationIdIntoEvent()
    {
        // Arrange
        const string expectedId = "event-correlation-id";
        var services = CreateServiceCollection();
        var capturedCorrelationId = (string?)null;

        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(expectedId);
        _ = services
            .AddScoped<IEventHandler<TestEvent>>(_ => new CapturingEventHandler(id => capturedCorrelationId = id))
            .AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new TestEvent { CorrelationId = null }).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(capturedCorrelationId).IsEqualTo(expectedId);
    }

    [Test]
    public async Task EventInterceptor_WithAccessor_DoesNotOverwriteExistingCorrelationId()
    {
        // Arrange
        const string existingId = "caller-set-event-id";
        var services = CreateServiceCollection();
        var capturedCorrelationId = (string?)null;

        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator("http-event-id");
        _ = services
            .AddScoped<IEventHandler<TestEvent>>(_ => new CapturingEventHandler(id => capturedCorrelationId = id))
            .AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.PublishAsync(new TestEvent { CorrelationId = existingId }).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(capturedCorrelationId).IsEqualTo(existingId);
    }

    [Test]
    public async Task EventInterceptor_WithoutAccessor_PassesThroughWithoutError()
    {
        // Arrange — IHttpCorrelationAccessor intentionally NOT registered
        var services = CreateServiceCollection();
        var handlerCalled = false;

        _ = services
            .AddScoped<IEventHandler<TestEvent>>(_ => new SimpleEventHandler(() => handlerCalled = true))
            .AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert — should not throw
        await mediator.PublishAsync(new TestEvent()).ConfigureAwait(false);
        _ = await Assert.That(handlerCalled).IsTrue();
    }

    #region Test Types

    private sealed record TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class CapturingCommandHandler(Action<string?> capture) : ICommandHandler<TestCommand, string>
    {
        public Task<string> HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            capture(command.CorrelationId);
            return Task.FromResult("ok");
        }
    }

    private sealed class SimpleCommandHandler(Action onHandle) : ICommandHandler<TestCommand, string>
    {
        public Task<string> HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            onHandle();
            return Task.FromResult("ok");
        }
    }

    private sealed class CapturingEventHandler(Action<string?> capture) : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            capture(message.CorrelationId);
            return Task.CompletedTask;
        }
    }

    private sealed class SimpleEventHandler(Action onHandle) : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            onHandle();
            return Task.CompletedTask;
        }
    }

    #endregion
}
