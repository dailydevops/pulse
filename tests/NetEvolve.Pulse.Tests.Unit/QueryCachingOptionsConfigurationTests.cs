namespace NetEvolve.Pulse.Tests.Unit;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Caching;
using TUnit.Core;

[TestGroup("QueryCaching")]
public sealed class QueryCachingOptionsConfigurationTests
{
    [Test]
    public async Task Configure_WithPopulatedSection_BindsAllProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Pulse:QueryCaching:ExpirationMode"] = "Sliding",
                    ["Pulse:QueryCaching:DefaultExpiry"] = "00:10:00",
                }
            )
            .Build();

        var configurator = new QueryCachingOptionsConfiguration(configuration);
        var options = new QueryCachingOptions();

        configurator.Configure(options);

        _ = await Assert.That(options.ExpirationMode).IsEqualTo(CacheExpirationMode.Sliding);
        _ = await Assert.That(options.DefaultExpiry).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task Configure_WithoutSection_LeavesDefaultsUnchanged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var configurator = new QueryCachingOptionsConfiguration(configuration);
        var options = new QueryCachingOptions();
        var defaultMode = options.ExpirationMode;
        var defaultExpiry = options.DefaultExpiry;

        configurator.Configure(options);

        _ = await Assert.That(options.ExpirationMode).IsEqualTo(defaultMode);
        _ = await Assert.That(options.DefaultExpiry).IsEqualTo(defaultExpiry);
    }

    [Test]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new QueryCachingOptionsConfiguration(null!));
}
