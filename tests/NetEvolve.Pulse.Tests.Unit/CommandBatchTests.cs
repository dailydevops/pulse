namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using Void = Extensibility.Void;

[TestGroup("CommandBatch")]
public class CommandBatchTests
{
    [Test]
    public async Task CommandBatch_Add_WithNullCommand_ThrowsArgumentNullException()
    {
        var batch = new CommandBatch();
        TestCommand? command = null;

        _ = Assert.Throws<ArgumentNullException>("command", () => _ = batch.Add(command!));
    }

    [Test]
    public async Task CommandBatch_Count_EmptyBatch_ReturnsZero()
    {
        var batch = new CommandBatch();

        _ = await Assert.That(batch.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CommandBatch_Add_SingleCommand_CountIsOne()
    {
        var batch = new CommandBatch().Add(new TestCommand());

        _ = await Assert.That(batch.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CommandBatch_Add_MultipleCommands_CountMatchesAdded()
    {
        var batch = new CommandBatch().Add(new TestCommand()).Add(new TestCommand()).Add(new TestCommand());

        _ = await Assert.That(batch.Count).IsEqualTo(3);
    }

    [Test]
    public async Task CommandBatch_Add_IsFluentAndReturnsSameInstance()
    {
        var batch = new CommandBatch();
        var returned = batch.Add(new TestCommand());

        _ = await Assert.That(returned).IsSameReferenceAs(batch);
    }

    [Test]
    public async Task SendBatchAsync_WithNullMediator_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        IMediatorSendOnly? mediator = null;
        var batch = new CommandBatch();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            "mediator",
            async () => await mediator!.SendBatchAsync(batch, cancellationToken).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendBatchAsync_WithNullBatch_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();
        CommandBatch? batch = null;

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            "batch",
            async () => await mediator.SendBatchAsync(batch!, cancellationToken).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendBatchAsync_EmptyBatch_CompletesSuccessfully(CancellationToken cancellationToken)
    {
        var handler = new TestCommandHandler();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        _ = services.AddScoped<ICommandHandler<TestCommand, Void>>(_ => handler);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();
        var batch = new CommandBatch();

        await mediator.SendBatchAsync(batch, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(handler.HandledCommands).IsEmpty();
    }

    [Test]
    public async Task SendBatchAsync_ExecutesAllCommands_InOrder(CancellationToken cancellationToken)
    {
        var executionOrder = new List<string>();
        var handler = new OrderTrackingCommandHandler(executionOrder);
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        _ = services.AddScoped<ICommandHandler<OrderedCommand, Void>>(_ => handler);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();
        var batch = new CommandBatch()
            .Add(new OrderedCommand("first"))
            .Add(new OrderedCommand("second"))
            .Add(new OrderedCommand("third"));

        await mediator.SendBatchAsync(batch, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(executionOrder.Count).IsEqualTo(3);
            _ = await Assert.That(executionOrder[0]).IsEqualTo("first");
            _ = await Assert.That(executionOrder[1]).IsEqualTo("second");
            _ = await Assert.That(executionOrder[2]).IsEqualTo("third");
        }
    }

    [Test]
    public async Task SendBatchAsync_WhenCommandThrows_StopsExecutionAndPropagatesException(
        CancellationToken cancellationToken
    )
    {
        var executionOrder = new List<string>();
        var handler = new ThrowingOrderTrackingCommandHandler(executionOrder, throwOnName: "second");
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        _ = services.AddScoped<ICommandHandler<OrderedCommand, Void>>(_ => handler);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();
        var batch = new CommandBatch()
            .Add(new OrderedCommand("first"))
            .Add(new OrderedCommand("second"))
            .Add(new OrderedCommand("third"));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendBatchAsync(batch, cancellationToken).ConfigureAwait(false)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(executionOrder.Count).IsEqualTo(2);
            _ = await Assert.That(executionOrder[0]).IsEqualTo("first");
            _ = await Assert.That(executionOrder[1]).IsEqualTo("second");
        }
    }

    private sealed class TestCommand : ICommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class TestCommandHandler : ICommandHandler<TestCommand, Void>
    {
        public List<TestCommand> HandledCommands { get; } = [];

        public Task<Void> HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            HandledCommands.Add(command);
            return Task.FromResult(Void.Completed);
        }
    }

    private sealed class OrderedCommand : ICommand
    {
        public OrderedCommand(string name) => Name = name;

        public string Name { get; }
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class OrderTrackingCommandHandler : ICommandHandler<OrderedCommand, Void>
    {
        private readonly List<string> _executionOrder;

        public OrderTrackingCommandHandler(List<string> executionOrder) => _executionOrder = executionOrder;

        public Task<Void> HandleAsync(OrderedCommand command, CancellationToken cancellationToken = default)
        {
            _executionOrder.Add(command.Name);
            return Task.FromResult(Void.Completed);
        }
    }

    private sealed class ThrowingOrderTrackingCommandHandler : ICommandHandler<OrderedCommand, Void>
    {
        private readonly List<string> _executionOrder;
        private readonly string _throwOnName;

        public ThrowingOrderTrackingCommandHandler(List<string> executionOrder, string throwOnName)
        {
            _executionOrder = executionOrder;
            _throwOnName = throwOnName;
        }

        public Task<Void> HandleAsync(OrderedCommand command, CancellationToken cancellationToken = default)
        {
            _executionOrder.Add(command.Name);

            if (command.Name == _throwOnName)
            {
                throw new InvalidOperationException($"Simulated failure on command '{command.Name}'.");
            }

            return Task.FromResult(Void.Completed);
        }
    }
}
