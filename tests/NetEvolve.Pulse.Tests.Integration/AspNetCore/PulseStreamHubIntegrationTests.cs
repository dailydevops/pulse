namespace NetEvolve.Pulse.Tests.Integration.AspNetCore;

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions.Enums;

/// <summary>
/// End-to-end integration tests for <see cref="PulseStreamHub{TQuery, TResponse}"/> mapped via
/// <c>MapStreamQueryHub</c>:
/// a real SignalR <see cref="HubConnection"/> talks to the hub over a <see cref="TestServer"/>, so hub method
/// discovery on the closed generic hub, query payload binding and cancellation token injection are exercised
/// through the SignalR dispatcher.
/// </summary>
[TestGroup("AspNetCore")]
public sealed class PulseStreamHubIntegrationTests
{
    private const string HubPath = "/hubs/numbers";

    [Test]
    public async Task StreamAsync_ThroughSignalR_StreamsAllItemsInOrder(CancellationToken cancellationToken)
    {
        using var host = await CreateHostAsync(cancellationToken).ConfigureAwait(false);
        var connection = await ConnectAsync(host, cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var items = new List<int>();
            await foreach (
                var item in connection
                    .StreamAsync<int>("StreamAsync", new CountingStreamQuery(3), cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                items.Add(item);
            }

            _ = await Assert.That(items).IsEquivalentTo([0, 1, 2], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task StreamAsync_ThroughSignalR_ClientCancellationStopsHandler(CancellationToken cancellationToken)
    {
        var probe = new CancellationProbe();
        using var host = await CreateHostAsync(cancellationToken, probe).ConfigureAwait(false);
        var connection = await ConnectAsync(host, cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var received = new List<int>();
            Exception? clientException = null;
            try
            {
                await foreach (
                    var item in connection
                        .StreamAsync<int>("StreamAsync", new CountingStreamQuery(null), cts.Token)
                        .ConfigureAwait(false)
                )
                {
                    received.Add(item);
                    if (received.Count == 2)
                    {
                        await cts.CancelAsync().ConfigureAwait(false);
                    }
                    else if (received.Count > 1000)
                    {
                        // Guard: fail with an assertion instead of streaming forever.
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected: the client surfaces its own cancellation.
            }
            catch (Exception ex)
            {
                clientException = ex;
            }

            var handlerCancelled = await probe
                .Exited.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(clientException).IsNull();
            _ = await Assert.That(received.Take(2)).IsEquivalentTo([0, 1], CollectionOrdering.Matching);
            _ = await Assert.That(handlerCancelled).IsTrue();

            // The connection stays usable after the stream was cancelled.
            var items = new List<int>();
            await foreach (
                var item in connection
                    .StreamAsync<int>("StreamAsync", new CountingStreamQuery(2), cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                items.Add(item);
            }

            _ = await Assert.That(items).IsEquivalentTo([0, 1], CollectionOrdering.Matching);
        }
    }

    private static async Task<HubConnection> ConnectAsync(IHost host, CancellationToken cancellationToken)
    {
        var server = host.GetTestServer();
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, HubPath),
                options => options.HttpMessageHandlerFactory = _ => server.CreateHandler()
            )
            .Build();

        await connection.StartAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<IHost> CreateHostAsync(
        CancellationToken cancellationToken,
        CancellationProbe? probe = null
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                _ = webBuilder.UseTestServer();
                _ = webBuilder.ConfigureServices(services =>
                {
                    _ = services.AddRouting();
                    _ = services.AddSignalR();
                    _ = services.AddSingleton(probe ?? new CancellationProbe());
                    _ = services.AddPulse(mediator =>
                        mediator.AddStreamQueryHandler<CountingStreamQuery, int, CountingStreamQueryHandler>()
                    );
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints => endpoints.MapStreamQueryHub<CountingStreamQuery, int>(HubPath));
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    /// <summary>Streams <c>0..Count-1</c>, or forever when <see cref="Count"/> is <see langword="null"/>.</summary>
    internal sealed record CountingStreamQuery(int? Count) : IStreamQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    internal sealed class CancellationProbe
    {
        /// <summary>
        /// Completed when the handler's enumeration ends; the result is whether its cancellation token was
        /// cancelled at that point.
        /// </summary>
        /// <remarks>
        /// Deliberately signalled from a <see langword="finally"/> block instead of a
        /// <see cref="CancellationToken.Register(Action)"/> callback: the handler unwinds on a thread-pool
        /// thread (e.g. the cancelled <see cref="Task.Delay(int, CancellationToken)"/> continuation) while
        /// <see cref="CancellationTokenSource.Cancel()"/> is still walking its callback list on the SignalR
        /// receive loop. Disposing a registration whose callback has not been invoked yet unregisters it, so
        /// a callback-based probe can be skipped even though the token was cancelled.
        /// </remarks>
        public TaskCompletionSource<bool> Exited { get; } =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class CountingStreamQueryHandler(CancellationProbe probe)
        : IStreamQueryHandler<CountingStreamQuery, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            CountingStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            try
            {
                for (var i = 0; request.Count is null || i < request.Count; i++)
                {
                    yield return i;
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _ = probe.Exited.TrySetResult(cancellationToken.IsCancellationRequested);
            }
        }
    }
}
