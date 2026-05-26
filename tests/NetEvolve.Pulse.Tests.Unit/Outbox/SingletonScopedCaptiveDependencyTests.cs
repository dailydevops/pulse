namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Unit.EntityFramework;
using TUnit.Core;

/// <summary>
/// Phase 2 audit verification — Q01.
/// Reproduces the captive-dependency bug where the Singleton
/// <see cref="OutboxProcessorHostedService"/> consumes the Scoped
/// <c>IOutboxRepository</c> directly through its constructor.
///
/// EXPECTED TO FAIL today: the test asserts that resolving the registered
/// <see cref="IHostedService"/> enumerable through a validated root provider
/// does NOT throw. Today it throws <see cref="InvalidOperationException"/>
/// (the standard message from <c>CallSiteValidator</c>) because the
/// EntityFrameworkOutboxRepository is registered Scoped.
/// </summary>
[TestGroup("Audit-Q01")]
public sealed class SingletonScopedCaptiveDependencyTests
{
    [Test]
    public async Task OutboxProcessorHostedService_Should_Not_Capture_Scoped_Repository()
    {
        // Arrange: the standard EF-Core outbox wiring used in production.
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddDbContext<TestDbContext>(o =>
            o.UseInMemoryDatabase(nameof(OutboxProcessorHostedService_Should_Not_Capture_Scoped_Repository))
        );
        _ = services.AddPulse(config => config.AddOutbox().AddEntityFrameworkOutbox<TestDbContext>());

        // Act + Assert: building the root provider with scope validation and resolving the
        // hosted service enumerable must not throw. Today this throws InvalidOperationException
        // because OutboxProcessorHostedService is a Singleton consuming the Scoped IOutboxRepository.
        var act = () =>
        {
            using var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

            // Force resolution of all hosted services to surface the captive-dependency error.
            var hostedServices = provider.GetServices<IHostedService>().ToArray();
            return hostedServices;
        };

        // ASSERTION CAPTURES THE DEFECT:
        // The captive-dependency rule SHOULD be honored — this should NOT throw.
        // Today it throws InvalidOperationException with a "Cannot consume scoped service ... from singleton ..." message.
        _ = await Assert.That(act).ThrowsNothing();
    }
}
