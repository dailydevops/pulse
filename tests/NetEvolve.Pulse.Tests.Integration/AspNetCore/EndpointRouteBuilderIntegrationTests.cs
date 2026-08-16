namespace NetEvolve.Pulse.Tests.Integration.AspNetCore;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// End-to-end integration tests exercising <see cref="EndpointRouteBuilderExtensions"/> through a real
/// <see cref="TestServer"/> HTTP round-trip: an actual <see cref="HttpClient"/> (from <c>server.CreateClient()</c>)
/// sends requests over the ASP.NET Core pipeline (routing, model binding, JSON (de)serialization) into the
/// Pulse mediator and back, rather than invoking the mapped delegates directly.
/// </summary>
[TestGroup("AspNetCore")]
public sealed class EndpointRouteBuilderIntegrationTests
{
    [Test]
    public async Task MapCommand_WithResponse_PostsCommand_ReturnsOkWithHandlerResult(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                app => app.MapCommand<EchoCommand, EchoResult>("/echo"),
                cancellationToken
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .PostAsJsonAsync(new Uri("/echo", UriKind.Relative), new EchoCommand("hello"), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<EchoResult>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Message).IsEqualTo("hello");
        _ = await Assert.That(result.Reversed).IsEqualTo("olleh");
    }

    [Test]
    public async Task MapCommand_Void_PostsCommand_ReturnsNoContent_AndInvokesHandler(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var counter = new PingCounter();

        using var host = await CreateHostAsync(
                app => app.MapCommand<PingCommand>("/ping"),
                cancellationToken,
                services => services.AddSingleton(counter)
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .PostAsJsonAsync(new Uri("/ping", UriKind.Relative), new PingCommand("test"), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        _ = await Assert.That(counter.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MapQuery_GetsQuery_ReturnsOkWithHandlerResult(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(app => app.MapQuery<GreetingQuery, string>("/greet"), cancellationToken)
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .GetAsync(new Uri("/greet?Name=World", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<string>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("Hello, World!");
    }

    [Test]
    public async Task MapStreamQuery_GetsStream_WithNdjsonAccept_ReadsAllItemsInOrder(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                app => app.MapStreamQuery<NumbersStreamQuery, int>("/numbers"),
                cancellationToken
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        using var response = await client
            .GetAsync(
                new Uri("/numbers", UriKind.Relative),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        _ = await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/x-ndjson");

        var items = new List<int>();
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

                items.Add(JsonSerializer.Deserialize<int>(line));
            }
        }

        _ = await Assert.That(items).IsEquivalentTo([1, 2, 3]);
    }

    private static async Task<IHost> CreateHostAsync(
        Action<IEndpointRouteBuilder> mapEndpoints,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null
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
                    _ = services.AddPulse(mediator =>
                        mediator
                            .AddCommandHandler<EchoCommand, EchoResult, EchoCommandHandler>()
                            .AddCommandHandler<PingCommand, PingCommandHandler>()
                            .AddQueryHandler<GreetingQuery, string, GreetingQueryHandler>()
                            .AddStreamQueryHandler<NumbersStreamQuery, int, NumbersStreamQueryHandler>()
                    );
                    configureServices?.Invoke(services);
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(mapEndpoints);
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    private sealed record EchoCommand(string Message) : ICommand<EchoResult>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record EchoResult(string Message, string Reversed);

    private sealed class EchoCommandHandler : ICommandHandler<EchoCommand, EchoResult>
    {
        public Task<EchoResult> HandleAsync(EchoCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reversed = new string([.. command.Message.Reverse()]);
            return Task.FromResult(new EchoResult(command.Message, reversed));
        }
    }

    private sealed record PingCommand(string Value) : ICommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class PingCounter
    {
        private int _count;

        public int Count => _count;

        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class PingCommandHandler(PingCounter counter) : ICommandHandler<PingCommand, Void>
    {
        public Task<Void> HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            counter.Increment();
            return Task.FromResult<Void>(default);
        }
    }

    private sealed record GreetingQuery(string Name) : IQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class GreetingQueryHandler : IQueryHandler<GreetingQuery, string>
    {
        public Task<string> HandleAsync(GreetingQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult($"Hello, {request.Name}!");
    }

    private sealed record NumbersStreamQuery : IStreamQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class NumbersStreamQueryHandler : IStreamQueryHandler<NumbersStreamQuery, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            NumbersStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var number in new[] { 1, 2, 3 })
            {
                yield return number;
                await Task.Yield();
            }
        }
    }
}
