namespace NetEvolve.Pulse.Tests.Unit.DataAnnotations;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("DataAnnotations")]
public sealed class DataAnnotationsExtensionsTests
{
    [Test]
    public async Task AddDataAnnotations_NullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => DataAnnotationsExtensions.AddDataAnnotations(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddDataAnnotations_RegistersRequestInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddDataAnnotations();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(DataAnnotationsRequestInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddDataAnnotations_RegistersStreamQueryInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddDataAnnotations();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IStreamQueryInterceptor<,>)
            && d.ImplementationType == typeof(DataAnnotationsStreamQueryInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddDataAnnotations_CalledMultipleTimes_DoesNotDuplicateInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddDataAnnotations();
        _ = configurator.AddDataAnnotations();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(DataAnnotationsRequestInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddDataAnnotations_CalledMultipleTimes_DoesNotDuplicateStreamQueryInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddDataAnnotations();
        _ = configurator.AddDataAnnotations();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IStreamQueryInterceptor<,>)
                && d.ImplementationType == typeof(DataAnnotationsStreamQueryInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddDataAnnotations_ReturnsSameConfigurator()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddDataAnnotations();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);
    }

    [Test]
    public async Task AddDataAnnotations_InterceptorResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddDataAnnotations());

        var provider = services.BuildServiceProvider();

        var interceptors = provider.GetServices<IRequestInterceptor<TestCommand, string>>().ToList();

        _ = await Assert.That(interceptors).IsNotEmpty();
    }

    [Test]
    public async Task AddDataAnnotations_RegistersEventInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddDataAnnotations();

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventInterceptor<>)
            && d.ImplementationType == typeof(DataAnnotationsEventInterceptor<>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddDataAnnotations_CalledMultipleTimes_DoesNotDuplicateEventInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddDataAnnotations();
        _ = configurator.AddDataAnnotations();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IEventInterceptor<>)
                && d.ImplementationType == typeof(DataAnnotationsEventInterceptor<>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddDataAnnotations_EventInterceptorResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddDataAnnotations());

        var provider = services.BuildServiceProvider();

        var interceptors = provider.GetServices<IEventInterceptor<TestEvent>>().ToList();

        _ = await Assert.That(interceptors).IsNotEmpty();
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }

        [Required]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
