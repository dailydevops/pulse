namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class EventDispatcherExtensionsTests
{
    [Test]
    public async Task UseDefaultEventDispatcher_NullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => EventDispatcherExtensions.UseDefaultEventDispatcher<SequentialEventDispatcher>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseDefaultEventDispatcher_RegistersDispatcherAsSingleton()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.UseDefaultEventDispatcher<SequentialEventDispatcher>();

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ImplementationType == typeof(SequentialEventDispatcher)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseDefaultEventDispatcher_WithCustomLifetime_RegistersWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.UseDefaultEventDispatcher<SequentialEventDispatcher>(ServiceLifetime.Scoped);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ImplementationType == typeof(SequentialEventDispatcher)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseDefaultEventDispatcher_CalledMultipleTimes_ReplacesRegistration()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.UseDefaultEventDispatcher<ParallelEventDispatcher>();
        _ = builder.UseDefaultEventDispatcher<SequentialEventDispatcher>();

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
        var builder = new MediatorBuilder(services);

        _ = builder.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>();
        _ = builder.UseDefaultEventDispatcher<ParallelEventDispatcher>();

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
    public async Task UseDefaultEventDispatcher_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.UseDefaultEventDispatcher<ParallelEventDispatcher>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(builder);
            _ = await Assert.That(result).IsTypeOf<IMediatorBuilder>();
        }
    }

    [Test]
    public async Task UseDefaultEventDispatcher_WithFactory_RegistersDispatcher()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.UseDefaultEventDispatcher(_ => new RateLimitedEventDispatcher(maxConcurrency: 10));

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }

        await using var sp = services.BuildServiceProvider();
        var dispatcher = (RateLimitedEventDispatcher)sp.GetRequiredService<IEventDispatcher>();

        _ = await Assert.That(dispatcher.MaxConcurrency).IsEqualTo(10);
    }

    [Test]
    public async Task UseDefaultEventDispatcher_WithFactory_WithNullFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);
        Func<IServiceProvider, RateLimitedEventDispatcher>? factory = null;

        _ = Assert.Throws<ArgumentNullException>("factory", () => builder.UseDefaultEventDispatcher(factory!));
    }

    [Test]
    public async Task UseEventDispatcherFor_NullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => EventDispatcherExtensions.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseEventDispatcherFor_RegistersKeyedDispatcher()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>();

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher)
            && Equals(d.ServiceKey, typeof(TestEvent))
            && d.KeyedImplementationType == typeof(SequentialEventDispatcher)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseEventDispatcherFor_WithCustomLifetime_RegistersWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>(ServiceLifetime.Scoped);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, typeof(TestEvent))
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseEventDispatcherFor_CalledMultipleTimes_ReplacesRegistration()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.UseEventDispatcherFor<TestEvent, ParallelEventDispatcher>();
        _ = builder.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>();

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
        var builder = new MediatorBuilder(services);

        _ = builder.UseEventDispatcherFor<TestEvent, SequentialEventDispatcher>();
        _ = builder.UseEventDispatcherFor<AnotherTestEvent, ParallelEventDispatcher>();

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
        var builder = new MediatorBuilder(services);

        _ = builder.UseDefaultEventDispatcher<SequentialEventDispatcher>();
        _ = builder.UseEventDispatcherFor<TestEvent, ParallelEventDispatcher>();

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
    public async Task UseEventDispatcherFor_WithFactory_RegistersKeyedDispatcher()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.UseEventDispatcherFor<TestEvent, RateLimitedEventDispatcher>(
            _ => new RateLimitedEventDispatcher(maxConcurrency: 5)
        );

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, typeof(TestEvent))
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }

        await using var sp = services.BuildServiceProvider();
        var dispatcher = (RateLimitedEventDispatcher)sp.GetRequiredKeyedService<IEventDispatcher>(typeof(TestEvent));

        _ = await Assert.That(dispatcher.MaxConcurrency).IsEqualTo(5);
    }

    [Test]
    public async Task UseEventDispatcherFor_WithFactory_WithNullFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);
        Func<IServiceProvider, RateLimitedEventDispatcher>? factory = null;

        _ = Assert.Throws<ArgumentNullException>(
            "factory",
            () => builder.UseEventDispatcherFor<TestEvent, RateLimitedEventDispatcher>(factory!)
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
