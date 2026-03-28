namespace NetEvolve.Pulse.AspNetCore.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using PulseEndpoints = global::NetEvolve.Pulse.EndpointRouteBuilderExtensions;

public sealed class EndpointRouteBuilderExtensionsTests
{
    // MapCommand<TCommand, TResponse> — null-argument guards

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

    // MapCommand<TCommand, TResponse> — httpMethod validation

    [Test]
    public async Task MapCommand_WithResponse_WithGetMethod_ThrowsArgumentException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentException>(() =>
            PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", "GET")
        );
    }

    [Test]
    public async Task MapCommand_WithResponse_WithGetMethodLowercase_ThrowsArgumentException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentException>(() =>
            PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", "get")
        );
    }

    [Test]
    public async Task MapCommand_WithResponse_WithEmptyMethod_ThrowsArgumentException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentException>(() =>
            PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", "")
        );
    }

    [Test]
    public async Task MapCommand_WithResponse_WithWhitespaceMethod_ThrowsArgumentException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentException>(() =>
            PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", "  ")
        );
    }

    // MapCommand<TCommand, TResponse> — valid cases

    [Test]
    public async Task MapCommand_WithResponse_DefaultPost_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test");

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_WithResponse_WithPutMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", "PUT");

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_WithResponse_WithPatchMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", "PATCH");

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_WithResponse_WithDeleteMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", "DELETE");

        _ = await Assert.That(builder).IsNotNull();
    }

    // MapCommand<TCommand> (void) — null-argument guards

    [Test]
    public void MapCommand_VoidAndNullEndpoints_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapCommand<VoidTestCommand>(null!, "/test"));

    [Test]
    public async Task MapCommand_VoidAndNullPattern_ThrowsArgumentNullException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentNullException>(() => PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, null!));
    }

    // MapCommand<TCommand> (void) — httpMethod validation

    [Test]
    public async Task MapCommand_Void_WithGetMethod_ThrowsArgumentException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentException>(() =>
            PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test", "GET")
        );
    }

    [Test]
    public async Task MapCommand_Void_WithGetMethodLowercase_ThrowsArgumentException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentException>(() =>
            PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test", "get")
        );
    }

    [Test]
    public async Task MapCommand_Void_WithEmptyMethod_ThrowsArgumentException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentException>(() => PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test", ""));
    }

    [Test]
    public async Task MapCommand_Void_WithWhitespaceMethod_ThrowsArgumentException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentException>(() =>
            PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test", "  ")
        );
    }

    // MapCommand<TCommand> (void) — valid cases

    [Test]
    public async Task MapCommand_Void_DefaultPost_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test");

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_Void_WithDeleteMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test", "DELETE");

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_Void_WithPutMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test", "PUT");

        _ = await Assert.That(builder).IsNotNull();
    }

    // MapQuery — null-argument guards

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
