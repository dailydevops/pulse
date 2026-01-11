namespace NetEvolve.Pulse.Tests.Unit.Internals;

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

public class MediatorConfiguratorTests
{
    [Test]
    public async Task Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;

        _ = Assert.Throws<ArgumentNullException>(
            "services",
            () => new MediatorConfigurator(services!).AddActivityAndMetrics()
        );
    }

    [Test]
    public async Task Constructor_WithValidServices_CreatesInstance()
    {
        var services = new ServiceCollection();

        var configurator = new MediatorConfigurator(services);

        using (Assert.Multiple())
        {
            _ = await Assert.That(configurator).IsNotNull();
            _ = await Assert.That(configurator).IsTypeOf<MediatorConfigurator>();
        }
    }

    [Test]
    public async Task AddActivityAndMetrics_RegistersEventInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        var result = configurator.AddActivityAndMetrics();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var eventInterceptorDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventInterceptor<>)
            && d.ImplementationType == typeof(ActivityAndMetricsEventInterceptor<>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(eventInterceptorDescriptor).IsNotNull();
            _ = await Assert.That(eventInterceptorDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddActivityAndMetrics_RegistersRequestInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        var result = configurator.AddActivityAndMetrics();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var requestInterceptorDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(ActivityAndMetricsRequestInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(requestInterceptorDescriptor).IsNotNull();
            _ = await Assert.That(requestInterceptorDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddActivityAndMetrics_CalledMultipleTimes_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        _ = configurator.AddActivityAndMetrics();
        _ = configurator.AddActivityAndMetrics();

        var eventInterceptors = services
            .Where(d =>
                d.ServiceType == typeof(IEventInterceptor<>)
                && d.ImplementationType == typeof(ActivityAndMetricsEventInterceptor<>)
            )
            .ToList();

        var requestInterceptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(ActivityAndMetricsRequestInterceptor<,>)
            )
            .ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(eventInterceptors).HasSingleItem();
            _ = await Assert.That(requestInterceptors).HasSingleItem();
        }
    }

    [Test]
    public async Task AddActivityAndMetrics_ReturnsConfiguratorForChaining()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        var result = configurator.AddActivityAndMetrics();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(configurator);
            _ = await Assert.That(result).IsTypeOf<IMediatorConfigurator>();
        }
    }

    [Test]
    public async Task Services_ReturnsProvidedServiceCollection()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        var result = configurator.Services;

        _ = await Assert.That(result).IsSameReferenceAs(services);
    }

    [Test]
    public async Task UseDefaultEventDispatcher_RegistersDispatcherAsSingleton()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        var result = configurator.UseDefaultEventDispatcher<SequentialEventDispatcher>();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var dispatcherDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ImplementationType == typeof(SequentialEventDispatcher)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(dispatcherDescriptor).IsNotNull();
            _ = await Assert.That(dispatcherDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseDefaultEventDispatcher_WithCustomLifetime_RegistersWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        _ = configurator.UseDefaultEventDispatcher<SequentialEventDispatcher>(ServiceLifetime.Scoped);

        var dispatcherDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ImplementationType == typeof(SequentialEventDispatcher)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(dispatcherDescriptor).IsNotNull();
            _ = await Assert.That(dispatcherDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseDefaultEventDispatcher_CalledMultipleTimes_ReplacesRegistration()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        _ = configurator.UseDefaultEventDispatcher<ParallelEventDispatcher>();
        _ = configurator.UseDefaultEventDispatcher<SequentialEventDispatcher>();

        var dispatchers = services
            .Where(d => d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null)
            .ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(dispatchers).HasSingleItem();
            _ = await Assert.That(dispatchers[0].ImplementationType).IsEqualTo(typeof(SequentialEventDispatcher));
        }
    }

    [Test]
    public async Task UseDefaultEventDispatcher_DoesNotRemoveKeyedDispatchers()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        // First register a keyed dispatcher for a specific event type
        _ = configurator.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>();

        // Then replace the global dispatcher
        _ = configurator.UseDefaultEventDispatcher<ParallelEventDispatcher>();

        var globalDispatcher = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null
        );

        var keyedDispatcher = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, typeof(TestEvent))
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(globalDispatcher).IsNotNull();
            _ = await Assert.That(globalDispatcher!.ImplementationType).IsEqualTo(typeof(ParallelEventDispatcher));
            _ = await Assert.That(keyedDispatcher).IsNotNull();
            _ = await Assert
                .That(keyedDispatcher!.KeyedImplementationType)
                .IsEqualTo(typeof(SequentialEventDispatcher));
        }
    }

    [Test]
    public async Task UseDefaultEventDispatcher_ReturnsConfiguratorForChaining()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        var result = configurator.UseDefaultEventDispatcher<ParallelEventDispatcher>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(configurator);
            _ = await Assert.That(result).IsTypeOf<IMediatorConfigurator>();
        }
    }

    [Test]
    public async Task UseEventDispatcherFor_RegistersKeyedDispatcher()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        var result = configurator.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var dispatcherDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher)
            && Equals(d.ServiceKey, typeof(TestEvent))
            && d.KeyedImplementationType == typeof(SequentialEventDispatcher)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(dispatcherDescriptor).IsNotNull();
            _ = await Assert.That(dispatcherDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseEventDispatcherFor_WithCustomLifetime_RegistersWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        _ = configurator.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>(ServiceLifetime.Scoped);

        var dispatcherDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, typeof(TestEvent))
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(dispatcherDescriptor).IsNotNull();
            _ = await Assert.That(dispatcherDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseEventDispatcherFor_CalledMultipleTimes_ReplacesRegistration()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        _ = configurator.UseEventDispatcherFor<TestEvent, ParallelEventDispatcher>();
        _ = configurator.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>();

        var dispatchers = services
            .Where(d => d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, typeof(TestEvent)))
            .ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(dispatchers).HasSingleItem();
            _ = await Assert.That(dispatchers[0].KeyedImplementationType).IsEqualTo(typeof(SequentialEventDispatcher));
        }
    }

    [Test]
    public async Task UseEventDispatcherFor_DifferentEventTypes_RegistersSeparately()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        _ = configurator.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>();
        _ = configurator.UseEventDispatcherFor<AnotherTestEvent, ParallelEventDispatcher>();

        var testEventDispatcher = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, typeof(TestEvent))
        );

        var anotherEventDispatcher = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, typeof(AnotherTestEvent))
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(testEventDispatcher).IsNotNull();
            _ = await Assert
                .That(testEventDispatcher!.KeyedImplementationType)
                .IsEqualTo(typeof(SequentialEventDispatcher));
            _ = await Assert.That(anotherEventDispatcher).IsNotNull();
            _ = await Assert
                .That(anotherEventDispatcher!.KeyedImplementationType)
                .IsEqualTo(typeof(ParallelEventDispatcher));
        }
    }

    [Test]
    public async Task UseEventDispatcherFor_DoesNotAffectGlobalDispatcher()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        _ = configurator.UseDefaultEventDispatcher<SequentialEventDispatcher>();
        _ = configurator.UseEventDispatcherFor<TestEvent, ParallelEventDispatcher>();

        var globalDispatcher = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null
        );

        var keyedDispatcher = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, typeof(TestEvent))
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(globalDispatcher).IsNotNull();
            _ = await Assert.That(globalDispatcher!.ImplementationType).IsEqualTo(typeof(SequentialEventDispatcher));
            _ = await Assert.That(keyedDispatcher).IsNotNull();
            _ = await Assert.That(keyedDispatcher!.KeyedImplementationType).IsEqualTo(typeof(ParallelEventDispatcher));
        }
    }

    [Test]
    public async Task UseDefaultEventDispatcher_WithFactory_RegistersDispatcher()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        var result = configurator.UseDefaultEventDispatcher(_ => new RateLimitedEventDispatcher(maxConcurrency: 10));

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var dispatcherDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(dispatcherDescriptor).IsNotNull();
            _ = await Assert.That(dispatcherDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }

        await using var sp = services.BuildServiceProvider();
        using var dispatcher = (RateLimitedEventDispatcher)sp.GetRequiredService<IEventDispatcher>();

        _ = await Assert.That(dispatcher.MaxConcurrency).IsEqualTo(10);
    }

    [Test]
    public async Task UseDefaultEventDispatcher_WithFactory_WithCustomLifetime_RegistersWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        _ = configurator.UseDefaultEventDispatcher(
            _ => new RateLimitedEventDispatcher(maxConcurrency: 3),
            ServiceLifetime.Scoped
        );

        var dispatcherDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(dispatcherDescriptor).IsNotNull();
            _ = await Assert.That(dispatcherDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseDefaultEventDispatcher_WithFactory_WithNullFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);
        Func<IServiceProvider, RateLimitedEventDispatcher>? factory = null;

        _ = Assert.Throws<ArgumentNullException>("factory", () => configurator.UseDefaultEventDispatcher(factory!));
    }

    [Test]
    public async Task UseEventDispatcherFor_WithFactory_RegistersKeyedDispatcher()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);

        var result = configurator.UseEventDispatcherFor<TestEvent, RateLimitedEventDispatcher>(
            _ => new RateLimitedEventDispatcher(maxConcurrency: 5)
        );

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var dispatcherDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, typeof(TestEvent))
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(dispatcherDescriptor).IsNotNull();
            _ = await Assert.That(dispatcherDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }

        await using var sp = services.BuildServiceProvider();
        using var dispatcher = (RateLimitedEventDispatcher)
            sp.GetRequiredKeyedService<IEventDispatcher>(typeof(TestEvent));

        _ = await Assert.That(dispatcher.MaxConcurrency).IsEqualTo(5);
    }

    [Test]
    public async Task UseEventDispatcherFor_WithFactory_WithNullFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorConfigurator(services);
        Func<IServiceProvider, RateLimitedEventDispatcher>? factory = null;

        _ = Assert.Throws<ArgumentNullException>(
            "factory",
            () => configurator.UseEventDispatcherFor<TestEvent, RateLimitedEventDispatcher>(factory!)
        );
    }

    private sealed class TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class AnotherTestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
