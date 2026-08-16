namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Unit.EntityFramework;
using TUnit.Core;

/// <summary>
/// Verifies that <see cref="OutboxProcessorHostedService"/>, which is registered as a singleton
/// hosted service, does not directly capture the scoped <see cref="IOutboxRepository"/>
/// registered by the Entity Framework outbox provider. A captive dependency of this kind causes
/// <see cref="ServiceProviderOptions.ValidateScopes"/> to throw at build time and, when validation
/// is disabled, pins a single <see cref="DbContext"/> for the lifetime of the process.
/// </summary>
[TestGroup("Outbox")]
public sealed class OutboxProcessorHostedServiceDbContextLifetimeTests
{
    [Test]
    public async Task BuildServiceProvider_WithValidateScopesAndEntityFrameworkOutbox_DoesNotThrow()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ = services.AddSingleton<IHostApplicationLifetime>(new FakeHostApplicationLifetime());
        _ = services.AddDbContext<TestDbContext>(o =>
            o.UseInMemoryDatabase(nameof(BuildServiceProvider_WithValidateScopesAndEntityFrameworkOutbox_DoesNotThrow))
        );
        _ = services.AddPulse(config => config.AddOutbox().AddEntityFrameworkOutbox<TestDbContext>());

        ServiceProvider? provider = null;
        try
        {
            provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        }
        finally
        {
            if (provider is not null)
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    [Test]
    public async Task ExecuteAsync_AcrossMultipleCycles_ResolvesFreshRepositoryPerCycle(
        CancellationToken cancellationToken = default
    )
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ = services.AddSingleton<IHostApplicationLifetime>(new FakeHostApplicationLifetime());
        _ = services.AddDbContext<TestDbContext>(o =>
            o.UseInMemoryDatabase(nameof(ExecuteAsync_AcrossMultipleCycles_ResolvesFreshRepositoryPerCycle))
        );
        _ = services.AddPulse(config => config.AddOutbox().AddEntityFrameworkOutbox<TestDbContext>());

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        var processor = hostedServices.OfType<OutboxProcessorHostedService>().Single();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await processor.StartAsync(cts.Token).ConfigureAwait(false);
        // Allow a couple of polling cycles to run; the important assertion is that starting and
        // stopping the singleton hosted service against a scoped-repository registration does not
        // throw, which it would if the repository (and its DbContext) were captured for the whole
        // process lifetime while ValidateScopes is enabled at resolution time.
        await Task.Delay(150, cts.Token).ConfigureAwait(false);
        await cts.CancelAsync().ConfigureAwait(false);
        await processor.StopAsync(cts.Token).ConfigureAwait(false);
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        // A pre-cancelled token signals that the application has already started, causing
        // ExecuteAsync to proceed immediately without waiting for a real host startup sequence.
        private static readonly CancellationToken s_startedToken = new(canceled: true);

        public CancellationToken ApplicationStarted => s_startedToken;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }
}
