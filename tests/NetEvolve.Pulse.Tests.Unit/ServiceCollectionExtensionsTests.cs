namespace NetEvolve.Pulse.Tests.Unit;

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public void AddPulse_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;

        _ = Assert.Throws<ArgumentNullException>(() => services!.AddPulse());
    }

    [Test]
    public async Task AddPulse_WithoutBuilder_RegistersMediator()
    {
        var services = new ServiceCollection();

        var result = services.AddPulse();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(services);
            _ = await Assert.That(services).HasSingleItem();

            var descriptor = services[0];
            _ = await Assert.That(descriptor.ServiceType).IsEqualTo(typeof(IMediator));
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(PulseMediator));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddPulse_WithBuilder_InvokesBuilderAction()
    {
        var services = new ServiceCollection();
        var builderInvoked = false;

        var result = services.AddPulse(_ => builderInvoked = true);

        using (Assert.Multiple())
        {
            _ = await Assert.That(builderInvoked).IsTrue();
            _ = await Assert.That(result).IsSameReferenceAs(services);
        }
    }

    [Test]
    public async Task AddPulse_WithActivityAndMetrics_RegistersInterceptors()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config => config.AddActivityAndMetrics());

        var eventInterceptorDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventInterceptor<>)
            && d.ImplementationType == typeof(ActivityAndMetricsEventInterceptor<>)
        );

        var requestInterceptorDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(ActivityAndMetricsRequestInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(eventInterceptorDescriptor).IsNotNull();
            _ = await Assert.That(eventInterceptorDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);

            _ = await Assert.That(requestInterceptorDescriptor).IsNotNull();
            _ = await Assert.That(requestInterceptorDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddPulse_CanBeInvokedMultipleTimes()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse();
        _ = services.AddPulse();

        var mediatorDescriptors = services.Where(d => d.ServiceType == typeof(IMediator)).ToList();
        _ = await Assert.That(mediatorDescriptors.Count).IsEqualTo(2);
    }
}
