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

[TestGroup("Extensions")]
public sealed class CacheInvalidationMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddCacheInvalidation_NullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => CacheInvalidationMediatorBuilderExtensions.AddCacheInvalidation(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddCacheInvalidation_RegistersCacheKeyRegistryAsSingleton()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddCacheInvalidation();

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ICacheKeyRegistry) && d.ImplementationType == typeof(InMemoryCacheKeyRegistry)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddCacheInvalidation_RegistersRequestInterceptorAsSingleton()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddCacheInvalidation();

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(CacheInvalidationInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddCacheInvalidation_CalledMultipleTimes_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddCacheInvalidation();
        _ = builder.AddCacheInvalidation();

        var registryDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(ICacheKeyRegistry) && d.ImplementationType == typeof(InMemoryCacheKeyRegistry)
            )
            .ToList();

        var interceptorDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(CacheInvalidationInterceptor<,>)
            )
            .ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(registryDescriptors).HasSingleItem();
            _ = await Assert.That(interceptorDescriptors).HasSingleItem();
        }
    }

    [Test]
    public async Task AddCacheInvalidation_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddCacheInvalidation();

        _ = await Assert.That(result).IsSameReferenceAs(builder);
    }

    [Test]
    public async Task AddCacheInvalidation_WithoutPriorAddQueryCaching_RegistersSuccessfully()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        // Registration does not require AddQueryCaching() to have run first; the ordering only matters
        // for invalidation to have an effect at runtime, not for the registration itself.
        var result = builder.AddCacheInvalidation();

        var registryDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ICacheKeyRegistry) && d.ImplementationType == typeof(InMemoryCacheKeyRegistry)
        );

        var interceptorDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(CacheInvalidationInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(builder);
            _ = await Assert.That(registryDescriptor).IsNotNull();
            _ = await Assert.That(interceptorDescriptor).IsNotNull();
        }
    }
}
