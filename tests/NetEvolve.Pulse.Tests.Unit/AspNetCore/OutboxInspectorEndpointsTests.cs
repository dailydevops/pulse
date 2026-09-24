namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using TUnit.Core;
using PulseEndpoints = OutboxInspectorEndpoints;

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
        _ = await Assert.That(payload.Payload).IsEqualTo("{}");
        _ = await Assert.That(payload.Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
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

    // GET {base}/dead-letters — invalid paging is rejected before reaching IOutboxManagement

    [Test]
    [Arguments("pageSize=0")]
    [Arguments("pageSize=-1")]
    [Arguments("page=-1")]
    [Arguments("pageSize=2&page=2147483647")]
    public async Task GetDeadLetterMessages_WithInvalidPaging_ReturnsBadRequest(
        string query,
        CancellationToken cancellationToken
    )
    {
        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetDeadLetterMessagesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OutboxMessage>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/outbox/dead-letters?{query}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        mock.GetDeadLetterMessagesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .WasCalled(Times.Never);
    }

    // GET {base}/messages

    [Test]
    public async Task GetMessages_WithoutQuery_UsesDefaultsAndReturnsMessages(CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();
        var messages = new[]
        {
            new OutboxMessage
            {
                Id = messageId,
                EventType = typeof(string),
                Payload = "{\"value\":1}",
                Status = OutboxMessageStatus.Pending,
            },
        };

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetMessagesAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<OutboxMessageStatus?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(messages);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/outbox/messages", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<OutboxMessageResponse[]>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.Length).IsEqualTo(1);
        _ = await Assert.That(payload[0].Id).IsEqualTo(messageId);
        _ = await Assert.That(payload[0].EventType).IsEqualTo(typeof(string).ToOutboxEventTypeName());
        _ = await Assert.That(payload[0].Payload).IsEqualTo("{\"value\":1}");
        _ = await Assert.That(payload[0].Status).IsEqualTo(OutboxMessageStatus.Pending);

        mock.GetMessagesAsync(50, 0, null, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    [Arguments("status=DeadLetter")]
    [Arguments("status=4")]
    public async Task GetMessages_WithQuery_PassesPagingAndStatusThrough(
        string statusQuery,
        CancellationToken cancellationToken
    )
    {
        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetMessagesAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<OutboxMessageStatus?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Array.Empty<OutboxMessage>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(
                new Uri($"/pulse/outbox/messages?pageSize=10&page=2&{statusQuery}", UriKind.Relative),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.GetMessagesAsync(10, 2, OutboxMessageStatus.DeadLetter, Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    [Arguments("pageSize=0")]
    [Arguments("pageSize=-1")]
    [Arguments("page=-1")]
    [Arguments("pageSize=2&page=2147483647")]
    [Arguments("status=99")]
    [Arguments("status=Unknown")]
    public async Task GetMessages_WithInvalidQuery_ReturnsBadRequest(string query, CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/outbox/messages?{query}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        mock.GetMessagesAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<OutboxMessageStatus?>(),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Never);
    }

    // GET {base}/messages/{id:guid}

    [Test]
    public async Task GetMessage_WhenFound_ReturnsOkWithMessage(CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();
        var message = new OutboxMessage
        {
            Id = messageId,
            EventType = typeof(string),
            Payload = "{}",
            Status = OutboxMessageStatus.Processing,
        };

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetMessageAsync(messageId, Arg.Any<CancellationToken>()).Returns(message);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/outbox/messages/{messageId}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<OutboxMessageResponse>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.Id).IsEqualTo(messageId);
        _ = await Assert.That(payload.EventType).IsEqualTo(typeof(string).ToOutboxEventTypeName());
        _ = await Assert.That(payload.Status).IsEqualTo(OutboxMessageStatus.Processing);
    }

    [Test]
    public async Task GetMessage_WhenNotFound_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetMessageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((OutboxMessage?)null);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/outbox/messages/{Guid.NewGuid()}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetMessage_WithNonGuidId_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/outbox/messages/not-a-guid", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        mock.GetMessageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).WasCalled(Times.Never);
    }

    // POST {base}/messages/{id:guid}/replay — alias of the dead-letter replay

    [Test]
    [Arguments(true, HttpStatusCode.NoContent)]
    [Arguments(false, HttpStatusCode.NotFound)]
    public async Task ReplayMessageViaMessagesRoute_ReturnsExpectedStatus(
        bool replayed,
        HttpStatusCode expected,
        CancellationToken cancellationToken
    )
    {
        var messageId = Guid.NewGuid();

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.ReplayMessageAsync(messageId, Arg.Any<CancellationToken>()).Returns(replayed);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri($"/pulse/outbox/messages/{messageId}/replay", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(expected);

        mock.ReplayMessageAsync(messageId, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    // POST {base}/dead-letters/{id:guid}/dismiss

    [Test]
    [Arguments(true, HttpStatusCode.NoContent)]
    [Arguments(false, HttpStatusCode.NotFound)]
    public async Task DismissMessage_ReturnsExpectedStatus(
        bool dismissed,
        HttpStatusCode expected,
        CancellationToken cancellationToken
    )
    {
        var messageId = Guid.NewGuid();

        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.DismissMessageAsync(messageId, Arg.Any<CancellationToken>()).Returns(dismissed);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri($"/pulse/outbox/dead-letters/{messageId}/dismiss", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(expected);

        mock.DismissMessageAsync(messageId, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task DismissMessage_WithNonGuidId_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri("/pulse/outbox/dead-letters/not-a-guid/dismiss", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        mock.DismissMessageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).WasCalled(Times.Never);
    }

    // OutboxInspectorOptions — defaults

    [Test]
    public async Task OutboxInspectorOptions_Defaults_AreExpected()
    {
        var options = new OutboxInspectorOptions();

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.BasePath).IsEqualTo("/pulse/outbox");
            _ = await Assert.That(options.RouteGroupName).IsEqualTo("Pulse Outbox Inspector");
        }
    }

    // MapOutboxInspector — RouteGroupName is applied as endpoint group name metadata

    [Test]
    public async Task MapOutboxInspector_WithDefaultOptions_AppliesDefaultGroupName()
    {
        var app = CreateApplication();
        await using (app.ConfigureAwait(false))
        {
            _ = app.MapOutboxInspector();

            var groupNames = GetEndpoints(app)
                .Select(e => e.Metadata.GetMetadata<IEndpointGroupNameMetadata>()?.EndpointGroupName)
                .ToArray();

            _ = await Assert.That(groupNames).IsNotEmpty();
            _ = await Assert.That(groupNames).All(n => n == "Pulse Outbox Inspector");
        }
    }

    [Test]
    public async Task MapOutboxInspector_WithCustomGroupName_AppliesConfiguredGroupName()
    {
        var app = CreateApplication();
        await using (app.ConfigureAwait(false))
        {
            _ = app.MapOutboxInspector(options => options.RouteGroupName = "Admin Outbox");

            var groupNames = GetEndpoints(app)
                .Select(e => e.Metadata.GetMetadata<IEndpointGroupNameMetadata>()?.EndpointGroupName)
                .ToArray();

            _ = await Assert.That(groupNames).IsNotEmpty();
            _ = await Assert.That(groupNames).All(n => n == "Admin Outbox");
        }
    }

    // MapOutboxInspector — no built-in authorization, but RequireAuthorization can be chained

    [Test]
    public async Task MapOutboxInspector_WithoutRequireAuthorization_AddsNoAuthorizationMetadata()
    {
        var app = CreateApplication();
        await using (app.ConfigureAwait(false))
        {
            _ = app.MapOutboxInspector();

            var endpoints = GetEndpoints(app);

            _ = await Assert.That(endpoints).IsNotEmpty();
            _ = await Assert.That(endpoints.Any(e => e.Metadata.GetMetadata<IAuthorizeData>() is not null)).IsFalse();
        }
    }

    [Test]
    public async Task MapOutboxInspector_WithRequireAuthorization_AppliesPolicyToAllEndpoints()
    {
        var app = CreateApplication();
        await using (app.ConfigureAwait(false))
        {
            _ = app.MapOutboxInspector().RequireAuthorization("OutboxAdmin");

            var policies = GetEndpoints(app).Select(e => e.Metadata.GetMetadata<IAuthorizeData>()?.Policy).ToArray();

            _ = await Assert.That(policies).IsNotEmpty();
            _ = await Assert.That(policies).All(p => p == "OutboxAdmin");
        }
    }

    // GET {base}/dead-letters — paging binding

    [Test]
    public async Task GetDeadLetterMessages_WithoutQuery_UsesDefaultPaging(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetDeadLetterMessagesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OutboxMessage>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/outbox/dead-letters", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.GetDeadLetterMessagesAsync(50, 0, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task GetDeadLetterMessages_WithPagingQuery_PassesValuesThrough(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();
        _ = mock.GetDeadLetterMessagesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OutboxMessage>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/outbox/dead-letters?pageSize=10&page=2", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.GetDeadLetterMessagesAsync(10, 2, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    // {id:guid} route constraint — non-guid identifiers do not match any endpoint

    [Test]
    public async Task GetDeadLetterMessage_WithNonGuidId_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/outbox/dead-letters/not-a-guid", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        mock.GetDeadLetterMessageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task ReplayMessage_WithNonGuidId_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IOutboxManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri("/pulse/outbox/dead-letters/not-a-guid/replay", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        mock.ReplayMessageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).WasCalled(Times.Never);
    }

    // POST {base}/dead-letters/replay-all — exact response body shape

    [Test]
    public async Task ReplayAllDeadLetter_ReturnsCountOnlyBody(CancellationToken cancellationToken)
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

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(body).IsEqualTo("""{"count":7}""");
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
    private sealed record OutboxMessageResponse(Guid Id, string EventType, string Payload, OutboxMessageStatus Status);

    private static WebApplication CreateApplication()
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.Services.AddAuthorization();
        _ = builder.Services.AddSingleton(Mock.Of<IOutboxManagement>().Object);
        return builder.Build();
    }

    private static Endpoint[] GetEndpoints(IEndpointRouteBuilder app) =>
        [.. app.DataSources.SelectMany(dataSource => dataSource.Endpoints)];
}
