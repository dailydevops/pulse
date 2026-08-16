namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using TUnit.Core;

[TestGroup("AspNetCore")]
public sealed class OpenApiMediatorBuilderExtensionsTests
{
    [Test]
    public void EnableOpenApiMetadata_WithNullBuilder_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => OpenApiMediatorBuilderExtensions.EnableOpenApiMetadata(null!));

    [Test]
    public async Task EnableOpenApiMetadata_SetsOpenApiMetadataEnabledOnOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(builder => builder.EnableOpenApiMetadata());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AspNetCoreOptions>>();

        _ = await Assert.That(options.Value.OpenApiMetadataEnabled).IsTrue();
    }

    [Test]
    public async Task EnableOpenApiMetadata_WhenNotCalled_OptionsDefaultToDisabled()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(_ => { });

        using var provider = services.BuildServiceProvider();

        // Mirrors the production consumption in EndpointRouteBuilderExtensions: when
        // EnableOpenApiMetadata() was never called, nothing registered IOptions<AspNetCoreOptions>,
        // so it resolves to null and metadata application is skipped (treated as disabled).
        var options = provider.GetService<IOptions<AspNetCoreOptions>>();

        _ = await Assert.That(options?.Value.OpenApiMetadataEnabled ?? false).IsFalse();
    }

    [Test]
    public async Task EnableOpenApiMetadata_MapCommandWithResponse_AutoAppliesSummaryAndProduces()
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.Services.AddPulse(b => b.EnableOpenApiMetadata());
        var app = builder.Build();

        await using (app.ConfigureAwait(false))
        {
            _ = app.MapCommand<EndpointRouteBuilderExtensionsTests.TestCommand, string>("/commands");

            IEndpointRouteBuilder endpoints = app;
            var endpoint = endpoints
                .DataSources.SelectMany(dataSource => dataSource.Endpoints)
                .Single(e => ((RouteEndpoint)e).RoutePattern.RawText == "/commands");

            var summary = endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>();
            var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();

            _ = await Assert.That(summary?.Summary).IsEqualTo(nameof(EndpointRouteBuilderExtensionsTests.TestCommand));
            _ = await Assert.That(produces.Any(p => p.Type == typeof(string))).IsTrue();
        }
    }

    [Test]
    public async Task EnableOpenApiMetadata_MapQuery_AutoAppliesSummaryAndProduces()
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.Services.AddPulse(b => b.EnableOpenApiMetadata());
        var app = builder.Build();

        await using (app.ConfigureAwait(false))
        {
            _ = app.MapQuery<EndpointRouteBuilderExtensionsTests.TestQuery, string>("/queries/{id}");

            IEndpointRouteBuilder endpoints = app;
            var endpoint = endpoints
                .DataSources.SelectMany(dataSource => dataSource.Endpoints)
                .Single(e => ((RouteEndpoint)e).RoutePattern.RawText == "/queries/{id}");

            var summary = endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>();
            var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();

            _ = await Assert.That(summary?.Summary).IsEqualTo(nameof(EndpointRouteBuilderExtensionsTests.TestQuery));
            _ = await Assert.That(produces.Any(p => p.Type == typeof(string))).IsTrue();
        }
    }

    [Test]
    public async Task EnableOpenApiMetadata_MapStreamQuery_AutoAppliesSummaryAndStreamProduces()
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.Services.AddPulse(b => b.EnableOpenApiMetadata());
        var app = builder.Build();

        await using (app.ConfigureAwait(false))
        {
            _ = app.MapStreamQuery<EndpointRouteBuilderExtensionsTests.TestStreamQuery, string>("/queries/stream");

            IEndpointRouteBuilder endpoints = app;
            var endpoint = endpoints
                .DataSources.SelectMany(dataSource => dataSource.Endpoints)
                .Single(e => ((RouteEndpoint)e).RoutePattern.RawText == "/queries/stream");

            var summary = endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>();
            var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();
            var contentTypes = produces.SelectMany(p => p.ContentTypes).ToList();

            _ = await Assert
                .That(summary?.Summary)
                .IsEqualTo(nameof(EndpointRouteBuilderExtensionsTests.TestStreamQuery));
            _ = await Assert.That(contentTypes).Contains("text/event-stream");
            _ = await Assert.That(contentTypes).Contains("application/x-ndjson");
        }
    }

    [Test]
    public async Task WithoutEnableOpenApiMetadata_MapCommand_DoesNotApplyMetadata()
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.Services.AddPulse(_ => { });
        var app = builder.Build();

        await using (app.ConfigureAwait(false))
        {
            _ = app.MapCommand<EndpointRouteBuilderExtensionsTests.VoidTestCommand>("/commands/void");

            IEndpointRouteBuilder endpoints = app;
            var endpoint = endpoints
                .DataSources.SelectMany(dataSource => dataSource.Endpoints)
                .Single(e => ((RouteEndpoint)e).RoutePattern.RawText == "/commands/void");

            var summary = endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>();

            _ = await Assert.That(summary).IsNull();
        }
    }
}
