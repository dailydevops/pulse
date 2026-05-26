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
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Phase 2 audit U09: <c>MapStreamQuery</c> uses raw <see cref="JsonSerializer"/>.Serialize
/// without options, so configured <c>JsonSerializerOptions</c> (e.g. camelCase via
/// <c>ConfigureHttpJsonOptions</c>) are silently ignored. <c>MapQuery</c> honors them; the
/// two endpoints serialize the *same DTO* differently — PascalCase vs camelCase.
/// See <c>audit/verification/round-01-U09.md</c>.
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
        using var host = await CreateTestHostAsync(cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        // ACT — Hit both endpoints; the stream endpoint emits NDJSON when asked.
        var queryJson = await client
            .GetStringAsync(new Uri("/order", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        using var streamRequest = new HttpRequestMessage(HttpMethod.Get, "/orders/stream");
        streamRequest.Headers.Accept.ParseAdd("application/x-ndjson");
        using var streamResponse = await client.SendAsync(streamRequest, cancellationToken).ConfigureAwait(false);
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

    private static async Task<IHost> CreateTestHostAsync(CancellationToken cancellationToken)
    {
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

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    private sealed record OrderDto(string OrderId, int Quantity);

    private sealed record EchoQuery : IQuery<OrderDto>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record EchoStreamQuery : IStreamQuery<OrderDto>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class EchoQueryHandler : IQueryHandler<EchoQuery, OrderDto>
    {
        public Task<OrderDto> HandleAsync(EchoQuery request, CancellationToken cancellationToken = default) =>
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
