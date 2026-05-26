namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Audit Round 01 — U12: The XML-doc example at
/// <c>src/NetEvolve.Pulse.Extensibility/IMediatorSendOnly.cs:20-34</c> injects the Scoped
/// <see cref="IMediatorSendOnly"/> directly into a Singleton <see cref="BackgroundService"/>.
/// With <see cref="ServiceProviderOptions.ValidateScopes"/> = <see langword="true"/> (the dev
/// default for <see cref="Host.CreateApplicationBuilder()"/>), host startup must throw a
/// captive-dependency <see cref="InvalidOperationException"/>.
/// </summary>
[TestGroup("U12")]
public sealed class IMediatorSendOnlyBackgroundServiceLifetimeTests
{
    [Test]
    public async Task BackgroundService_From_XmlDocExample_Fails_ScopeValidation_On_Build()
    {
        // Arrange — register Pulse defaults plus the example BackgroundService.
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        _ = services.AddHostedService<OrderBackgroundService>();

        // Act + Assert — building with scope validation must surface the captive dependency.
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        });

        _ = await Assert
            .That(ex.Message)
            .Contains("scoped", StringComparison.OrdinalIgnoreCase)
            .Because(
                "U12: BackgroundService is Singleton; IMediatorSendOnly is Scoped (ServiceCollectionExtensions.cs:111). "
                    + "The XML-doc example at IMediatorSendOnly.cs:20-34 must either work with dev defaults "
                    + "or document the IServiceScopeFactory requirement."
            );
    }

    // Verbatim from src/NetEvolve.Pulse.Extensibility/IMediatorSendOnly.cs:20-34
    private sealed class OrderBackgroundService : BackgroundService
    {
        private readonly IMediatorSendOnly _mediator;

        public OrderBackgroundService(IMediatorSendOnly mediator)
        {
            _mediator = mediator;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
