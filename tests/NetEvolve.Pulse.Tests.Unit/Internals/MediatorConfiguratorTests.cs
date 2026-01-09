namespace NetEvolve.Pulse.Tests.Unit.Internals;

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
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
}
