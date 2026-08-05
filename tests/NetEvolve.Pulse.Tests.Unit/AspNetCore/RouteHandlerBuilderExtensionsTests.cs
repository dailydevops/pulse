namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using NetEvolve.Extensions.TUnit;
using TUnit.Core;

[TestGroup("AspNetCore")]
public sealed class RouteHandlerBuilderExtensionsTests
{
    // WithPulseSummary<T> — null-guard

    [Test]
    public async Task WithPulseSummary_WithNullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => RouteHandlerBuilderExtensions.WithPulseSummary<TestDocumentedType>(null!))
            .Throws<ArgumentNullException>();

    // WithPulseSummary<T> — behavior

    [Test]
    public async Task WithPulseSummary_WithoutXmlDocumentation_FallsBackToTypeName()
    {
        var summary = await MapAndGetMetadataAsync<IEndpointSummaryMetadata>(builder =>
            builder.WithPulseSummary<TestDocumentedType>()
        );

        _ = await Assert.That(summary?.Summary).IsEqualTo(nameof(TestDocumentedType));
    }

    // WithPulseDescription — null-guards

    [Test]
    public async Task WithPulseDescription_WithNullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => RouteHandlerBuilderExtensions.WithPulseDescription(null!, "description"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task WithPulseDescription_WithNullDescription_ThrowsArgumentNullException()
    {
        await using var app = CreateApp();

        _ = await Assert
            .That(() => app.MapGet("/test", () => "ok").WithPulseDescription(null!))
            .Throws<ArgumentNullException>();
    }

    // WithPulseDescription — behavior

    [Test]
    public async Task WithPulseDescription_AppliesDescriptionMetadata()
    {
        var description = await MapAndGetMetadataAsync<IEndpointDescriptionMetadata>(builder =>
            builder.WithPulseDescription("A description.")
        );

        _ = await Assert.That(description?.Description).IsEqualTo("A description.");
    }

    // WithPulseTag — null-guards

    [Test]
    public async Task WithPulseTag_WithNullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => RouteHandlerBuilderExtensions.WithPulseTag(null!, "tag"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task WithPulseTag_WithNullTag_ThrowsArgumentNullException()
    {
        await using var app = CreateApp();

        _ = await Assert
            .That(() => app.MapGet("/test", () => "ok").WithPulseTag(null!))
            .Throws<ArgumentNullException>();
    }

    // WithPulseTag — behavior

    [Test]
    public async Task WithPulseTag_AppliesTagMetadata()
    {
        var tags = await MapAndGetMetadataAsync<ITagsMetadata>(builder => builder.WithPulseTag("Orders"));

        _ = await Assert.That(tags?.Tags.Contains("Orders")).IsTrue();
    }

    // WithPulseProduces<TResponse> — null-guard

    [Test]
    public async Task WithPulseProduces_WithNullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => RouteHandlerBuilderExtensions.WithPulseProduces<string>(null!))
            .Throws<ArgumentNullException>();

    // WithPulseProduces<TResponse> — behavior

    [Test]
    public async Task WithPulseProduces_AppliesProducesResponseTypeMetadata()
    {
        var metadata = await MapAndGetMetadataListAsync<IProducesResponseTypeMetadata>(builder =>
            builder.WithPulseProduces<string>()
        );

        _ = await Assert
            .That(metadata.Any(m => m.StatusCode == StatusCodes.Status200OK && m.Type == typeof(string)))
            .IsTrue();
    }

    // WithPulseStreamProduces — null-guard

    [Test]
    public async Task WithPulseStreamProduces_WithNullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => RouteHandlerBuilderExtensions.WithPulseStreamProduces(null!))
            .Throws<ArgumentNullException>();

    // WithPulseStreamProduces — behavior

    [Test]
    public async Task WithPulseStreamProduces_AppliesBothStreamContentTypes()
    {
        var metadata = await MapAndGetMetadataListAsync<IProducesResponseTypeMetadata>(builder =>
            builder.WithPulseStreamProduces()
        );

        var contentTypes = metadata
            .Where(m => m.StatusCode == StatusCodes.Status200OK)
            .SelectMany(m => m.ContentTypes)
            .ToList();

        _ = await Assert.That(contentTypes).Contains("text/event-stream");
        _ = await Assert.That(contentTypes).Contains("application/x-ndjson");
    }

    private static WebApplication CreateApp() => WebApplication.CreateBuilder().Build();

    private static async Task<TMetadata?> MapAndGetMetadataAsync<TMetadata>(
        Func<RouteHandlerBuilder, RouteHandlerBuilder> configure
    )
        where TMetadata : class
    {
        var list = await MapAndGetMetadataListAsync<TMetadata>(configure).ConfigureAwait(false);
        return list.LastOrDefault();
    }

    private static async Task<List<TMetadata>> MapAndGetMetadataListAsync<TMetadata>(
        Func<RouteHandlerBuilder, RouteHandlerBuilder> configure
    )
        where TMetadata : class
    {
        await using var app = CreateApp();

        _ = configure(app.MapGet("/test", () => "ok"));

        IEndpointRouteBuilder endpoints = app;
        var endpoint = endpoints
            .DataSources.SelectMany(dataSource => dataSource.Endpoints)
            .Single(e => ((RouteEndpoint)e).RoutePattern.RawText == "/test");

        return endpoint.Metadata.OfType<TMetadata>().ToList();
    }

#pragma warning disable S2094 // Empty type intentionally used only to exercise the typeof(T).Name fallback.
    private sealed class TestDocumentedType;
#pragma warning restore S2094
}
