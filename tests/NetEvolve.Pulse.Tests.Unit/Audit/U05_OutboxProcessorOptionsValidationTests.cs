namespace NetEvolve.Pulse.Tests.Unit.Audit;

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Phase 2 / Round 01 / U05 — Critical options classes have no IValidateOptions.
///
/// OutboxProcessorOptions accepts nonsensical values (BatchSize&lt;=0, PollingInterval&lt;=Zero,
/// BackoffMultiplier&lt;=0, ProcessingTimeout=Zero) and only fails deep inside the processor
/// at runtime. No <see cref="IValidateOptions{TOptions}"/> nor data-annotation validation is
/// registered by <c>AddOutbox()</c>.
///
/// These tests are intentionally FAILING — they assert that invalid values surface as an
/// <see cref="OptionsValidationException"/> when the options are resolved. Today the options
/// resolve cleanly with the bad values, so the tests fail. Phase 3 must add validators and
/// wire ValidateOnStart().
/// </summary>
[TestGroup("Audit-Round01-U05")]
public class U05_OutboxProcessorOptionsValidationTests
{
    [Test]
    public async Task BatchSize_Zero_ShouldFailValidation_When_OptionsResolved()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(builder => builder.AddOutbox(configureProcessorOptions: o => o.BatchSize = 0));
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var resolve = () => provider.GetRequiredService<IOptions<OutboxProcessorOptions>>().Value;

        // Today: succeeds and returns OutboxProcessorOptions with BatchSize=0 (invalid).
        // Expected after Phase 3: throws OptionsValidationException.
        _ = await Assert
            .That(resolve)
            .Throws<OptionsValidationException>()
            .Because(
                "AddOutbox() must register an IValidateOptions<OutboxProcessorOptions> that rejects BatchSize<=0."
            );
    }

    [Test]
    public async Task PollingInterval_Zero_ShouldFailValidation_When_OptionsResolved()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(builder =>
            builder.AddOutbox(configureProcessorOptions: o => o.PollingInterval = System.TimeSpan.Zero)
        );
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var resolve = () => provider.GetRequiredService<IOptions<OutboxProcessorOptions>>().Value;

        _ = await Assert
            .That(resolve)
            .Throws<OptionsValidationException>()
            .Because(
                "AddOutbox() must register an IValidateOptions<OutboxProcessorOptions> that rejects PollingInterval<=TimeSpan.Zero."
            );
    }

    [Test]
    public async Task BackoffMultiplier_Zero_ShouldFailValidation_When_OptionsResolved()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(builder => builder.AddOutbox(configureProcessorOptions: o => o.BackoffMultiplier = 0d));
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var resolve = () => provider.GetRequiredService<IOptions<OutboxProcessorOptions>>().Value;

        _ = await Assert
            .That(resolve)
            .Throws<OptionsValidationException>()
            .Because(
                "AddOutbox() must register an IValidateOptions<OutboxProcessorOptions> that rejects BackoffMultiplier<=0."
            );
    }

    [Test]
    public async Task ProcessingTimeout_Zero_ShouldFailValidation_When_OptionsResolved()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(builder =>
            builder.AddOutbox(configureProcessorOptions: o => o.ProcessingTimeout = System.TimeSpan.Zero)
        );
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var resolve = () => provider.GetRequiredService<IOptions<OutboxProcessorOptions>>().Value;

        _ = await Assert
            .That(resolve)
            .Throws<OptionsValidationException>()
            .Because(
                "AddOutbox() must register an IValidateOptions<OutboxProcessorOptions> that rejects ProcessingTimeout=TimeSpan.Zero."
            );
    }
}
