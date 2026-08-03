namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

[TestGroup("Interceptors")]
public class LoggingInterceptorOptionsConfigurationTests
{
    [Test]
    public async Task Configure_WithPopulatedSection_BindsAllProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Pulse:Logging:LogLevel"] = "Information",
                    ["Pulse:Logging:SlowRequestThreshold"] = "00:00:01",
                }
            )
            .Build();

        var configurator = new LoggingInterceptorOptionsConfiguration(configuration);
        var options = new LoggingInterceptorOptions();

        configurator.Configure(options);

        _ = await Assert.That(options.LogLevel).IsEqualTo(LogLevel.Information);
        _ = await Assert.That(options.SlowRequestThreshold).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task Configure_WithoutSection_LeavesDefaultsUnchanged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var configurator = new LoggingInterceptorOptionsConfiguration(configuration);
        var options = new LoggingInterceptorOptions();
        var defaultLogLevel = options.LogLevel;
        var defaultThreshold = options.SlowRequestThreshold;

        configurator.Configure(options);

        _ = await Assert.That(options.LogLevel).IsEqualTo(defaultLogLevel);
        _ = await Assert.That(options.SlowRequestThreshold).IsEqualTo(defaultThreshold);
    }

    [Test]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new LoggingInterceptorOptionsConfiguration(null!));
}
