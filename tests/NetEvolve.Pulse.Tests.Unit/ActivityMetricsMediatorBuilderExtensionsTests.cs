namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class ActivityMetricsMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddActivityAndMetrics_NullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => ActivityMetricsMediatorBuilderExtensions.AddActivityAndMetrics(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddActivityAndMetrics_RegistersEventInterceptor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddActivityAndMetrics();

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventInterceptor<>)
            && d.ImplementationType == typeof(ActivityAndMetricsEventInterceptor<>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddActivityAndMetrics_RegistersRequestInterceptor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddActivityAndMetrics();

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(ActivityAndMetricsRequestInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddActivityAndMetrics_CalledMultipleTimes_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddActivityAndMetrics();
        _ = builder.AddActivityAndMetrics();

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
    public async Task AddActivityAndMetrics_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddActivityAndMetrics();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(builder);
            _ = await Assert.That(result).IsTypeOf<IMediatorBuilder>();
        }
    }
}
