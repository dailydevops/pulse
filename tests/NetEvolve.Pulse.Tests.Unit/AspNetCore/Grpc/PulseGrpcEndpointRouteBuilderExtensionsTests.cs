namespace NetEvolve.Pulse.Tests.Unit.AspNetCore.Grpc;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("AspNetCore")]
public sealed class PulseGrpcEndpointRouteBuilderExtensionsTests
{
    [Test]
    public void MapStreamQueryGrpc_WithNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() =>
            PulseGrpcEndpointRouteBuilderExtensions.MapStreamQueryGrpc<TestStreamService>(null!)
        );

    [Test]
    public async Task MapStreamQueryGrpc_ReturnsConventionBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.Services.AddGrpc();
        var app = builder.Build();
        await using (app.ConfigureAwait(false))
        {
            var conventionBuilder = app.MapStreamQueryGrpc<TestStreamService>();

            _ = await Assert.That(conventionBuilder).IsNotNull();
        }
    }

    [Test]
    public async Task MapStreamQueryGrpc_RegistersServerStreamingRoute()
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.Services.AddGrpc();
        var app = builder.Build();
        await using (app.ConfigureAwait(false))
        {
            _ = app.MapStreamQueryGrpc<TestStreamService>();

            var routes = ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(endpoint => endpoint.RoutePattern.RawText)
                .ToArray();

            _ = await Assert
                .That(routes)
                .Contains($"/{TestStreamService.ServiceName}/{nameof(TestStreamService.Stream)}");
        }
    }

    [Test]
    public async Task MapStreamQueryGrpc_ServerStreamingCall_ReceivesAllItemsInOrder(
        CancellationToken cancellationToken
    )
    {
        using var host = await CreateTestHostAsync(YieldAsync(["first", "second", "third"]), cancellationToken)
            .ConfigureAwait(false);
        using var channel = CreateChannel(host);

        using var call = channel
            .CreateCallInvoker()
            .AsyncServerStreamingCall(
                TestStreamService.StreamMethod,
                null,
                new CallOptions(cancellationToken: cancellationToken),
                new TestStreamQuery()
            );

        var received = new List<string>();
        await foreach (var item in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            received.Add(item);
        }

        _ = await Assert.That(received).IsEquivalentTo(["first", "second", "third"]);
    }

    [Test]
    public async Task MapStreamQueryGrpc_WhenClientCancels_StopsStream(CancellationToken cancellationToken)
    {
        var serverStreamEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = await CreateTestHostAsync(
                YieldForeverAsync(serverStreamEnded, CancellationToken.None),
                cancellationToken
            )
            .ConfigureAwait(false);
        using var channel = CreateChannel(host);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        using var call = channel
            .CreateCallInvoker()
            .AsyncServerStreamingCall(
                TestStreamService.StreamMethod,
                null,
                new CallOptions(cancellationToken: cts.Token),
                new TestStreamQuery()
            );

        _ = await Assert.That(await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false)).IsTrue();
        await cts.CancelAsync().ConfigureAwait(false);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                _ = call.ResponseStream.Current;
            }
        });

        _ = await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.Cancelled);
        await serverStreamEnded.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task MapStreamQueryGrpc_TrimmingAnnotation_CoversMapGrpcServiceRequirements()
    {
        var required = GetDynamicallyAccessedMemberTypes(
            typeof(GrpcEndpointRouteBuilderExtensions)
                .GetMethods()
                .Single(method =>
                    method.Name == nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService)
                    && method.IsGenericMethodDefinition
                )
        );
        var declared = GetDynamicallyAccessedMemberTypes(
            typeof(PulseGrpcEndpointRouteBuilderExtensions).GetMethod(
                nameof(PulseGrpcEndpointRouteBuilderExtensions.MapStreamQueryGrpc)
            )!
        );

        _ = await Assert.That(declared & required).IsEqualTo(required);
    }

    private static DynamicallyAccessedMemberTypes GetDynamicallyAccessedMemberTypes(MethodInfo method) =>
        method
            .GetGenericArguments()[0]
            .GetCustomAttributes<DynamicallyAccessedMembersAttribute>()
            .Aggregate(DynamicallyAccessedMemberTypes.None, (all, attribute) => all | attribute.MemberTypes);

    private static GrpcChannel CreateChannel(IHost host)
    {
        var server = host.GetTestServer();
        return GrpcChannel.ForAddress(
            server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = server.CreateHandler() }
        );
    }

    private static async Task<IHost> CreateTestHostAsync(
        IAsyncEnumerable<string> items,
        CancellationToken cancellationToken
    )
    {
        var handler = Mock.Of<IStreamQueryHandler<TestStreamQuery, string>>();
        _ = handler.HandleAsync(Arg.Any<TestStreamQuery>(), Arg.Any<CancellationToken>()).Returns(items);

        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                _ = webBuilder.UseTestServer();
                _ = webBuilder.ConfigureServices(services =>
                {
                    _ = services.AddRouting();
                    _ = services.AddGrpc();
                    _ = services.AddSingleton(handler.Object);
                    _ = services.AddPulse(_ => { });
                });
                _ = webBuilder.Configure(app =>
                {
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints => endpoints.MapStreamQueryGrpc<TestStreamService>());
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

#pragma warning disable CS1998 // Async iterator without await is intentional.
    private static async IAsyncEnumerable<string> YieldAsync(string[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
    }
#pragma warning restore CS1998

    private static async IAsyncEnumerable<string> YieldForeverAsync(
        TaskCompletionSource ended,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        try
        {
            var i = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                yield return $"item-{i++}";
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _ = ended.TrySetResult();
        }
    }
}
