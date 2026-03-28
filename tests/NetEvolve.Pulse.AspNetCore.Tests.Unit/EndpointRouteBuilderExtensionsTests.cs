namespace NetEvolve.Pulse.AspNetCore.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using PulseEndpoints = global::NetEvolve.Pulse.EndpointRouteBuilderExtensions;

public sealed class EndpointRouteBuilderExtensionsTests
{
    [Test]
    public void MapCommand_WithResponseAndNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapCommand<TestCommand, string>(null!, "/test"));

    [Test]
    public async Task MapCommand_WithResponseAndNullPattern_ThrowsArgumentNullException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentNullException>(() =>
            PulseEndpoints.MapCommand<TestCommand, string>(endpoints, null!)
        );
    }

    [Test]
    public async Task MapCommand_WithResponse_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test");

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public void MapCommand_VoidAndNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapCommand<VoidTestCommand>(null!, "/test"));

    [Test]
    public async Task MapCommand_VoidAndNullPattern_ThrowsArgumentNullException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, null!));
    }

    [Test]
    public async Task MapCommand_Void_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test");

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public void MapQuery_WithNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapQuery<TestQuery, string>(null!, "/test"));

    [Test]
    public async Task MapQuery_WithNullPattern_ThrowsArgumentNullException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapQuery<TestQuery, string>(endpoints, null!));
    }

    [Test]
    public async Task MapQuery_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapQuery<TestQuery, string>(endpoints, "/test");

        _ = await Assert.That(builder).IsNotNull();
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
}
