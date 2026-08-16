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
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using TUnit.Core;
using PulseEndpoints = NetEvolve.Pulse.DeadLetter.CommandDeadLetterInspectorEndpoints;

[TestGroup("AspNetCore")]
public sealed class CommandDeadLetterInspectorEndpointsTests
{
    // MapCommandDeadLetterInspector — null-argument guard

    [Test]
    public void MapCommandDeadLetterInspector_WithNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapCommandDeadLetterInspector(null!));

    // MapCommandDeadLetterInspector — default registration

    [Test]
    public async Task MapCommandDeadLetterInspector_ReturnsEndpointConventionBuilder()
    {
        var endpoints = WebApplication.CreateBuilder().Build();
        await using (endpoints.ConfigureAwait(false))
        {
            var builder = endpoints.MapCommandDeadLetterInspector();

            _ = await Assert.That(builder).IsNotNull();
        }
    }

    // GET {base}/stats

    [Test]
    public async Task GetStatistics_ReturnsOkWithMockedStatistics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var statistics = new CommandDeadLetterStatistics(
            NewCount: 1,
            ReplayingCount: 2,
            ResolvedCount: 3,
            DismissedCount: 4
        );

        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetStatisticsAsync(Arg.Any<CancellationToken>()).Returns(statistics);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/stats", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<CommandDeadLetterStatistics>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.NewCount).IsEqualTo(1);
        _ = await Assert.That(payload.ReplayingCount).IsEqualTo(2);
        _ = await Assert.That(payload.ResolvedCount).IsEqualTo(3);
        _ = await Assert.That(payload.DismissedCount).IsEqualTo(4);
    }

    // GET {base}/entries

    [Test]
    public async Task GetPendingEntries_ReturnsOkWithMockedList(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entryId = Guid.NewGuid();
        var entries = new[]
        {
            new CommandDeadLetterEntry
            {
                Id = entryId,
                CommandType = typeof(string).AssemblyQualifiedName!,
                Payload = "{}",
                Status = CommandDeadLetterStatus.New,
            },
        };

        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(entries);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/entries", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<CommandDeadLetterEntry[]>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.Length).IsEqualTo(1);
        _ = await Assert.That(payload[0].Id).IsEqualTo(entryId);
    }

    // GET {base}/entries — respects the count query parameter

    [Test]
    public async Task GetPendingEntries_WithCountQueryParameter_PassesCountThrough(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CommandDeadLetterEntry>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/entries?count=7", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.GetPendingAsync(7, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    // POST {base}/entries/{id:guid}/replay

    [Test]
    public async Task ReplayEntry_ReturnsNoContent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entryId = Guid.NewGuid();

        var mock = Mock.Of<ICommandDeadLetterManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri($"/pulse/commands/entries/{entryId}/replay", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        mock.ReplayAsync(entryId, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    // POST {base}/entries/{id:guid}/dismiss

    [Test]
    public async Task DismissEntry_ReturnsNoContent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entryId = Guid.NewGuid();

        var mock = Mock.Of<ICommandDeadLetterManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri($"/pulse/commands/entries/{entryId}/dismiss", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        mock.DismissAsync(entryId, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    // MapCommandDeadLetterInspector — custom BasePath applied correctly

    [Test]
    public async Task MapCommandDeadLetterInspector_WithCustomBasePath_UsesConfiguredPrefix(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetStatisticsAsync(Arg.Any<CancellationToken>()).Returns(new CommandDeadLetterStatistics(0, 0, 0, 0));

        using var host = await CreateTestHostAsync(
                mock.Object,
                options => options.BasePath = "/admin/commands",
                cancellationToken
            )
            .ConfigureAwait(false);
        var client = host.GetTestClient();

        using var defaultPathResponse = await client
            .GetAsync(new Uri("/pulse/commands/stats", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(defaultPathResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        using var customPathResponse = await client
            .GetAsync(new Uri("/admin/commands/stats", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(customPathResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<IHost> CreateTestHostAsync(
        ICommandDeadLetterManagement commandDeadLetterManagement,
        Action<CommandDeadLetterInspectorOptions>? configure,
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
                    _ = services.AddSingleton(commandDeadLetterManagement);
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints => endpoints.MapCommandDeadLetterInspector(configure));
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }
}
