# U09 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse.AspNetCore/EndpointRouteBuilderExtensions.cs:226` — SSE branch: `JsonSerializer.Serialize(item)` (no options).
- `src/NetEvolve.Pulse.AspNetCore/EndpointRouteBuilderExtensions.cs:248` — NDJSON branch: `JsonSerializer.SerializeToUtf8Bytes(item)` (no options).
- `MapQuery` (`:142-148`) returns `TypedResults.Ok(...)`, which serializes via ASP.NET Core's `IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>` (camelCase by default and honors `ConfigureHttpJsonOptions`).
- No `IPayloadSerializer` lookup, no `IOptions<JsonSerializerOptions>` resolution, no `JsonOptions` pull in the streaming helpers (`ExecuteStreamReadServerSentEvents`, `ExecuteStreamReadNdjson`).

**Reasoning:** Two endpoints exposing the same DTO will serialize that DTO with *different* property casing: `MapQuery` honors `ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)` and emits `{ "orderId": ... }`, while `MapStreamQuery` ignores the configuration and emits `{ "OrderId": ... }` (the C# property name, PascalCase). Beyond inconsistency, the raw `JsonSerializer.Serialize<T>` call also blocks AOT trimming for stream endpoints (no `JsonSerializerContext` route, no `IPayloadSerializer` indirection).

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/AspNetCore/MapStreamQueryJsonCasingTests.cs`
- Status: written

```csharp
namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Phase 2 audit U09: MapStreamQuery uses raw <see cref="JsonSerializer"/>.Serialize
/// without options, so configured <c>JsonSerializerOptions</c> (e.g. camelCase via
/// <c>ConfigureHttpJsonOptions</c>) are silently ignored. MapQuery honors them; the
/// two endpoints serialize the *same DTO* differently.
/// </summary>
[TestGroup("AspNetCore")]
public sealed class MapStreamQueryJsonCasingTests
{
    [Test]
    public async Task MapQuery_and_MapStreamQuery_should_use_consistent_JSON_property_casing(
        CancellationToken cancellationToken
    )
    {
        // ARRANGE — Host configured with camelCase via ConfigureHttpJsonOptions.
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                _ = webBuilder.UseTestServer();
                _ = webBuilder.ConfigureServices(services =>
                {
                    _ = services.AddRouting();
                    _ = services.ConfigureHttpJsonOptions(opts =>
                        opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    );
                    _ = services.AddSingleton<IQueryHandler<EchoQuery, OrderDto>, EchoQueryHandler>();
                    _ = services.AddSingleton<IStreamQueryHandler<EchoStreamQuery, OrderDto>, EchoStreamQueryHandler>();
                    _ = services.AddPulse(_ => { });
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints =>
                    {
                        _ = endpoints.MapQuery<EchoQuery, OrderDto>("/order");
                        _ = endpoints.MapStreamQuery<EchoStreamQuery, OrderDto>("/orders/stream");
                    });
                });
            })
            .Build();

        await using (host.ConfigureAwait(false))
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);

            var client = host.GetTestClient();

            // ACT — Hit both endpoints; the stream endpoint emits NDJSON when asked.
            var queryJson = await client
                .GetStringAsync(new Uri("/order", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            using var streamRequest = new HttpRequestMessage(HttpMethod.Get, "/orders/stream");
            streamRequest.Headers.Accept.ParseAdd("application/x-ndjson");
            var streamResponse = await client.SendAsync(streamRequest, cancellationToken).ConfigureAwait(false);
            var streamJson = (
                await streamResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
            ).Trim();

            // ASSERT — MapQuery uses camelCase (configured). MapStreamQuery must match.
            using (Assert.Multiple())
            {
                _ = await Assert.That(queryJson).Contains("\"orderId\"");
                _ = await Assert.That(streamJson).Contains("\"orderId\"");
                _ = await Assert.That(streamJson).DoesNotContain("\"OrderId\"");
            }
        }
    }

    public sealed record OrderDto(string OrderId, int Quantity);

    public sealed record EchoQuery : IQuery<OrderDto>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    public sealed record EchoStreamQuery : IStreamQuery<OrderDto>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class EchoQueryHandler : IQueryHandler<EchoQuery, OrderDto>
    {
        public Task<OrderDto> HandleAsync(EchoQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(new OrderDto("o-1", 42));
    }

    private sealed class EchoStreamQueryHandler : IStreamQueryHandler<EchoStreamQuery, OrderDto>
    {
        public async IAsyncEnumerable<OrderDto> HandleAsync(
            EchoStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            yield return new OrderDto("o-1", 42);
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
```

**Notes:**
- Today: `queryJson` will be `{"orderId":"o-1","quantity":42}` (correct camelCase) but `streamJson` will be `{"OrderId":"o-1","Quantity":42}` (raw `JsonSerializer.Serialize` ignores configured options) → both `Contains("\"orderId\"")` for the stream and `DoesNotContain("\"OrderId\"")` fail.
- Phase 3 fix: inject `IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>` (or resolve `IPayloadSerializer`) inside the endpoint delegate, pass `.SerializerOptions` to `JsonSerializer.Serialize/SerializeToUtf8Bytes`. The SSE branch on .NET 10 (`TypedResults.ServerSentEvents`) likely already honors `JsonOptions` — only the NDJSON branch (`ExecuteStreamReadNdjson`) and the pre-.NET-10 SSE fallback (`ExecuteStreamReadServerSentEvents`) need updating.
- Placed in `Tests.Integration` rather than `Tests.Unit` because it spins a TestServer, mirroring the existing `EndpointRouteBuilderExtensionsTests` patterns; the integration project already references AspNetCore and TestHost. Per the prompt, integration is an acceptable location.
