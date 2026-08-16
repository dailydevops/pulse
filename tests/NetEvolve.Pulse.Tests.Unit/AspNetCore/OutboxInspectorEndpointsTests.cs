namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;
using PulseEndpoints = NetEvolve.Pulse.Outbox.OutboxInspectorEndpoints;

[TestGroup("AspNetCore")]
public sealed class OutboxInspectorEndpointsTests
{
    // MapOutboxInspector — null-argument guard

    [Test]
    public void MapOutboxInspector_WithNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapOutboxInspector(null!));

    // MapOutboxInspector — default registration

    [Test]
    public async Task MapOutboxInspector_ReturnsEndpointConventionBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapOutboxInspector();

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    // GET {base}/stats

    [Test]
    public async Task GetStatistics_ReturnsOkWithMockedStatistics(CancellationToken cancellationToken)
    {
        var statistics = new OutboxStatistics
        {
            Pending = 1,
            Processing = 2,
            Completed = 3,
            Failed = 4,
            DeadLetter = 5,
        };

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetStatisticsAsync(Arg.Any<CancellationToken>()).Returns(statistics);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/outbox/stats", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<OutboxStatistics>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.Pending).IsEqualTo(1);
        _ = await Assert.That(payload.Processing).IsEqualTo(2);
        _ = await Assert.That(payload.Completed).IsEqualTo(3);
        _ = await Assert.That(payload.Failed).IsEqualTo(4);
        _ = await Assert.That(payload.DeadLetter).IsEqualTo(5);
    }

    // GET {base}/dead-letters

    [Test]
    public async Task GetDeadLetterMessages_ReturnsOkWithMockedList(CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();
        var messages = new[]
        {
            new OutboxMessage
            {
                Id = messageId,
                EventType = typeof(string),
                Payload = "{}",
                Status = OutboxMessageStatus.DeadLetter,
            },
        };

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetDeadLetterMessagesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(messages);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/outbox/dead-letters", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<OutboxMessageResponse[]>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.Length).IsEqualTo(1);
        _ = await Assert.That(payload[0].Id).IsEqualTo(messageId);
        _ = await Assert.That(payload[0].EventType).IsEqualTo(typeof(string).ToOutboxEventTypeName());
    }

    // GET {base}/dead-letters/count

    [Test]
    public async Task GetDeadLetterCount_ReturnsOkWithMockedCount(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetDeadLetterCountAsync(Arg.Any<CancellationToken>()).Returns(42L);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/outbox/dead-letters/count", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<long>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(payload).IsEqualTo(42L);
    }

    // GET {base}/dead-letters/{id:guid} — found

    [Test]
    public async Task GetDeadLetterMessage_WhenFound_ReturnsOkWithMockedMessage(CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();
        var message = new OutboxMessage
        {
            Id = messageId,
            EventType = typeof(string),
            Payload = "{}",
            Status = OutboxMessageStatus.DeadLetter,
        };

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetDeadLetterMessageAsync(messageId, Arg.Any<CancellationToken>()).Returns(message);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/outbox/dead-letters/{messageId}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<OutboxMessageResponse>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.Id).IsEqualTo(messageId);
        _ = await Assert.That(payload.EventType).IsEqualTo(typeof(string).ToOutboxEventTypeName());
    }

    // GET {base}/dead-letters/{id:guid} — not found

    [Test]
    public async Task GetDeadLetterMessage_WhenNotFound_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetDeadLetterMessageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((OutboxMessage?)null);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/outbox/dead-letters/{messageId}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // POST {base}/dead-letters/{id:guid}/replay — success

    [Test]
    public async Task ReplayMessage_WhenSucceeds_ReturnsNoContent(CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.ReplayMessageAsync(messageId, Arg.Any<CancellationToken>()).Returns(true);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri($"/pulse/outbox/dead-letters/{messageId}/replay", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    // POST {base}/dead-letters/{id:guid}/replay — not found

    [Test]
    public async Task ReplayMessage_WhenNotFound_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.ReplayMessageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri($"/pulse/outbox/dead-letters/{messageId}/replay", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // POST {base}/dead-letters/replay-all

    [Test]
    public async Task ReplayAllDeadLetter_ReturnsOkWithMockedCount(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.ReplayAllDeadLetterAsync(Arg.Any<CancellationToken>()).Returns(7);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri("/pulse/outbox/dead-letters/replay-all", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<ReplayAllResponse>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.Count).IsEqualTo(7);
    }

    // MapOutboxInspector — custom BasePath applied correctly

    [Test]
    public async Task MapOutboxInspector_WithCustomBasePath_UsesConfiguredPrefix(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetDeadLetterCountAsync(Arg.Any<CancellationToken>()).Returns(3L);

        using var host = await CreateTestHostAsync(
                mock.Object,
                options => options.BasePath = "/admin/outbox",
                cancellationToken
            )
            .ConfigureAwait(false);
        var client = host.GetTestClient();

        using var defaultPathResponse = await client
            .GetAsync(new Uri("/pulse/outbox/dead-letters/count", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(defaultPathResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        using var customPathResponse = await client
            .GetAsync(new Uri("/admin/outbox/dead-letters/count", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(customPathResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await customPathResponse.Content.ReadFromJsonAsync<long>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(payload).IsEqualTo(3L);
    }

    private static async Task<IHost> CreateTestHostAsync(
        IOutboxManagement outboxManagement,
        Action<OutboxInspectorOptions>? configure,
        CancellationToken cancellationToken
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                _ = webBuilder.UseTestServer();
                _ = webBuilder.ConfigureServices(services =>
                {
                    _ = services.AddRouting();
                    _ = services.AddSingleton(outboxManagement);
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints => endpoints.MapOutboxInspector(configure));
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    private sealed record ReplayAllResponse(int Count);

    // The wire shape of OutboxMessage as written by the outbox inspector, where EventType is
    // serialized as its outbox event type identifier string (see TypeJsonConverter), rather than
    // the raw System.Type on the domain model, which System.Text.Json cannot serialize.
    private sealed record OutboxMessageResponse(Guid Id, string EventType);
}
