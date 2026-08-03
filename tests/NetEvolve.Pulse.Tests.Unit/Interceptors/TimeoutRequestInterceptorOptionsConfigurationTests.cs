namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

[TestGroup("Interceptors")]
public class TimeoutRequestInterceptorOptionsConfigurationTests
{
    [Test]
    public async Task Configure_WithPopulatedSection_BindsAllProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pulse:Timeout:GlobalTimeout"] = "00:00:30" })
            .Build();

        var configurator = new TimeoutRequestInterceptorOptionsConfiguration(configuration);
        var options = new TimeoutRequestInterceptorOptions();

        configurator.Configure(options);

        _ = await Assert.That(options.GlobalTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task Configure_WithoutSection_LeavesDefaultsUnchanged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var configurator = new TimeoutRequestInterceptorOptionsConfiguration(configuration);
        var options = new TimeoutRequestInterceptorOptions();
        var defaultTimeout = options.GlobalTimeout;

        configurator.Configure(options);

        _ = await Assert.That(options.GlobalTimeout).IsEqualTo(defaultTimeout);
    }

    [Test]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new TimeoutRequestInterceptorOptionsConfiguration(null!));
}
