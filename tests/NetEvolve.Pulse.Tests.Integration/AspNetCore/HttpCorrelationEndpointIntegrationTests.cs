namespace NetEvolve.Pulse.Tests.Integration.AspNetCore;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Http.Correlation.AspNetCore;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Integration tests verifying that a client-supplied <c>X-Correlation-ID</c> header propagates, through the
/// real <c>NetEvolve.Http.Correlation.AspNetCore</c> middleware and the <c>HttpCorrelationRequestInterceptor</c>
/// registered via <see cref="HttpCorrelationExtensions.AddHttpCorrelationEnrichment"/>, all the way into the
/// mediator request handled by a command dispatched from a real Minimal API endpoint.
/// </summary>
[TestGroup("AspNetCore")]
public sealed class HttpCorrelationEndpointIntegrationTests
{
    private const string CorrelationHeaderName = "X-Correlation-ID";

    [Test]
    public async Task Command_WithClientSuppliedCorrelationId_PropagatesToHandlerAndResponseHeader(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(cancellationToken).ConfigureAwait(false);
        using var client = host.GetTestServer().CreateClient();

        const string correlationId = "test-correlation-id-123";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/whoami")
        {
            Content = JsonContent.Create(new WhoAmICommand("caller")),
        };
        request.Headers.Add(CorrelationHeaderName, correlationId);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<WhoAmIResult>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();

        // The correlation ID observed by the mediator command handler (via the HttpCorrelation
        // request interceptor reading IHttpCorrelationAccessor) must match the one the client sent.
        _ = await Assert.That(result!.ObservedCorrelationId).IsEqualTo(correlationId);

        // The middleware also reflects the (possibly generated) correlation ID back on the response.
        _ = await Assert.That(response.Headers.Contains(CorrelationHeaderName)).IsTrue();
        _ = await Assert.That(response.Headers.GetValues(CorrelationHeaderName).First()).IsEqualTo(correlationId);
    }

    [Test]
    public async Task Command_WithoutClientSuppliedCorrelationId_StillReceivesAGeneratedCorrelationId(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(cancellationToken).ConfigureAwait(false);
        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .PostAsJsonAsync(new Uri("/whoami", UriKind.Relative), new WhoAmICommand("caller"), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<WhoAmIResult>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();

        // No header was supplied, so the middleware generates one; the handler must still observe
        // a non-empty correlation ID flowing through the interceptor.
        _ = await Assert.That(result!.ObservedCorrelationId).IsNotNull();
        _ = await Assert.That(string.IsNullOrEmpty(result.ObservedCorrelationId)).IsFalse();
    }

    [Test]
    public async Task Event_Published_From_Handler_ObservesSameCorrelationId(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sink = new CorrelationSink();

        using var host = await CreateHostAsync(
                services => services.AddSingleton(sink),
                endpoints => endpoints.MapCommand<PublishEventCommand>("/publish-event"),
                cancellationToken
            )
            .ConfigureAwait(false);
        using var client = host.GetTestServer().CreateClient();

        const string correlationId = "event-correlation-id-456";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish-event")
        {
            Content = JsonContent.Create(new PublishEventCommand()),
        };
        request.Headers.Add(CorrelationHeaderName, correlationId);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        _ = await Assert.That(sink.ObservedCorrelationId).IsEqualTo(correlationId);
    }

    [Test]
    public async Task StreamQuery_WithClientSuppliedCorrelationId_ObservesSameCorrelationId(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                configureServices: null,
                mapEndpoints: endpoints => endpoints.MapStreamQuery<CorrelationStreamQuery, string>("/stream-whoami"),
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        const string correlationId = "stream-correlation-id-789";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/stream-whoami");
        request.Headers.Add(CorrelationHeaderName, correlationId);

        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var items = new List<string>();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                items.Add(JsonSerializer.Deserialize<string>(line)!);
            }
        }

        _ = await Assert.That(items).IsEquivalentTo([correlationId]);
    }

    private static Task<IHost> CreateHostAsync(CancellationToken cancellationToken) =>
        CreateHostAsync(
            configureServices: null,
            mapEndpoints: endpoints => endpoints.MapCommand<WhoAmICommand, WhoAmIResult>("/whoami"),
            cancellationToken: cancellationToken
        );

    private static async Task<IHost> CreateHostAsync(
        Action<IServiceCollection>? configureServices,
        Action<IEndpointRouteBuilder> mapEndpoints,
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
                    _ = services.AddHttpContextAccessor();
                    _ = services.AddHttpCorrelation();
                    _ = services.AddPulse(mediator =>
                        mediator
                            .AddCommandHandler<WhoAmICommand, WhoAmIResult, WhoAmICommandHandler>()
                            .AddCommandHandler<PublishEventCommand, PublishEventCommandHandler>()
                            .AddEventHandler<CorrelationCapturedEvent, CorrelationCapturedEventHandler>()
                            .AddStreamQueryHandler<CorrelationStreamQuery, string, CorrelationStreamQueryHandler>()
                            .AddHttpCorrelationEnrichment()
                    );
                    configureServices?.Invoke(services);
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseHttpCorrelation();
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(mapEndpoints);
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    private sealed class CorrelationSink
    {
        public string? ObservedCorrelationId { get; set; }
    }

    private sealed record PublishEventCommand : ICommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class PublishEventCommandHandler(IMediator mediator) : ICommandHandler<PublishEventCommand, Void>
    {
        public async Task<Void> HandleAsync(PublishEventCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await mediator.PublishAsync(new CorrelationCapturedEvent(), cancellationToken).ConfigureAwait(false);
            return default;
        }
    }

    private sealed record CorrelationCapturedEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class CorrelationCapturedEventHandler(CorrelationSink sink) : IEventHandler<CorrelationCapturedEvent>
    {
        public Task HandleAsync(CorrelationCapturedEvent @event, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            sink.ObservedCorrelationId = @event.CorrelationId;
            return Task.CompletedTask;
        }
    }

    private sealed record CorrelationStreamQuery : IStreamQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class CorrelationStreamQueryHandler : IStreamQueryHandler<CorrelationStreamQuery, string>
    {
        public async IAsyncEnumerable<string> HandleAsync(
            CorrelationStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return request.CorrelationId ?? string.Empty;
            await Task.Yield();
        }
    }

    private sealed record WhoAmICommand(string Caller) : ICommand<WhoAmIResult>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record WhoAmIResult(string Caller, string? ObservedCorrelationId);

    private sealed class WhoAmICommandHandler : ICommandHandler<WhoAmICommand, WhoAmIResult>
    {
        public Task<WhoAmIResult> HandleAsync(WhoAmICommand command, CancellationToken cancellationToken = default) =>
            // By the time the handler runs, the HttpCorrelationRequestInterceptor has already
            // populated command.CorrelationId from IHttpCorrelationAccessor (unless the caller
            // already set one explicitly, which is not the case here).
            Task.FromResult(new WhoAmIResult(command.Caller, command.CorrelationId));
    }
}
