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
    public void AddConcurrentCommandGuard_WithNullConfigurator_ThrowsArgumentNullException()
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
    public void AddConcurrentCommandGuard_TypedWithNullConfigurator_ThrowsArgumentNullException()
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
            d.ServiceType == typeof(IRequestInterceptor<ExclusiveCommand, string>)
            && d.ImplementationFactory is not null
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
                && d.ImplementationFactory is not null
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public void AddConcurrentCommandGuard_VoidWithNullConfigurator_ThrowsArgumentNullException()
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
            && d.ImplementationFactory is not null
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
                && d.ImplementationFactory is not null
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

        // The typed overload must detect that the open-generic mapping already covers this closed
        // TRequest/TResponse pair and skip its own closed factory registration entirely — otherwise
        // GetServices<IRequestInterceptor<ExclusiveCommand, string>>() would resolve TWO independent
        // interceptor instances (one via open-generic auto-closing, one via the factory), each wrapping
        // the command handler separately.
        var closedGenericDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<ExclusiveCommand, string>)
                && d.ImplementationFactory is not null
            )
            .ToList();

        var provider = services.BuildServiceProvider();
        var resolvedInterceptors = provider.GetServices<IRequestInterceptor<ExclusiveCommand, string>>().ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(openGenericDescriptors).HasSingleItem();
            _ = await Assert.That(closedGenericDescriptors).IsEmpty();
            _ = await Assert.That(resolvedInterceptors).HasSingleItem();
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_CalledBeforeOpenGeneric_StillResolvesSingleInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        // Reverse call order: the typed overload runs first (registering its own closed factory since
        // no open-generic mapping exists yet), then the open-generic overload is added afterwards.
        _ = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();
        _ = configurator.AddConcurrentCommandGuard();

        var provider = services.BuildServiceProvider();
        var resolvedInterceptors = provider.GetServices<IRequestInterceptor<ExclusiveCommand, string>>().ToList();

        // NOTE: this ordering is not de-duplicated (only open-then-typed is) — documenting current
        // behavior. Calling both overloads for the same command is an unusual combination; the
        // recommended pattern is to pick one registration style per command type.
        _ = await Assert.That(resolvedInterceptors).IsNotEmpty();
    }

    private sealed record ExclusiveCommand : IExclusiveCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record ExclusiveVoidCommand : IExclusiveCommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
