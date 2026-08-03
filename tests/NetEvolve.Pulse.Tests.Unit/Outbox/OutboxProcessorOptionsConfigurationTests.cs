namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("Outbox")]
public class OutboxProcessorOptionsConfigurationTests
{
    [Test]
    public async Task Configure_WithPopulatedSection_BindsAllProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Pulse:OutboxProcessor:BatchSize"] = "50",
                    ["Pulse:OutboxProcessor:PollingInterval"] = "00:00:10",
                    ["Pulse:OutboxProcessor:MaxRetryCount"] = "5",
                    ["Pulse:OutboxProcessor:ProcessingTimeout"] = "00:01:00",
                    ["Pulse:OutboxProcessor:EnableBatchSending"] = "true",
                    ["Pulse:OutboxProcessor:EnableExponentialBackoff"] = "true",
                    ["Pulse:OutboxProcessor:BaseRetryDelay"] = "00:00:15",
                    ["Pulse:OutboxProcessor:MaxRetryDelay"] = "00:15:00",
                    ["Pulse:OutboxProcessor:BackoffMultiplier"] = "3.5",
                    ["Pulse:OutboxProcessor:AddJitter"] = "false",
                }
            )
            .Build();

        var configurator = new OutboxProcessorOptionsConfiguration(configuration);
        var options = new OutboxProcessorOptions();

        configurator.Configure(options);

        _ = await Assert.That(options.BatchSize).IsEqualTo(50);
        _ = await Assert.That(options.PollingInterval).IsEqualTo(TimeSpan.FromSeconds(10));
        _ = await Assert.That(options.MaxRetryCount).IsEqualTo(5);
        _ = await Assert.That(options.ProcessingTimeout).IsEqualTo(TimeSpan.FromMinutes(1));
        _ = await Assert.That(options.EnableBatchSending).IsTrue();
        _ = await Assert.That(options.EnableExponentialBackoff).IsTrue();
        _ = await Assert.That(options.BaseRetryDelay).IsEqualTo(TimeSpan.FromSeconds(15));
        _ = await Assert.That(options.MaxRetryDelay).IsEqualTo(TimeSpan.FromMinutes(15));
        _ = await Assert.That(options.BackoffMultiplier).IsEqualTo(3.5);
        _ = await Assert.That(options.AddJitter).IsFalse();
    }

    [Test]
    public async Task Configure_WithoutSection_LeavesDefaultsUnchanged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var configurator = new OutboxProcessorOptionsConfiguration(configuration);
        var options = new OutboxProcessorOptions();
        var defaultBatchSize = options.BatchSize;
        var defaultPollingInterval = options.PollingInterval;
        var defaultMaxRetryCount = options.MaxRetryCount;
        var defaultProcessingTimeout = options.ProcessingTimeout;
        var defaultEnableBatchSending = options.EnableBatchSending;
        var defaultEnableExponentialBackoff = options.EnableExponentialBackoff;
        var defaultBaseRetryDelay = options.BaseRetryDelay;
        var defaultMaxRetryDelay = options.MaxRetryDelay;
        var defaultBackoffMultiplier = options.BackoffMultiplier;
        var defaultAddJitter = options.AddJitter;

        configurator.Configure(options);

        _ = await Assert.That(options.BatchSize).IsEqualTo(defaultBatchSize);
        _ = await Assert.That(options.PollingInterval).IsEqualTo(defaultPollingInterval);
        _ = await Assert.That(options.MaxRetryCount).IsEqualTo(defaultMaxRetryCount);
        _ = await Assert.That(options.ProcessingTimeout).IsEqualTo(defaultProcessingTimeout);
        _ = await Assert.That(options.EnableBatchSending).IsEqualTo(defaultEnableBatchSending);
        _ = await Assert.That(options.EnableExponentialBackoff).IsEqualTo(defaultEnableExponentialBackoff);
        _ = await Assert.That(options.BaseRetryDelay).IsEqualTo(defaultBaseRetryDelay);
        _ = await Assert.That(options.MaxRetryDelay).IsEqualTo(defaultMaxRetryDelay);
        _ = await Assert.That(options.BackoffMultiplier).IsEqualTo(defaultBackoffMultiplier);
        _ = await Assert.That(options.AddJitter).IsEqualTo(defaultAddJitter);
    }

    [Test]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new OutboxProcessorOptionsConfiguration(null!));
}
