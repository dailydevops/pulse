namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Audit;
using TUnit.Core;
using PulseEndpoints = AuditInspectorEndpoints;

[TestGroup("AspNetCore")]
public sealed class AuditInspectorEndpointsTests
{
    // MapAuditInspector — null-argument guard

    [Test]
    public void MapAuditInspector_WithNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapAuditInspector(null!));

    // MapAuditInspector — default registration

    [Test]
    public async Task MapAuditInspector_ReturnsEndpointConventionBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapAuditInspector();

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    // GET {base}/stats

    [Test]
    public async Task GetStatistics_ReturnsOkWithMockedStatistics(CancellationToken cancellationToken)
    {
        var statistics = new AuditStatistics(3, 2);

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.GetStatisticsAsync(Arg.Any<CancellationToken>()).Returns(statistics);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/stats", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<AuditStatistics>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.SuccessCount).IsEqualTo(3);
        _ = await Assert.That(payload.FailureCount).IsEqualTo(2);
        _ = await Assert.That(payload.TotalCount).IsEqualTo(5);
    }

    // GET {base}/entries — no filter

    [Test]
    public async Task GetEntries_ReturnsOkWithMockedList(CancellationToken cancellationToken)
    {
        var recordId = Guid.NewGuid();
        var records = new[]
        {
            new AuditRecord
            {
                Id = recordId,
                CommandType = "TestCommand",
                Result = AuditResult.Success,
            },
        };

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.QueryAsync(Arg.Any<AuditFilter>(), Arg.Any<CancellationToken>()).Returns(records);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<AuditRecord[]>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.Length).IsEqualTo(1);
        _ = await Assert.That(payload[0].Id).IsEqualTo(recordId);
    }

    // GET {base}/entries/{id:guid}

    [Test]
    public async Task GetEntry_WithExistingId_ReturnsOkWithRecord(CancellationToken cancellationToken)
    {
        var recordId = Guid.NewGuid();
        var record = new AuditRecord
        {
            Id = recordId,
            CommandType = "TestCommand",
            UserId = "alice",
            Result = AuditResult.Failure,
            ExceptionMessage = "boom",
        };

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.GetByIdAsync(recordId, Arg.Any<CancellationToken>()).Returns(record);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/audit/entries/{recordId}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<AuditRecord>(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        using (Assert.Multiple())
        {
            _ = await Assert.That(payload!.Id).IsEqualTo(recordId);
            _ = await Assert.That(payload.CommandType).IsEqualTo("TestCommand");
            _ = await Assert.That(payload.UserId).IsEqualTo("alice");
            _ = await Assert.That(payload.Result).IsEqualTo(AuditResult.Failure);
            _ = await Assert.That(payload.ExceptionMessage).IsEqualTo("boom");
        }

        mock.GetByIdAsync(recordId, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntry_WithUnknownId_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var recordId = Guid.NewGuid();

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AuditRecord?)null);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/audit/entries/{recordId}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        mock.GetByIdAsync(recordId, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntry_WithNonGuidId_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries/not-a-guid", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        mock.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).WasCalled(Times.Never);
    }

    // GET {base}/entries — filter parameter binding

    [Test]
    public async Task GetEntries_WithoutQuery_UsesFilterDefaults(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.QueryAsync(
                Arg.Is<AuditFilter>(f =>
                    f != null
                    && f.CommandType == null
                    && f.UserId == null
                    && f.From == null
                    && f.To == null
                    && f.Result == null
                    && f.Take == 50
                    && f.Skip == 0
                ),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntries_WithCommandTypeFilter_BindsCommandTypeOntoFilter(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?commandType=MyCommand", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.QueryAsync(
                Arg.Is<AuditFilter>(f => f != null && f.CommandType == "MyCommand"),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntries_WithUserIdFilter_BindsUserIdOntoFilter(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?userId=alice", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.QueryAsync(Arg.Is<AuditFilter>(f => f != null && f.UserId == "alice"), Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntries_WithFromFilter_BindsFromOntoFilter(CancellationToken cancellationToken)
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(
                new Uri($"/pulse/audit/entries?from={Uri.EscapeDataString(from.ToString("O"))}", UriKind.Relative),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.QueryAsync(Arg.Is<AuditFilter>(f => f != null && f.From == from), Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntries_WithToFilter_BindsToOntoFilter(CancellationToken cancellationToken)
    {
        var to = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);

        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(
                new Uri($"/pulse/audit/entries?to={Uri.EscapeDataString(to.ToString("O"))}", UriKind.Relative),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.QueryAsync(Arg.Is<AuditFilter>(f => f != null && f.To == to), Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntries_WithResultFilter_BindsResultOntoFilter(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?result=Failure", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.QueryAsync(
                Arg.Is<AuditFilter>(f => f != null && f.Result == AuditResult.Failure),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntries_WithTakeFilter_BindsTakeOntoFilter(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?take=10", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.QueryAsync(Arg.Is<AuditFilter>(f => f != null && f.Take == 10), Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntries_WithSkipFilter_BindsSkipOntoFilter(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?skip=20", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.QueryAsync(Arg.Is<AuditFilter>(f => f != null && f.Skip == 20), Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task GetEntries_WithAllFilters_BindsAllFieldsOntoFilter(CancellationToken cancellationToken)
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);

        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        var query =
            $"commandType=MyCommand&userId=alice&from={Uri.EscapeDataString(from.ToString("O"))}"
            + $"&to={Uri.EscapeDataString(to.ToString("O"))}&result=Success&take=5&skip=15";

        using var response = await client
            .GetAsync(new Uri($"/pulse/audit/entries?{query}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.QueryAsync(
                Arg.Is<AuditFilter>(f =>
                    f != null
                    && f.CommandType == "MyCommand"
                    && f.UserId == "alice"
                    && f.From == from
                    && f.To == to
                    && f.Result == AuditResult.Success
                    && f.Take == 5
                    && f.Skip == 15
                ),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
    }

    // GET {base}/entries — malformed query values

    [Test]
    [Arguments("take=abc")]
    [Arguments("skip=abc")]
    [Arguments("result=Bogus")]
    [Arguments("from=not-a-date")]
    [Arguments("to=not-a-date")]
    public async Task GetEntries_WithMalformedQueryValue_ReturnsBadRequest(
        string query,
        CancellationToken cancellationToken
    )
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/audit/entries?{query}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        mock.QueryAsync(Arg.Any<AuditFilter>(), Arg.Any<CancellationToken>()).WasCalled(Times.Never);
    }

    // GET {base}/entries — out-of-range query values

    [Test]
    [Arguments("take=0")]
    [Arguments("take=-1")]
    [Arguments("take=1001")]
    [Arguments("skip=-1")]
    [Arguments("from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    public async Task GetEntries_WithOutOfRangeQueryValue_ReturnsBadRequest(
        string query,
        CancellationToken cancellationToken
    )
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/audit/entries?{query}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        mock.QueryAsync(Arg.Any<AuditFilter>(), Arg.Any<CancellationToken>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task GetEntries_WithEqualFromAndTo_ReturnsOk(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(
                new Uri("/pulse/audit/entries?from=2026-01-01T00:00:00Z&to=2026-01-01T00:00:00Z", UriKind.Relative),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var expected = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        mock.QueryAsync(
                Arg.Is<AuditFilter>(f => f != null && f.From == expected && f.To == expected),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
    }

    // MapAuditInspector — read-only

    [Test]
    [Arguments("POST", "/pulse/audit/stats")]
    [Arguments("POST", "/pulse/audit/entries")]
    [Arguments("PUT", "/pulse/audit/entries")]
    [Arguments("DELETE", "/pulse/audit/entries")]
    [Arguments("DELETE", "/pulse/audit/entries/5b0f8f55-3f7c-4b8e-9d5c-0d7c2f0e9a11")]
    [Arguments("PUT", "/pulse/audit/entries/5b0f8f55-3f7c-4b8e-9d5c-0d7c2f0e9a11")]
    public async Task MapAuditInspector_WithMutatingMethod_ReturnsMethodNotAllowed(
        string method,
        string path,
        CancellationToken cancellationToken
    )
    {
        var mock = Mock.Of<IAuditManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    // MapAuditInspector — RouteGroupName applied as endpoint group name

    [Test]
    [Arguments(null, "Pulse Audit Inspector")]
    [Arguments("Admin Audit Inspector", "Admin Audit Inspector")]
    public async Task MapAuditInspector_AppliesRouteGroupNameToAllEndpoints(
        string? routeGroupName,
        string expectedGroupName,
        CancellationToken cancellationToken
    )
    {
        var mock = Mock.Of<IAuditManagement>();
        Action<AuditInspectorOptions>? configure = routeGroupName is null
            ? null
            : options => options.RouteGroupName = routeGroupName;

        using var host = await CreateTestHostAsync(mock.Object, configure, cancellationToken).ConfigureAwait(false);

        var groupNames = host
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.Select(e => e.Metadata.GetMetadata<IEndpointGroupNameMetadata>()?.EndpointGroupName)
            .ToArray();

        _ = await Assert.That(groupNames.Length).IsGreaterThan(0);
        _ = await Assert.That(groupNames.All(name => name == expectedGroupName)).IsTrue();
    }

    // MapAuditInspector — custom BasePath applied correctly

    [Test]
    public async Task MapAuditInspector_WithCustomBasePath_UsesConfiguredPrefix(CancellationToken cancellationToken)
    {
        var statistics = new AuditStatistics(1, 0);

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.GetStatisticsAsync(Arg.Any<CancellationToken>()).Returns(statistics);

        using var host = await CreateTestHostAsync(
                mock.Object,
                options => options.BasePath = "/admin/audit",
                cancellationToken
            )
            .ConfigureAwait(false);
        var client = host.GetTestClient();

        using var defaultPathResponse = await client
            .GetAsync(new Uri("/pulse/audit/stats", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(defaultPathResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        using var customPathResponse = await client
            .GetAsync(new Uri("/admin/audit/stats", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(customPathResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await customPathResponse
            .Content.ReadFromJsonAsync<AuditStatistics>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.SuccessCount).IsEqualTo(1);
    }

    private static async Task<IHost> CreateTestHostAsync(
        IAuditManagement auditManagement,
        Action<AuditInspectorOptions>? configure,
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
                    _ = services.AddSingleton(auditManagement);
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints => endpoints.MapAuditInspector(configure));
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }
}
