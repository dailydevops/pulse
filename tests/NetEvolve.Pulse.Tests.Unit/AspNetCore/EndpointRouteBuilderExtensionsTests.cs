namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using PulseEndpoints = EndpointRouteBuilderExtensions;

[TestGroup("AspNetCore")]
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

        _ = Assert.Throws<ArgumentNullException>(() => endpoints.MapCommand<TestCommand, string>(null!));
    }

    // MapCommand<TCommand, TResponse> — httpMethod validation

    [Test]
    public async Task MapCommand_WithResponse_WithUndefinedMethod_ThrowsArgumentOutOfRangeException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            endpoints.MapCommand<TestCommand, string>("/test", UndefinedHttpMethod)
        );
    }

    // MapCommand<TCommand, TResponse> — valid cases

    [Test]
    public async Task MapCommand_WithResponse_DefaultPost_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = endpoints.MapCommand<TestCommand, string>("/test");

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_WithResponse_WithPutMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = endpoints.MapCommand<TestCommand, string>("/test", CommandHttpMethod.Put);

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_WithResponse_WithPatchMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = endpoints.MapCommand<TestCommand, string>("/test", CommandHttpMethod.Patch);

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_WithResponse_WithDeleteMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = endpoints.MapCommand<TestCommand, string>("/test", CommandHttpMethod.Delete);

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

        _ = Assert.Throws<ArgumentNullException>(() => endpoints.MapCommand<VoidTestCommand>(null!));
    }

    // MapCommand<TCommand> (void) — httpMethod validation

    [Test]
    public async Task MapCommand_Void_WithUndefinedMethod_ThrowsArgumentOutOfRangeException()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            endpoints.MapCommand<VoidTestCommand>("/test", UndefinedHttpMethod)
        );
    }

    // MapCommand<TCommand> (void) — valid cases

    [Test]
    public async Task MapCommand_Void_DefaultPost_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = endpoints.MapCommand<VoidTestCommand>("/test");

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_Void_WithDeleteMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = endpoints.MapCommand<VoidTestCommand>("/test", CommandHttpMethod.Delete);

        _ = await Assert.That(builder).IsNotNull();
    }

    [Test]
    public async Task MapCommand_Void_WithPutMethod_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = endpoints.MapCommand<VoidTestCommand>("/test", CommandHttpMethod.Put);

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

        _ = Assert.Throws<ArgumentNullException>(() => endpoints.MapQuery<TestQuery, string>(null!));
    }

    [Test]
    public async Task MapQuery_ReturnsRouteHandlerBuilder()
    {
        await using var endpoints = WebApplication.CreateBuilder().Build();

        var builder = endpoints.MapQuery<TestQuery, string>("/test");

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
