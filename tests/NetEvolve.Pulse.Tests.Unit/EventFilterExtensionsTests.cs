namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("EventFilter")]
public sealed class EventFilterExtensionsTests
{
    [Test]
    public async Task AddEventFilter_WithTypeOverload_NullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => EventFilterExtensions.AddEventFilter<TestEvent, TestEventFilter>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddEventFilter_WithPredicateOverload_NullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => EventFilterExtensions.AddEventFilter<TestEvent>(null!, (_, _) => ValueTask.FromResult(true)))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddEventFilter_WithPredicateOverload_NullPredicate_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = await Assert.That(() => configurator.AddEventFilter<TestEvent>(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddEventFilter_WithTypeOverload_RegistersFilterWithScopedLifetime()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddEventFilter<TestEvent, TestEventFilter>();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventFilter<TestEvent>) && d.ImplementationType == typeof(TestEventFilter)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddEventFilter_WithTypeOverload_ExplicitLifetime_RegistersFilterWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddEventFilter<TestEvent, TestEventFilter>(ServiceLifetime.Singleton);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventFilter<TestEvent>) && d.ImplementationType == typeof(TestEventFilter)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddEventFilter_WithPredicateOverload_RegistersFilterWithScopedLifetime()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddEventFilter<TestEvent>((_, _) => ValueTask.FromResult(true));

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventFilter<TestEvent>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddEventFilter_RegistersEventFilterInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddEventFilter<TestEvent, TestEventFilter>();

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventInterceptor<>) && d.ImplementationType == typeof(EventFilterInterceptor<>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddEventFilter_CalledMultipleTimes_DoesNotDuplicateInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddEventFilter<TestEvent, TestEventFilter>();
        _ = configurator.AddEventFilter<TestEvent, TestEventFilter>();

        var interceptorCount = services.Count(d =>
            d.ServiceType == typeof(IEventInterceptor<>) && d.ImplementationType == typeof(EventFilterInterceptor<>)
        );

        _ = await Assert.That(interceptorCount).IsEqualTo(1);
    }

    [Test]
    public async Task AddEventFilter_WithTypeOverload_FilterResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(c => c.AddEventFilter<TestEvent, TestEventFilter>());

        var provider = services.BuildServiceProvider();

        var filters = provider.GetServices<IEventFilter<TestEvent>>().ToList();

        _ = await Assert.That(filters).IsNotEmpty();
    }

    [Test]
    public async Task AddEventFilter_WithPredicateOverload_FilterResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(c => c.AddEventFilter<TestEvent>((_, _) => ValueTask.FromResult(true)));

        var provider = services.BuildServiceProvider();

        var filters = provider.GetServices<IEventFilter<TestEvent>>().ToList();

        _ = await Assert.That(filters).IsNotEmpty();
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class TestEventFilter : IEventFilter<TestEvent>
    {
        public ValueTask<bool> ShouldHandleAsync(TestEvent message, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
