namespace NetEvolve.Pulse.Tests.Unit.DeadLetter;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using TUnit.Core;

[TestGroup("DeadLetter")]
public sealed class CommandDeadLetterReplayDispatcherTests
{
    [Test]
    public async Task ReplayAsync_WithVoidCommand_DispatchesToHandler(CancellationToken cancellationToken)
    {
        var handler = new VoidCommandHandler();

        await ReplayAsync<VoidCommand, Extensibility.Void>(
                handler,
                new VoidCommand { Value = "void" },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(handler.LastValue).IsEqualTo("void");
    }

    [Test]
    public async Task ReplayAsync_WithValueTypeResponse_DispatchesToHandler(CancellationToken cancellationToken)
    {
        var handler = new ValueTypeCommandHandler();

        await ReplayAsync<ValueTypeCommand, int>(handler, new ValueTypeCommand { Value = 21 }, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(handler.LastValue).IsEqualTo(21);
    }

    [Test]
    public async Task ReplayAsync_WhenHandlerThrows_RethrowsOriginalException(CancellationToken cancellationToken)
    {
        var handler = new ThrowingCommandHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ReplayAsync<ThrowingCommand, string>(handler, new ThrowingCommand(), cancellationToken)
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception!.Message).IsEqualTo(ThrowingCommandHandler.ErrorMessage);
    }

    [Test]
    public async Task ReplayAsync_WithUnresolvableCommandType_ThrowsInvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var mediator = provider.GetRequiredService<IMediatorSendOnly>();
            var payloadSerializer = provider.GetRequiredService<IPayloadSerializer>();

            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await CommandDeadLetterReplayDispatcher
                    .ReplayAsync(
                        mediator,
                        payloadSerializer,
                        "Unknown.Command, Unknown.Assembly",
                        "{}",
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            );
        }
    }

    private static async Task ReplayAsync<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> handler,
        TCommand command,
        CancellationToken cancellationToken
    )
        where TCommand : ICommand<TResponse>
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        _ = services.AddScoped(_ => handler);
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var scope = provider.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();
                var payloadSerializer = scope.ServiceProvider.GetRequiredService<IPayloadSerializer>();

                await CommandDeadLetterReplayDispatcher
                    .ReplayAsync(
                        mediator,
                        payloadSerializer,
                        typeof(TCommand).AssemblyQualifiedName!,
                        payloadSerializer.Serialize(command),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
        }
    }

    private sealed class VoidCommand : ICommand
    {
        public string? Value { get; set; }

        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class VoidCommandHandler : ICommandHandler<VoidCommand, Extensibility.Void>
    {
        public string? LastValue { get; private set; }

        public Task<Extensibility.Void> HandleAsync(VoidCommand command, CancellationToken cancellationToken = default)
        {
            LastValue = command.Value;
            return Task.FromResult(Extensibility.Void.Completed);
        }
    }

    private sealed class ValueTypeCommand : ICommand<int>
    {
        public int Value { get; set; }

        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class ValueTypeCommandHandler : ICommandHandler<ValueTypeCommand, int>
    {
        public int LastValue { get; private set; }

        public Task<int> HandleAsync(ValueTypeCommand command, CancellationToken cancellationToken = default)
        {
            LastValue = command.Value;
            return Task.FromResult(command.Value * 2);
        }
    }

    private sealed class ThrowingCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class ThrowingCommandHandler : ICommandHandler<ThrowingCommand, string>
    {
        public const string ErrorMessage = "Handler failed.";

        public Task<string> HandleAsync(ThrowingCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(ErrorMessage);
    }
}
