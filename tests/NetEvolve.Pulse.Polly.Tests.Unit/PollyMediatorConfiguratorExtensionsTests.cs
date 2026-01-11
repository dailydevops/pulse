namespace NetEvolve.Pulse.Polly.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using global::Polly;
using global::Polly.Retry;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class PollyMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task AddPollyRequestPolicies_NullConfigurator_ThrowsArgumentNullException() =>
        // Act & Assert
        _ = await Assert
            .That(() =>
                PollyMediatorConfiguratorExtensions.AddPollyRequestPolicies<TestCommand, string>(null!, _ => { })
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPollyRequestPolicies_NullConfigure_ThrowsArgumentNullException() =>
        // Act & Assert
        _ = await Assert
            .That(() =>
                PollyMediatorConfiguratorExtensions.AddPollyRequestPolicies<TestCommand, string>(
                    new MediatorConfiguratorStub(),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPollyRequestPolicies_RegistersPipelineAndInterceptor()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator.AddPollyRequestPolicies<TestCommand, string>(pipeline =>
                pipeline.AddRetry(
                    new RetryStrategyOptions<string> { MaxRetryAttempts = 3, Delay = TimeSpan.FromMilliseconds(10) }
                )
            )
        );

        var provider = services.BuildServiceProvider();

        // Assert
        var pipelineInstance = provider.GetKeyedService<ResiliencePipeline<string>>(typeof(TestCommand));
        _ = await Assert.That(pipelineInstance).IsNotNull();

        var interceptors = provider.GetServices<IRequestInterceptor<TestCommand, string>>();
        _ = await Assert.That(interceptors).IsNotNull();
    }

    [Test]
    public async Task AddPollyRequestPolicies_VoidCommand_RegistersPipelineAndInterceptor()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator.AddPollyRequestPolicies<VoidCommand>(pipeline => pipeline.AddTimeout(TimeSpan.FromSeconds(30)))
        );

        var provider = services.BuildServiceProvider();

        // Assert
        var pipelineInstance = provider.GetKeyedService<ResiliencePipeline<Extensibility.Void>>(typeof(VoidCommand));
        _ = await Assert.That(pipelineInstance).IsNotNull();

        var interceptors = provider.GetServices<IRequestInterceptor<VoidCommand, Extensibility.Void>>();
        _ = await Assert.That(interceptors).IsNotNull();
    }

    [Test]
    public async Task AddPollyEventPolicies_NullConfigurator_ThrowsArgumentNullException() =>
        // Act & Assert
        _ = await Assert
            .That(() => PollyMediatorConfiguratorExtensions.AddPollyEventPolicies<TestEvent>(null!, _ => { }))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPollyEventPolicies_NullConfigure_ThrowsArgumentNullException() =>
        // Act & Assert
        _ = await Assert
            .That(() =>
                PollyMediatorConfiguratorExtensions.AddPollyEventPolicies<TestEvent>(
                    new MediatorConfiguratorStub(),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPollyEventPolicies_RegistersPipelineAndInterceptor()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator.AddPollyEventPolicies<TestEvent>(pipeline =>
                pipeline.AddRetry(
                    new RetryStrategyOptions { MaxRetryAttempts = 2, Delay = TimeSpan.FromMilliseconds(10) }
                )
            )
        );

        var provider = services.BuildServiceProvider();

        // Assert
        var pipelineInstance = provider.GetKeyedService<ResiliencePipeline>(typeof(TestEvent));
        _ = await Assert.That(pipelineInstance).IsNotNull();

        var interceptors = provider.GetServices<IEventInterceptor<TestEvent>>();
        _ = await Assert.That(interceptors).IsNotNull();
    }

    [Test]
    public async Task AddPollyRequestPolicies_WithDifferentLifetimes_RespectsLifetime()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator.AddPollyRequestPolicies<TestCommand, string>(
                pipeline => pipeline.AddTimeout(TimeSpan.FromSeconds(30)),
                ServiceLifetime.Scoped
            )
        );

        var provider = services.BuildServiceProvider();

        // Assert - Create two scopes and verify different instances
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var pipeline1 = scope1.ServiceProvider.GetKeyedService<ResiliencePipeline<string>>(typeof(TestCommand));
        var pipeline2 = scope2.ServiceProvider.GetKeyedService<ResiliencePipeline<string>>(typeof(TestCommand));

        _ = await Assert.That(pipeline1).IsNotNull();
        _ = await Assert.That(pipeline2).IsNotNull();
        _ = await Assert.That(pipeline1).IsNotEqualTo(pipeline2);
    }

    [Test]
    public async Task AddPollyRequestPolicies_CalledTwice_ReplacesExistingRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator
                .AddPollyRequestPolicies<TestCommand, string>(pipeline =>
                    pipeline.AddRetry(new RetryStrategyOptions<string> { MaxRetryAttempts = 3 })
                )
                .AddPollyRequestPolicies<TestCommand, string>(pipeline =>
                    pipeline.AddRetry(new RetryStrategyOptions<string> { MaxRetryAttempts = 5 })
                )
        );

        var provider = services.BuildServiceProvider();

        // Assert - Should have only one registration (the second one)
        var interceptors = provider.GetServices<IRequestInterceptor<TestCommand, string>>().ToList();
        _ = await Assert.That(interceptors.Count).IsEqualTo(1);

        var pipeline = provider.GetKeyedService<ResiliencePipeline<string>>(typeof(TestCommand));
        _ = await Assert.That(pipeline).IsNotNull();
    }

    [Test]
    public async Task AddPollyEventPolicies_CalledTwice_ReplacesExistingRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator
                .AddPollyEventPolicies<TestEvent>(pipeline =>
                    pipeline.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 2 })
                )
                .AddPollyEventPolicies<TestEvent>(pipeline =>
                    pipeline.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 4 })
                )
        );

        var provider = services.BuildServiceProvider();

        // Assert - Should have only one registration (the second one)
        var interceptors = provider.GetServices<IEventInterceptor<TestEvent>>().ToList();
        _ = await Assert.That(interceptors.Count).IsEqualTo(1);

        var pipeline = provider.GetKeyedService<ResiliencePipeline>(typeof(TestEvent));
        _ = await Assert.That(pipeline).IsNotNull();
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record VoidCommand : ICommand<Extensibility.Void>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    /// <summary>
    /// Test stub for IMediatorConfigurator used to test argument validation
    /// </summary>
    private sealed class MediatorConfiguratorStub : IMediatorConfigurator
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public IMediatorConfigurator AddActivityAndMetrics() => throw new NotImplementedException();

        public IMediatorConfigurator UseDefaultEventDispatcher<TDispatcher>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();

        public IMediatorConfigurator UseDefaultEventDispatcher<TDispatcher>(
            Func<IServiceProvider, TDispatcher> factory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();

        public IMediatorConfigurator UseEventDispatcherFor<TEvent, TDispatcher>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TEvent : IEvent
            where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();

        public IMediatorConfigurator UseEventDispatcherFor<TEvent, TDispatcher>(
            Func<IServiceProvider, TDispatcher> factory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TEvent : IEvent
            where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();
    }
}
