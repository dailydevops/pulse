namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Collections.Generic;
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
using NetEvolve.Pulse.Extensibility.DeadLetter;
using TUnit.Core;
using PulseEndpoints = CommandDeadLetterInspectorEndpoints;

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
        _ = mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(entries);

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
        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CommandDeadLetterEntry>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/entries?count=7", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.GetPendingAsync(7, 0, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    // POST {base}/entries/{id:guid}/replay

    [Test]
    public async Task ReplayEntry_ReturnsNoContent(CancellationToken cancellationToken)
    {
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
        mock.GetEntryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    // POST {base}/entries/{id:guid}/dismiss

    [Test]
    public async Task DismissEntry_ReturnsNoContent(CancellationToken cancellationToken)
    {
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

    // GET {base}/entries — defaults when no query parameters are supplied

    [Test]
    public async Task GetPendingEntries_WithoutQueryParameters_UsesDefaults(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CommandDeadLetterEntry>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/entries", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.GetPendingAsync(50, 0, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    // GET {base}/entries — respects the count and skip query parameters

    [Test]
    public async Task GetPendingEntries_WithCountAndSkipQueryParameters_PassesBothThrough(
        CancellationToken cancellationToken
    )
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CommandDeadLetterEntry>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/entries?count=7&skip=3", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.GetPendingAsync(7, 3, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    // GET {base}/entries — rejects a negative skip

    [Test]
    public async Task GetPendingEntries_WithNegativeSkip_ReturnsBadRequest(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/entries?skip=-1", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    // GET {base}/entries/{id:guid}

    [Test]
    public async Task GetEntry_WhenFound_ReturnsOkWithEntry(CancellationToken cancellationToken)
    {
        var entryId = Guid.NewGuid();
        var entry = new CommandDeadLetterEntry
        {
            Id = entryId,
            CommandType = typeof(string).AssemblyQualifiedName!,
            Payload = "{}",
            Status = CommandDeadLetterStatus.New,
        };

        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetEntryAsync(entryId, Arg.Any<CancellationToken>()).Returns(entry);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/commands/entries/{entryId}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await response
            .Content.ReadFromJsonAsync<CommandDeadLetterEntry>(cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(payload).IsNotNull();
        _ = await Assert.That(payload!.Id).IsEqualTo(entryId);
    }

    // GET {base}/entries/{id:guid} — unknown entry

    [Test]
    public async Task GetEntry_WhenNotFound_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetEntryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CommandDeadLetterEntry?)null);

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/commands/entries/{Guid.NewGuid()}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // GET {base}/entries/{id:guid} — route constraint rejects non-guid ids

    [Test]
    public async Task GetEntry_WithNonGuidId_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/entries/not-a-guid", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        mock.GetEntryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    // GET {base}/entries — rejects invalid paging input

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task GetPendingEntries_WithNonPositiveCount_ReturnsBadRequest(
        int count,
        CancellationToken cancellationToken
    )
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri($"/pulse/commands/entries?count={count}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    // GET {base}/entries — rejects a count above the upper bound

    [Test]
    public async Task GetPendingEntries_WithCountAboveMaximum_ReturnsBadRequest(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/entries?count=1001", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    // GET {base}/entries — accepts the maximum count

    [Test]
    public async Task GetPendingEntries_WithMaximumCount_PassesThrough(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.GetPendingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CommandDeadLetterEntry>());

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .GetAsync(new Uri("/pulse/commands/entries?count=1000", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        mock.GetPendingAsync(1000, 0, Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    // POST {base}/entries/{id:guid}/replay — unknown entry

    [Test]
    public async Task ReplayEntry_WhenEntryNotFound_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.ReplayAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Throws<CommandDeadLetterEntryNotFoundException>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri($"/pulse/commands/entries/{Guid.NewGuid()}/replay", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        mock.GetEntryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    // POST {base}/entries/{id:guid}/replay — handler failure is not reported as a missing entry

    [Test]
    public async Task ReplayEntry_WhenHandlerThrowsKeyNotFoundException_DoesNotReturnNotFound(
        CancellationToken cancellationToken
    )
    {
        var entryId = Guid.NewGuid();

        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.ReplayAsync(entryId, Arg.Any<CancellationToken>()).Throws<KeyNotFoundException>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        _ = await Assert
            .That(async () =>
                await client
                    .PostAsync(
                        new Uri($"/pulse/commands/entries/{entryId}/replay", UriKind.Relative),
                        content: null,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            .Throws<KeyNotFoundException>();
    }

    // POST {base}/entries/{id:guid}/dismiss — unknown entry

    [Test]
    public async Task DismissEntry_WhenEntryNotFound_ReturnsNotFound(CancellationToken cancellationToken)
    {
        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.DismissAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Throws<CommandDeadLetterEntryNotFoundException>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        using var response = await client
            .PostAsync(
                new Uri($"/pulse/commands/entries/{Guid.NewGuid()}/dismiss", UriKind.Relative),
                content: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // POST {base}/entries/{id:guid}/dismiss — unrelated KeyNotFoundException is not reported as a missing entry

    [Test]
    public async Task DismissEntry_WhenPlainKeyNotFoundException_DoesNotReturnNotFound(
        CancellationToken cancellationToken
    )
    {
        var entryId = Guid.NewGuid();

        var mock = Mock.Of<ICommandDeadLetterManagement>();
        _ = mock.DismissAsync(entryId, Arg.Any<CancellationToken>()).Throws<KeyNotFoundException>();

        using var host = await CreateTestHostAsync(mock.Object, null, cancellationToken).ConfigureAwait(false);
        var client = host.GetTestClient();

        _ = await Assert
            .That(async () =>
                await client
                    .PostAsync(
                        new Uri($"/pulse/commands/entries/{entryId}/dismiss", UriKind.Relative),
                        content: null,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            .Throws<KeyNotFoundException>();
    }

    // MapCommandDeadLetterInspector — custom BasePath applied correctly

    [Test]
    public async Task MapCommandDeadLetterInspector_WithCustomBasePath_UsesConfiguredPrefix(
        CancellationToken cancellationToken
    )
    {
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
