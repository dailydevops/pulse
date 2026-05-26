namespace NetEvolve.Pulse.Tests.Unit.AzureServiceBus;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Phase 2 audit U07: Building a service collection that follows
/// <c>src/NetEvolve.Pulse.AzureServiceBus/README.md:32-45</c> verbatim must surface
/// a clear, actionable error — pointing the user at the missing <c>AddOutbox()</c>
/// / persistence provider call — rather than silently leaving the outbox half-wired.
///
/// Currently FAILS: the provider builds without error, <c>IOutboxRepository</c> resolution
/// throws a generic <c>"No service for type … IOutboxRepository …"</c> message that does not
/// mention <c>AddOutbox</c>, <c>AddSqlServerOutbox</c>, or any actionable remediation, and
/// <c>OutboxProcessorHostedService</c> is never registered.
/// </summary>
[TestGroup("AzureServiceBus")]
public sealed class AzureServiceBusReadmeQuickStartTests
{
    private const string FakeConnectionString =
        "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";

    [Test]
    public async Task AsbReadmeQuickStart_should_register_IOutboxRepository_or_throw_actionable_DI_error()
    {
        // ARRANGE — Verbatim transcription of README:32-45 quick-start.
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options =>
            {
                options.ConnectionString = FakeConnectionString;
                options.EnableBatching = true;
            })
        );

        // ACT — Build the provider and try to resolve the persistence side.
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            // ASSERT (1) — Either IOutboxRepository is registered (preferred), or
            // resolving it throws an exception that *mentions* the missing call.
            Exception? repositoryError = null;
            try
            {
                _ = provider.GetRequiredService<IOutboxRepository>();
            }
            catch (Exception ex)
            {
                repositoryError = ex;
            }

            if (repositoryError is not null)
            {
                var message = repositoryError.Message;
                var mentionsRemediation =
                    message.Contains("AddOutbox", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("persistence", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("provider", StringComparison.OrdinalIgnoreCase);

                _ = await Assert.That(mentionsRemediation).IsTrue();
            }

            // ASSERT (2) — OutboxProcessorHostedService must be wired up so the transport
            // is actually drained; ASB-only registration without AddOutbox leaves it absent.
            var hostedServices = provider.GetServices<IHostedService>();
            var hasOutboxProcessor = false;
            foreach (var hosted in hostedServices)
            {
                if (hosted is OutboxProcessorHostedService)
                {
                    hasOutboxProcessor = true;
                    break;
                }
            }

            _ = await Assert.That(hasOutboxProcessor).IsTrue();
        }
    }
}
