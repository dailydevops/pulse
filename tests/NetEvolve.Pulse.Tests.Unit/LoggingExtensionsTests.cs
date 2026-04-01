namespace NetEvolve.Pulse.Tests.Unit;

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

public class LoggingExtensionsTests
{
    [Test]
    public async Task AddLogging_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>("configurator", () => configurator!.AddLogging());
    }

    [Test]
    public async Task AddLogging_RegistersEventInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddLogging();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventInterceptor<>) && d.ImplementationType == typeof(LoggingEventInterceptor<>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddLogging_RegistersRequestInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddLogging();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(LoggingRequestInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddLogging_CalledMultipleTimes_DoesNotDuplicateInterceptors()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddLogging();
        _ = configurator.AddLogging();

        var eventInterceptors = services
            .Where(d =>
                d.ServiceType == typeof(IEventInterceptor<>)
                && d.ImplementationType == typeof(LoggingEventInterceptor<>)
            )
            .ToList();

        var requestInterceptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(LoggingRequestInterceptor<,>)
            )
            .ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(eventInterceptors).HasSingleItem();
            _ = await Assert.That(requestInterceptors).HasSingleItem();
        }
    }

    [Test]
    public async Task AddLogging_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddLogging(opts =>
        {
            opts.SlowRequestThreshold = TimeSpan.FromMilliseconds(200);
            opts.LogLevel = Microsoft.Extensions.Logging.LogLevel.Information;
        });

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggingInterceptorOptions>>().Value;

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.SlowRequestThreshold).IsEqualTo(TimeSpan.FromMilliseconds(200));
            _ = await Assert.That(options.LogLevel).IsEqualTo(Microsoft.Extensions.Logging.LogLevel.Information);
        }
    }

    [Test]
    public async Task AddLogging_WithoutConfigure_UsesDefaultOptions()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddLogging();

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggingInterceptorOptions>>().Value;

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.SlowRequestThreshold).IsEqualTo(TimeSpan.FromMilliseconds(500));
            _ = await Assert.That(options.LogLevel).IsEqualTo(Microsoft.Extensions.Logging.LogLevel.Debug);
        }
    }

    [Test]
    public async Task AddLogging_ReturnsConfiguratorForChaining()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddLogging();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(configurator);
            _ = await Assert.That(result).IsTypeOf<IMediatorBuilder>();
        }
    }
}
