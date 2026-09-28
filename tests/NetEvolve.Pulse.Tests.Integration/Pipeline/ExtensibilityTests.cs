namespace NetEvolve.Pulse.Tests.Integration.Pipeline;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// End-to-end integration tests for <see cref="CommandBatch"/>, <see cref="MediatorSendOnlyExtensions"/>,
/// and the <see cref="IEventInProcess"/> default interface member, exercised through a real built host
/// and the real mediator rather than mocks.
/// </summary>
[TestGroup("Pipeline")]
public sealed class ExtensibilityTests
{
    [Test]
    public async Task SendBatchAsync_Executes_Commands_Sequentially_InOrder(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var order = new List<int>();

        using var host = await CreateHostAsync(services => services.AddSingleton(order), cancellationToken)
            .ConfigureAwait(false);
        using var scope = host.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();

        var batch = new CommandBatch().Add(new RecordCommand(1)).Add(new RecordCommand(2)).Add(new RecordCommand(3));

        await mediator.SendBatchAsync(batch, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(order).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task SendBatchAsync_OnFailure_StopsAndPropagatesException_SkippingRemaining(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var order = new List<int>();

        using var host = await CreateHostAsync(services => services.AddSingleton(order), cancellationToken)
            .ConfigureAwait(false);
        using var scope = host.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();

        var batch = new CommandBatch().Add(new RecordCommand(1)).Add(new FailingCommand()).Add(new RecordCommand(3));

        _ = await Assert
            .That(() => mediator.SendBatchAsync(batch, cancellationToken))
            .Throws<InvalidOperationException>();

        // The third command must never run since the batch stops at the first failure.
        _ = await Assert.That(order).IsEquivalentTo([1]);
    }

    [Test]
    public async Task CommandBatch_Add_WithNullCommand_ThrowsArgumentNullException()
    {
        var batch = new CommandBatch();

        _ = await Assert.That(() => batch.Add<RecordCommand>(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task IEventInProcess_DefaultImplementation_HandleInProcess_IsTrue()
    {
        IEventInProcess @event = new InProcessOnlyEvent();

        _ = await Assert.That(@event.HandleInProcess).IsTrue();
    }

    [Test]
    public async Task IOutboxTransactionScope_DefaultImplementation_HasActiveTransaction_ReflectsCurrentTransaction()
    {
        IOutboxTransactionScope noneActive = new TestTransactionScope(currentTransaction: null);
        IOutboxTransactionScope active = new TestTransactionScope(currentTransaction: new object());

        using (Assert.Multiple())
        {
            _ = await Assert.That(noneActive.HasActiveTransaction).IsFalse();
            _ = await Assert.That(active.HasActiveTransaction).IsTrue();
        }
    }

    private static async Task<IHost> CreateHostAsync(
        Action<IServiceCollection> configureServices,
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
                    configureServices(services);
                    _ = services.AddPulse(configurator =>
                        configurator
                            .AddCommandHandler<RecordCommand, RecordCommandHandler>()
                            .AddCommandHandler<FailingCommand, FailingCommandHandler>()
                    );
                });
                _ = webBuilder.Configure(applicationBuilder => { });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    private sealed record RecordCommand(int Value) : ICommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class RecordCommandHandler(List<int> order)
        : ICommandHandler<RecordCommand, NetEvolve.Pulse.Extensibility.Void>
    {
        public Task<NetEvolve.Pulse.Extensibility.Void> HandleAsync(
            RecordCommand command,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            order.Add(command.Value);
            return Task.FromResult<NetEvolve.Pulse.Extensibility.Void>(default);
        }
    }

    private sealed record FailingCommand : ICommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class FailingCommandHandler : ICommandHandler<FailingCommand, NetEvolve.Pulse.Extensibility.Void>
    {
        public Task<NetEvolve.Pulse.Extensibility.Void> HandleAsync(
            FailingCommand command,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Simulated failure");
    }

    private sealed record InProcessOnlyEvent : IEventInProcess
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class TestTransactionScope(object? currentTransaction) : IOutboxTransactionScope
    {
        public object? GetCurrentTransaction() => currentTransaction;
    }
}
