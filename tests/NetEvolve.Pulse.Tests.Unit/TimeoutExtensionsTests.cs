namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

[TestGroup("Timeout")]
public sealed class TimeoutExtensionsTests
{
    [Test]
    public async Task AddRequestTimeout_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>("configurator", () => configurator!.AddRequestTimeout());
    }

    [Test]
    public async Task AddRequestTimeout_RegistersRequestInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddRequestTimeout();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(TimeoutRequestInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddRequestTimeout_RegistersStreamQueryInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddRequestTimeout();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IStreamQueryInterceptor<,>)
            && d.ImplementationType == typeof(TimeoutStreamQueryInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddRequestTimeout_CalledMultipleTimes_DoesNotDuplicateInterceptors()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddRequestTimeout();
        _ = configurator.AddRequestTimeout();

        var requestInterceptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(TimeoutRequestInterceptor<,>)
            )
            .ToList();

        var streamQueryInterceptors = services
            .Where(d =>
                d.ServiceType == typeof(IStreamQueryInterceptor<,>)
                && d.ImplementationType == typeof(TimeoutStreamQueryInterceptor<,>)
            )
            .ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(requestInterceptors).HasSingleItem();
            _ = await Assert.That(streamQueryInterceptors).HasSingleItem();
        }
    }

    [Test]
    public async Task AddRequestTimeout_WithGlobalTimeout_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddRequestTimeout(TimeSpan.FromSeconds(30));

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<TimeoutRequestInterceptorOptions>>().Value;

            _ = await Assert.That(options.GlobalTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
        }
    }

    [Test]
    public async Task AddRequestTimeout_WithoutGlobalTimeout_LeavesGlobalTimeoutNull()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddRequestTimeout();

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<TimeoutRequestInterceptorOptions>>().Value;

            _ = await Assert.That(options.GlobalTimeout).IsNull();
        }
    }

    [Test]
    public async Task AddRequestTimeout_ReturnsSameConfiguratorForChaining()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddRequestTimeout();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(configurator);
            _ = await Assert.That(result).IsTypeOf<IMediatorBuilder>();
        }
    }
}
