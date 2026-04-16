namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
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
using PulseEndpoints = EndpointRouteBuilderExtensions;

[TestGroup("AspNetCore")]
public sealed class EndpointRouteBuilderExtensionsTests
{
    // Represents an undefined CommandHttpMethod value used to verify validation behaviour.
    private const CommandHttpMethod UndefinedHttpMethod = (CommandHttpMethod)99;

    // MapCommand<TCommand, TResponse> — null-argument guards

    [Test]
    public void MapCommand_WithResponseAndNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapCommand<TestCommand, string>(null!, "/test"));

    [Test]
    public async Task MapCommand_WithResponseAndNullPattern_ThrowsArgumentNullException()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            _ = Assert.Throws<ArgumentNullException>(() => endpoints.MapCommand<TestCommand, string>(null!));
        }
    }

    // MapCommand<TCommand, TResponse> — httpMethod validation

    [Test]
    public async Task MapCommand_WithResponse_WithUndefinedMethod_ThrowsArgumentOutOfRangeException()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                endpoints.MapCommand<TestCommand, string>("/test", UndefinedHttpMethod)
            );
        }
    }

    // MapCommand<TCommand, TResponse> — valid cases

    [Test]
    public async Task MapCommand_WithResponse_DefaultPost_ReturnsRouteHandlerBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapCommand<TestCommand, string>("/test");

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    [Test]
    public async Task MapCommand_WithResponse_WithPutMethod_ReturnsRouteHandlerBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapCommand<TestCommand, string>("/test", CommandHttpMethod.Put);

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    [Test]
    public async Task MapCommand_WithResponse_WithPatchMethod_ReturnsRouteHandlerBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapCommand<TestCommand, string>("/test", CommandHttpMethod.Patch);

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    [Test]
    public async Task MapCommand_WithResponse_WithDeleteMethod_ReturnsRouteHandlerBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapCommand<TestCommand, string>("/test", CommandHttpMethod.Delete);

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    // MapCommand<TCommand> (void) — null-argument guards

    [Test]
    public void MapCommand_VoidAndNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapCommand<VoidTestCommand>(null!, "/test"));

    [Test]
    public async Task MapCommand_VoidAndNullPattern_ThrowsArgumentNullException()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            _ = Assert.Throws<ArgumentNullException>(() => endpoints.MapCommand<VoidTestCommand>(null!));
        }
    }

    // MapCommand<TCommand> (void) — httpMethod validation

    [Test]
    public async Task MapCommand_Void_WithUndefinedMethod_ThrowsArgumentOutOfRangeException()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                endpoints.MapCommand<VoidTestCommand>("/test", UndefinedHttpMethod)
            );
        }
    }

    // MapCommand<TCommand> (void) — valid cases

    [Test]
    public async Task MapCommand_Void_DefaultPost_ReturnsRouteHandlerBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapCommand<VoidTestCommand>("/test");

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    [Test]
    public async Task MapCommand_Void_WithDeleteMethod_ReturnsRouteHandlerBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapCommand<VoidTestCommand>("/test", CommandHttpMethod.Delete);

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    [Test]
    public async Task MapCommand_Void_WithPutMethod_ReturnsRouteHandlerBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapCommand<VoidTestCommand>("/test", CommandHttpMethod.Put);

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    // MapQuery — null-argument guards

    [Test]
    public void MapQuery_WithNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapQuery<TestQuery, string>(null!, "/test"));

    [Test]
    public async Task MapQuery_WithNullPattern_ThrowsArgumentNullException()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            _ = Assert.Throws<ArgumentNullException>(() => endpoints.MapQuery<TestQuery, string>(null!));
        }
    }

    [Test]
    public async Task MapQuery_ReturnsRouteHandlerBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapQuery<TestQuery, string>("/test");

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    // MapStreamQuery — null-argument guards

    [Test]
    public void MapStreamQuery_WithNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() =>
            PulseEndpoints.MapStreamQuery<TestStreamQuery, string>(null!, "/stream")
        );

    [Test]
    public async Task MapStreamQuery_WithNullPattern_ThrowsArgumentNullException()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            _ = Assert.Throws<ArgumentNullException>(() => endpoints.MapStreamQuery<TestStreamQuery, string>(null!));
        }
    }

    // MapStreamQuery — valid registration

    [Test]
    public async Task MapStreamQuery_ReturnsEndpointConventionBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapStreamQuery<TestStreamQuery, string>("/stream");

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    // MapStreamQuery — SSE format

    [Test]
    public async Task MapStreamQuery_WithItems_WritesSSEFormatByDefault(CancellationToken cancellationToken)
    {
        using var host = await CreateTestHostAsync(["first", "second"], cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/stream", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        _ = await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/event-stream");

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

#if NET10_0_OR_GREATER
        _ = await Assert.That(body).Contains("data: first\n\n");
        _ = await Assert.That(body).Contains("data: second\n\n");
#else
        _ = await Assert.That(body).Contains("data: \"first\"\n\n");
        _ = await Assert.That(body).Contains("data: \"second\"\n\n");
#endif
    }

    // MapStreamQuery — NDJSON format

    [Test]
    public async Task MapStreamQuery_WithItems_WritesNdjsonWhenAcceptHeaderRequests(CancellationToken cancellationToken)
    {
        using var host = await CreateTestHostAsync(["alpha", "beta"], cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        using var response = await client
            .GetAsync(new Uri("/stream", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        _ = await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/x-ndjson");

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(body).Contains("\"alpha\"\n");
        _ = await Assert.That(body).Contains("\"beta\"\n");
    }

    // MapStreamQuery — empty stream

    [Test]
    public async Task MapStreamQuery_EmptyStream_ReturnsOkWithEmptyBody(CancellationToken cancellationToken)
    {
        using var host = await CreateTestHostAsync([], cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/stream", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(body).IsEqualTo(string.Empty);
    }

    // MapStreamQuery — exception from handler

    [Test]
    public async Task MapStreamQuery_WhenHandlerThrows_StreamTerminates(CancellationToken cancellationToken)
    {
        using var host = await CreateThrowingTestHostAsync(cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        // The handler throws; the exception propagates from the streaming delegate and
        // terminates the stream. TestServer surfaces it directly to the caller.
        _ = await Assert
            .That(async () =>
                await client.GetAsync(new Uri("/stream", UriKind.Relative), cancellationToken).ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();
    }

    // MapStreamQuery — client disconnect

    [Test]
    public async Task MapStreamQuery_WhenClientDisconnects_CompletesGracefully(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var host = await CreateInfiniteTestHostAsync(cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "/stream");

        // Start the request, read just enough to confirm streaming started, then abort.
        using var response = await client
            .SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        // Read one SSE line to confirm streaming has started.
        _ = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);

        // Cancel the token to simulate client disconnect; endpoint should not throw.
        await cts.CancelAsync().ConfigureAwait(false);
    }

    private static async Task<IHost> CreateTestHostAsync(IEnumerable<string> items, CancellationToken cancellationToken)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                _ = webBuilder.UseTestServer();
                _ = webBuilder.ConfigureServices(services =>
                {
                    _ = services.AddRouting();
                    _ = services.AddSingleton<IStreamQueryHandler<TestStreamQuery, string>>(
                        new FixedItemsStreamQueryHandler(items)
                    );
                    _ = services.AddPulse(_ => { });
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints => endpoints.MapStreamQuery<TestStreamQuery, string>("/stream"));
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    private static async Task<IHost> CreateThrowingTestHostAsync(CancellationToken cancellationToken)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                _ = webBuilder.UseTestServer();
                _ = webBuilder.ConfigureServices(services =>
                {
                    _ = services.AddRouting();
                    _ = services.AddSingleton<IStreamQueryHandler<TestStreamQuery, string>>(
                        new ThrowingStreamQueryHandler()
                    );
                    _ = services.AddPulse(_ => { });
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints => endpoints.MapStreamQuery<TestStreamQuery, string>("/stream"));
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    private static async Task<IHost> CreateInfiniteTestHostAsync(CancellationToken cancellationToken)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                _ = webBuilder.UseTestServer();
                _ = webBuilder.ConfigureServices(services =>
                {
                    _ = services.AddRouting();
                    _ = services.AddSingleton<IStreamQueryHandler<TestStreamQuery, string>>(
                        new InfiniteStreamQueryHandler()
                    );
                    _ = services.AddPulse(_ => { });
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints => endpoints.MapStreamQuery<TestStreamQuery, string>("/stream"));
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    private sealed record TestCommand(string Value) : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record VoidTestCommand(string Value) : ICommand
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestQuery(string Id) : IQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class FixedItemsStreamQueryHandler : IStreamQueryHandler<TestStreamQuery, string>
    {
        private readonly IEnumerable<string> _items;

        public FixedItemsStreamQueryHandler(IEnumerable<string> items) => _items = items;

        public async IAsyncEnumerable<string> HandleAsync(
            TestStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            foreach (var item in _items)
            {
                yield return item;
            }
        }
    }

    private sealed class ThrowingStreamQueryHandler : IStreamQueryHandler<TestStreamQuery, string>
    {
        public async IAsyncEnumerable<string> HandleAsync(
            TestStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.FromException(new InvalidOperationException("Handler failure.")).ConfigureAwait(false);
            yield break;
        }
    }

    private sealed class InfiniteStreamQueryHandler : IStreamQueryHandler<TestStreamQuery, string>
    {
        public async IAsyncEnumerable<string> HandleAsync(
            TestStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var counter = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                yield return $"item-{counter++}";
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
