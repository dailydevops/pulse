namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Caching;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("QueryCaching")]
public sealed class QueryCachingExtensionsTests
{
    [Test]
    public async Task AddQueryCaching_NullBuilder_ThrowsArgumentNullException(CancellationToken cancellationToken) =>
        _ = await Assert.That(() => QueryCachingExtensions.AddQueryCaching(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task AddQueryCaching_RegistersRequestInterceptor(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddQueryCaching();

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(DistributedCacheQueryInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddQueryCaching_WithConfigure_AppliesOptions(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddQueryCaching(opts => opts.ExpirationMode = CacheExpirationMode.Sliding);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QueryCachingOptions>>().Value;

        _ = await Assert.That(options.ExpirationMode).IsEqualTo(CacheExpirationMode.Sliding);
    }

    [Test]
    public async Task AddQueryCaching_CalledMultipleTimes_DoesNotDuplicateInterceptor(
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddQueryCaching();
        _ = builder.AddQueryCaching();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(DistributedCacheQueryInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddQueryCaching_ReturnsSameBuilder(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddQueryCaching();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(builder);
            _ = await Assert.That(result).IsTypeOf<IMediatorBuilder>();
        }
    }
}
