namespace NetEvolve.Pulse.Tests.Unit;

using System;
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

[TestGroup("ConcurrentCommandGuard")]
public sealed class ConcurrentCommandGuardExtensionsTests
{
    [Test]
    public async Task AddConcurrentCommandGuard_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>("configurator", () => configurator!.AddConcurrentCommandGuard());
    }

    [Test]
    public async Task AddConcurrentCommandGuard_RegistersOpenGenericInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddConcurrentCommandGuard();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(ConcurrentCommandGuardInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_CalledMultipleTimes_DoesNotDuplicateInterceptors()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddConcurrentCommandGuard();
        _ = configurator.AddConcurrentCommandGuard();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(ConcurrentCommandGuardInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddConcurrentCommandGuard_TypedWithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(
            "configurator",
            () => configurator!.AddConcurrentCommandGuard<ExclusiveCommand, string>()
        );
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<ExclusiveCommand, string>) && d.ImplementationFactory != null
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_CalledMultipleTimes_DoesNotDuplicateInterceptors()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();
        _ = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<ExclusiveCommand, string>)
                && d.ImplementationFactory != null
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddConcurrentCommandGuard_VoidWithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(
            "configurator",
            () => configurator!.AddConcurrentCommandGuard<ExclusiveVoidCommand>()
        );
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Void_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddConcurrentCommandGuard<ExclusiveVoidCommand>();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<ExclusiveVoidCommand, Extensibility.Void>)
            && d.ImplementationFactory != null
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Void_CalledMultipleTimes_DoesNotDuplicateInterceptors()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddConcurrentCommandGuard<ExclusiveVoidCommand>();
        _ = configurator.AddConcurrentCommandGuard<ExclusiveVoidCommand>();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<ExclusiveVoidCommand, Extensibility.Void>)
                && d.ImplementationFactory != null
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_CombinedWithOpenGeneric_DoesNotDuplicateInterfaceRegistrations()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddConcurrentCommandGuard();
        _ = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();

        // Open-generic overload: IRequestInterceptor<,> → ConcurrentCommandGuardInterceptor<,>
        var openGenericDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(ConcurrentCommandGuardInterceptor<,>)
            )
            .ToList();

        // Typed overload: IRequestInterceptor<ExclusiveCommand, string> → factory
        var closedGenericDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<ExclusiveCommand, string>)
                && d.ImplementationFactory != null
            )
            .ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(openGenericDescriptors).HasSingleItem();
            _ = await Assert.That(closedGenericDescriptors).HasSingleItem();
        }
    }

    private sealed record ExclusiveCommand : IExclusiveCommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record ExclusiveVoidCommand : IExclusiveCommand
    {
        public string? CorrelationId { get; set; }
    }
}
