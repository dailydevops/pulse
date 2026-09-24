namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;
using TUnit.Core;
using PulseEndpoints = NetEvolve.Pulse.Audit.AuditInspectorEndpoints;

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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

    // GET {base}/entries — filter parameter binding

    [Test]
    public async Task GetEntries_WithCommandTypeFilter_BindsCommandTypeOntoFilter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.QueryAsync(Arg.Is<AuditFilter>(f => f?.CommandType == "MyCommand"), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AuditRecord>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?commandType=MyCommand", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetEntries_WithUserIdFilter_BindsUserIdOntoFilter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.QueryAsync(Arg.Is<AuditFilter>(f => f?.UserId == "user-42"), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AuditRecord>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?userId=user-42", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetEntries_WithFromFilter_BindsFromOntoFilter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.QueryAsync(Arg.Is<AuditFilter>(f => f?.From == from), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AuditRecord>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(
                new Uri($"/pulse/audit/entries?from={Uri.EscapeDataString(from.ToString("O"))}", UriKind.Relative),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetEntries_WithToFilter_BindsToOntoFilter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var to = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.QueryAsync(Arg.Is<AuditFilter>(f => f?.To == to), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AuditRecord>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(
                new Uri($"/pulse/audit/entries?to={Uri.EscapeDataString(to.ToString("O"))}", UriKind.Relative),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetEntries_WithResultFilter_BindsResultOntoFilter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.QueryAsync(Arg.Is<AuditFilter>(f => f?.Result == AuditResult.Failure), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AuditRecord>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?result=Failure", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetEntries_WithTakeFilter_BindsTakeOntoFilter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.QueryAsync(Arg.Is<AuditFilter>(f => f?.Take == 10), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AuditRecord>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?take=10", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetEntries_WithSkipFilter_BindsSkipOntoFilter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mock = Mock.Of<IAuditManagement>();
        _ = mock.QueryAsync(Arg.Is<AuditFilter>(f => f?.Skip == 20), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AuditRecord>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/audit/entries?skip=20", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // MapAuditInspector — custom BasePath applied correctly

    [Test]
    public async Task MapAuditInspector_WithCustomBasePath_UsesConfiguredPrefix(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
