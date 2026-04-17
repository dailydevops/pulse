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

[TestGroup("Interceptors")]
public sealed class ConcurrentCommandGuardMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddConcurrentCommandGuard_NullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => ConcurrentCommandGuardMediatorBuilderExtensions.AddConcurrentCommandGuard(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddConcurrentCommandGuard_RegistersInterceptorAsSingleton()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddConcurrentCommandGuard();

        _ = await Assert.That(result).IsSameReferenceAs(builder);

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
    public async Task AddConcurrentCommandGuard_CalledMultipleTimes_DoesNotDuplicateInterceptor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddConcurrentCommandGuard();
        _ = builder.AddConcurrentCommandGuard();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(ConcurrentCommandGuardInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddConcurrentCommandGuard_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddConcurrentCommandGuard();

        _ = await Assert.That(result).IsSameReferenceAs(builder);
    }
}
