namespace NetEvolve.Pulse.Tests.Integration.AspNetCore;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    [Test]
    public async Task MapCommand_WithResponse_DeleteWithoutBody_BindsIdFromRoute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = Guid.NewGuid();

        using var host = await CreateHostAsync(
                app => app.MapCommand<ItemCommand, ItemResult>("/items/{id}", CommandHttpMethod.Delete),
                cancellationToken
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .DeleteAsync(new Uri($"/items/{id}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ItemResult>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(id);
        _ = await Assert.That(result.Name).IsNull();
    }

    [Test]
    public async Task MapCommand_WithResponse_DeleteWithoutBody_BindsQueryString(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = Guid.NewGuid();

        using var host = await CreateHostAsync(
                app => app.MapCommand<ItemCommand, ItemResult>("/items/{id}", CommandHttpMethod.Delete),
                cancellationToken
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .DeleteAsync(new Uri($"/items/{id}?name=archived", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ItemResult>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(id);
        _ = await Assert.That(result.Name).IsEqualTo("archived");
    }

    [Test]
    public async Task MapCommand_Void_DeleteWithoutBody_BindsIdFromRoute_ReturnsNoContent(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = Guid.NewGuid();
        var recorder = new CommandRecorder();

        using var host = await CreateHostAsync(
                app => app.MapCommand<RemoveItemCommand>("/items/{id}", CommandHttpMethod.Delete),
                cancellationToken,
                services => services.AddSingleton(recorder)
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .DeleteAsync(new Uri($"/items/{id}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        _ = await Assert.That(recorder.Id).IsEqualTo(id);
    }

    [Test]
    public async Task MapCommand_Void_DeleteWithBody_IgnoresBody_BindsIdFromRoute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var routeId = Guid.NewGuid();
        var recorder = new CommandRecorder();

        using var host = await CreateHostAsync(
                app => app.MapCommand<RemoveItemCommand>("/items/{id}", CommandHttpMethod.Delete),
                cancellationToken,
                services => services.AddSingleton(recorder)
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"/items/{routeId}", UriKind.Relative))
        {
            Content = JsonContent.Create(new RemoveItemCommand(Guid.NewGuid())),
        };

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        _ = await Assert.That(recorder.Id).IsEqualTo(routeId);
    }

    [Test]
    [Arguments(CommandHttpMethod.Post, "id")]
    [Arguments(CommandHttpMethod.Put, "id")]
    [Arguments(CommandHttpMethod.Patch, "id")]
    [Arguments(CommandHttpMethod.Put, "Id")]
    public async Task MapCommand_WithResponse_BodyWithMismatchedId_RouteValueWins(
        CommandHttpMethod httpMethod,
        string bodyIdName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var routeId = Guid.NewGuid();

        using var host = await CreateHostAsync(
                app => app.MapCommand<ItemCommand, ItemResult>("/tenants/{tenant}/items/{id}", httpMethod),
                cancellationToken
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(httpMethod.ToString().ToUpperInvariant()),
            new Uri($"/tenants/contoso/items/{routeId}", UriKind.Relative)
        )
        {
            Content = JsonContent.Create(
                new Dictionary<string, object> { [bodyIdName] = Guid.NewGuid(), ["name"] = "widget" }
            ),
        };

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ItemResult>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(routeId);
        _ = await Assert.That(result.Name).IsEqualTo("widget");
    }

    [Test]
    public async Task MapCommand_Void_PutWithMismatchedId_RouteValueWins(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var routeId = Guid.NewGuid();
        var recorder = new CommandRecorder();

        using var host = await CreateHostAsync(
                app => app.MapCommand<RemoveItemCommand>("/items/{id}", CommandHttpMethod.Put),
                cancellationToken,
                services => services.AddSingleton(recorder)
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .PutAsJsonAsync(
                new Uri($"/items/{routeId}", UriKind.Relative),
                new RemoveItemCommand(Guid.NewGuid()),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        _ = await Assert.That(recorder.Id).IsEqualTo(routeId);
    }

    [Test]
    public async Task MapCommand_WithResponse_NumericRouteValue_WithStrictNumberHandling_RouteValueWins(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                app => app.MapCommand<NumberedCommand, string>("/numbers/{number}/{enabled}", CommandHttpMethod.Put),
                cancellationToken,
                services =>
                    services.ConfigureHttpJsonOptions(options =>
                        options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict
                    )
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .PutAsJsonAsync(
                new Uri("/numbers/42/true", UriKind.Relative),
                new NumberedCommand(1, false, "answer"),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<string>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("42:True:answer");
    }

    [Test]
    public async Task MapCommand_WithResponse_PutWithUnconvertibleRouteValue_ReturnsBadRequest(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                app => app.MapCommand<ItemCommand, ItemResult>("/items/{id}", CommandHttpMethod.Put),
                cancellationToken
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .PutAsJsonAsync(
                new Uri("/items/not-a-guid", UriKind.Relative),
                new ItemCommand(Guid.NewGuid(), "widget"),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MapCommand_WithResponse_PutWithEmptyBody_ReturnsBadRequest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                app => app.MapCommand<ItemCommand, ItemResult>("/items/{id}", CommandHttpMethod.Put),
                cancellationToken
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();
        using var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        using var response = await client
            .PutAsync(new Uri($"/items/{Guid.NewGuid()}", UriKind.Relative), content, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MapCommand_WithResponse_DigitRouteValue_ForStringConverterType_RouteValueWins(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                app => app.MapCommand<OrderNumberCommand, string>("/orders/{number}", CommandHttpMethod.Put),
                cancellationToken
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();

        using var response = await client
            .PutAsJsonAsync(
                new Uri("/orders/12345", UriKind.Relative),
                new OrderNumberCommand(new OrderNumber("99")),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<string>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("12345");
    }

    [Test]
    public async Task MapCommand_WithResponse_SnakeCaseNamingPolicy_RouteValueWins(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var routeId = Guid.NewGuid();

        using var host = await CreateHostAsync(
                app => app.MapCommand<OrderItemCommand, ItemResult>("/orders/{orderId}", CommandHttpMethod.Put),
                cancellationToken,
                services =>
                    services.ConfigureHttpJsonOptions(options =>
                        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    )
            )
            .ConfigureAwait(false);

        using var client = host.GetTestServer().CreateClient();
        using var content = new StringContent(
            $"{{\"order_id\":\"{Guid.NewGuid()}\",\"item_name\":\"widget\"}}",
            Encoding.UTF8,
            "application/json"
        );

        using var response = await client
            .PutAsync(new Uri($"/orders/{routeId}", UriKind.Relative), content, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ItemResult>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(routeId);
        _ = await Assert.That(result.Name).IsEqualTo("widget");
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
                            .AddCommandHandler<ItemCommand, ItemResult, ItemCommandHandler>()
                            .AddCommandHandler<RemoveItemCommand, RemoveItemCommandHandler>()
                            .AddCommandHandler<NumberedCommand, string, NumberedCommandHandler>()
                            .AddCommandHandler<OrderNumberCommand, string, OrderNumberCommandHandler>()
                            .AddCommandHandler<OrderItemCommand, ItemResult, OrderItemCommandHandler>()
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

    private sealed record ItemCommand(Guid Id, string? Name) : ICommand<ItemResult>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record ItemResult(Guid Id, string? Name);

    private sealed class ItemCommandHandler : ICommandHandler<ItemCommand, ItemResult>
    {
        public Task<ItemResult> HandleAsync(ItemCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ItemResult(command.Id, command.Name));
    }

    private sealed record RemoveItemCommand(Guid Id) : ICommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class CommandRecorder
    {
        public Guid? Id { get; set; }
    }

    private sealed class RemoveItemCommandHandler(CommandRecorder recorder) : ICommandHandler<RemoveItemCommand, Void>
    {
        public Task<Void> HandleAsync(RemoveItemCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            recorder.Id = command.Id;
            return Task.FromResult<Void>(default);
        }
    }

    private sealed record NumberedCommand(int Number, bool Enabled, string Name) : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class NumberedCommandHandler : ICommandHandler<NumberedCommand, string>
    {
        public Task<string> HandleAsync(NumberedCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult($"{command.Number}:{command.Enabled}:{command.Name}");
    }

    [JsonConverter(typeof(OrderNumberConverter))]
    private sealed record OrderNumber(string Value);

    private sealed class OrderNumberConverter : JsonConverter<OrderNumber>
    {
        public override OrderNumber Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, OrderNumber value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    private sealed record OrderNumberCommand(OrderNumber Number) : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class OrderNumberCommandHandler : ICommandHandler<OrderNumberCommand, string>
    {
        public Task<string> HandleAsync(OrderNumberCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(command.Number.Value);
    }

    private sealed record OrderItemCommand(Guid OrderId, string? ItemName) : ICommand<ItemResult>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class OrderItemCommandHandler : ICommandHandler<OrderItemCommand, ItemResult>
    {
        public Task<ItemResult> HandleAsync(OrderItemCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ItemResult(command.OrderId, command.ItemName));
    }
}
