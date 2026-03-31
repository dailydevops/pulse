namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using PulseEndpoints = EndpointRouteBuilderExtensions;

public sealed class EndpointRouteBuilderExtensionsTests
{
    // Represents an undefined CommandHttpMethod value used to verify validation behaviour.
    private const CommandHttpMethod UndefinedHttpMethod = (CommandHttpMethod)99;

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
    public async Task MapCommand_WithResponse_WithUndefinedMethod_ThrowsArgumentOutOfRangeException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", UndefinedHttpMethod)
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

        var builder = PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", CommandHttpMethod.Put);

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_WithResponse_WithPatchMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", CommandHttpMethod.Patch);

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_WithResponse_WithDeleteMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<TestCommand, string>(endpoints, "/test", CommandHttpMethod.Delete);

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
    public async Task MapCommand_Void_WithUndefinedMethod_ThrowsArgumentOutOfRangeException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test", UndefinedHttpMethod)
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

        var builder = PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test", CommandHttpMethod.Delete);

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_Void_WithPutMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = PulseEndpoints.MapCommand<VoidTestCommand>(endpoints, "/test", CommandHttpMethod.Put);

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
