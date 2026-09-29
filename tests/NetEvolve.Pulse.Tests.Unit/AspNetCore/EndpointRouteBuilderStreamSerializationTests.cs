namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Collections.Generic;
using System.Net;
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
using TUnit.Core;

// INVARIANT: MapStreamQuery writes every item as one single-line JSON text serialized with the
// application's HttpJsonOptions, identically for NDJSON and SSE and on every target framework.
// NDJSON spec §3.1 forbids newlines inside a JSON text, and WHATWG HTML §9.2.6 only treats lines
// prefixed with "data:" as event data.
[TestGroup("AspNetCore")]
public sealed class EndpointRouteBuilderStreamSerializationTests
{
    private const string Ndjson = "application/x-ndjson";
    private const string Sse = "text/event-stream";

    [Test]
    [Arguments(Ndjson, "{\"orderId\":1,\"note\":\"a\"}\n{\"orderId\":2,\"note\":\"b\"}\n")]
    [Arguments(Sse, "data: {\"orderId\":1,\"note\":\"a\"}\n\ndata: {\"orderId\":2,\"note\":\"b\"}\n\n")]
    public async Task MapStreamQuery_WithIndentedJsonOptions_WritesOneSingleLineJsonTextPerItem(
        string accept,
        string expectedBody,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                services =>
                {
                    _ = services.Configure<JsonSerializerOptions>(o => o.WriteIndented = true);
                    _ = services.ConfigureHttpJsonOptions(o => o.SerializerOptions.WriteIndented = true);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var body = await GetBodyAsync(host, "/orders/stream", accept, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(body).IsEqualTo(expectedBody);
    }

    [Test]
    [Arguments(Ndjson, "{\"order_id\":1,\"note\":\"a\"}\n{\"order_id\":2,\"note\":\"b\"}\n")]
    [Arguments(Sse, "data: {\"order_id\":1,\"note\":\"a\"}\n\ndata: {\"order_id\":2,\"note\":\"b\"}\n\n")]
    public async Task MapStreamQuery_WithHttpJsonNamingPolicy_UsesSameContractAsMapQuery(
        string accept,
        string expectedBody,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                services =>
                    services.ConfigureHttpJsonOptions(o =>
                        o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

        var streamBody = await GetBodyAsync(host, "/orders/stream", accept, cancellationToken).ConfigureAwait(false);
        var queryBody = await GetBodyAsync(host, "/orders/1", "application/json", cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(streamBody).IsEqualTo(expectedBody);
        _ = await Assert.That(queryBody).IsEqualTo("{\"order_id\":1,\"note\":\"a\"}");
    }

    [Test]
    [Arguments(Ndjson, "\"line1\\nline2\"\n")]
    [Arguments(Sse, "data: \"line1\\nline2\"\n\n")]
    public async Task MapStreamQuery_WithStringItemContainingNewline_WritesQuotedJsonString(
        string accept,
        string expectedBody,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(_ => { }, cancellationToken).ConfigureAwait(false);

        var body = await GetBodyAsync(host, "/texts/stream", accept, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(body).IsEqualTo(expectedBody);
    }

    [Test]
    [Arguments(Ndjson, "null\n")]
    [Arguments(Sse, "data: null\n\n")]
    public async Task MapStreamQuery_WithNullItem_WritesJsonNull(
        string accept,
        string expectedBody,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(_ => { }, cancellationToken).ConfigureAwait(false);

        var body = await GetBodyAsync(host, "/nulls/stream", accept, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(body).IsEqualTo(expectedBody);
    }

    [Test]
    [Arguments(Ndjson)]
    [Arguments(Sse)]
    public async Task MapStreamQuery_WithDerivedItem_WritesRuntimeTypeProperties(
        string accept,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(_ => { }, cancellationToken).ConfigureAwait(false);

        var body = await GetBodyAsync(host, "/shapes/stream", accept, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(body).Contains("\"id\":1");
        _ = await Assert.That(body).Contains("\"extra\":\"x\"");
    }

    // INVARIANT: SSE responses must not be cached or buffered by compression, on every target
    // framework, matching what ServerSentEventsResult does on .NET 10.
    [Test]
    public async Task MapStreamQuery_WithSse_DisablesCaching(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/texts/stream", UriKind.Relative));
        _ = request.Headers.TryAddWithoutValidation("Accept", Sse);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(response.Headers.CacheControl?.NoCache).IsTrue();
        _ = await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        _ = await Assert.That(response.Content.Headers.ContentEncoding).Contains("identity");
    }

    private static async Task<string> GetBodyAsync(
        IHost host,
        string path,
        string accept,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        _ = request.Headers.TryAddWithoutValidation("Accept", accept);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IHost> CreateHostAsync(
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                _ = webBuilder.UseTestServer();
                _ = webBuilder.ConfigureServices(services =>
                {
                    _ = services.AddRouting();
                    _ = services.AddSingleton<IStreamQueryHandler<OrderStreamQuery, OrderItem>>(
                        new FixedItemsHandler<OrderStreamQuery, OrderItem>([new(1, "a"), new(2, "b")])
                    );
                    _ = services.AddSingleton<IStreamQueryHandler<TextStreamQuery, string>>(
                        new FixedItemsHandler<TextStreamQuery, string>(["line1\nline2"])
                    );
                    _ = services.AddSingleton<IStreamQueryHandler<NullStreamQuery, string?>>(
                        new FixedItemsHandler<NullStreamQuery, string?>([null])
                    );
                    _ = services.AddSingleton<IStreamQueryHandler<ShapeStreamQuery, Shape>>(
                        new FixedItemsHandler<ShapeStreamQuery, Shape>([new NamedShape(1, "x")])
                    );
                    _ = services.AddSingleton<IQueryHandler<OrderQuery, OrderItem>, OrderQueryHandler>();
                    _ = services.AddPulse(_ => { });
                    configureServices(services);
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints =>
                    {
                        _ = endpoints.MapStreamQuery<OrderStreamQuery, OrderItem>("/orders/stream");
                        _ = endpoints.MapStreamQuery<TextStreamQuery, string>("/texts/stream");
                        _ = endpoints.MapStreamQuery<NullStreamQuery, string?>("/nulls/stream");
                        _ = endpoints.MapStreamQuery<ShapeStreamQuery, Shape>("/shapes/stream");
                        _ = endpoints.MapQuery<OrderQuery, OrderItem>("/orders/{id}");
                    });
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    internal sealed record OrderItem(int OrderId, string Note);

    internal record Shape(int Id);

    internal sealed record NamedShape(int Id, string Extra) : Shape(Id);

    internal sealed record OrderStreamQuery : IStreamQuery<OrderItem>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    internal sealed record TextStreamQuery : IStreamQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    internal sealed record NullStreamQuery : IStreamQuery<string?>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    internal sealed record ShapeStreamQuery : IStreamQuery<Shape>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    internal sealed record OrderQuery : IQuery<OrderItem>
    {
        public int Id { get; set; }
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class OrderQueryHandler : IQueryHandler<OrderQuery, OrderItem>
    {
        public Task<OrderItem> HandleAsync(OrderQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderItem(request.Id, "a"));
    }

    private sealed class FixedItemsHandler<TQuery, TItem> : IStreamQueryHandler<TQuery, TItem>
        where TQuery : IStreamQuery<TItem>
    {
        private readonly IEnumerable<TItem> _items;

        public FixedItemsHandler(IEnumerable<TItem> items) => _items = items;

        public async IAsyncEnumerable<TItem> HandleAsync(
            TQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var item in _items)
            {
                yield return item;
                await Task.Yield();
            }
        }
    }
}
