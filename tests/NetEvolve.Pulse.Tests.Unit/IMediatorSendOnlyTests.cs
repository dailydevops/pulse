namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

[TestGroup("IMediatorSendOnly")]
public class IMediatorSendOnlyTests
{
    [Test]
    public async Task IMediatorSendOnly_IMediator_IsAssignableTo()
    {
        var isAssignable = typeof(IMediatorSendOnly).IsAssignableFrom(typeof(IMediator));

        _ = await Assert.That(isAssignable).IsTrue();
    }

    [Test]
    public async Task IMediatorSendOnly_DoesNotDeclare_QueryAsyncOrStreamQueryAsync()
    {
        var methods = typeof(IMediatorSendOnly)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        using (Assert.Multiple())
        {
            _ = await Assert.That(methods).DoesNotContain("QueryAsync");
            _ = await Assert.That(methods).DoesNotContain("StreamQueryAsync");
        }
    }

    [Test]
    public async Task IMediatorSendOnly_DI_ResolvesSameScopedInstanceAsIMediator()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var mediatorSendOnly = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();

        _ = await Assert.That(mediatorSendOnly).IsSameReferenceAs(mediator);
    }

    [Test]
    public async Task IMediatorSendOnly_ServiceCanCall_SendAsyncAndPublishAsync(CancellationToken cancellationToken)
    {
        var handler = new TestCommandHandler("result");
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        _ = services.AddScoped<ICommandHandler<TestCommand, string>>(_ => handler);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var mediatorSendOnly = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();
        var consumer = new WriteOnlyConsumer(mediatorSendOnly);
        await consumer.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(handler.HandledCommands).HasSingleItem();
            _ = await Assert.That(consumer.EventPublished).IsTrue();
        }
    }

    private sealed class TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class TestCommandHandler : ICommandHandler<TestCommand, string>
    {
        private readonly string _result;

        public List<TestCommand> HandledCommands { get; } = [];

        public TestCommandHandler(string result) => _result = result;

        public Task<string> HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            HandledCommands.Add(command);
            return Task.FromResult(_result);
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();

        public string? CorrelationId { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class WriteOnlyConsumer
    {
        private readonly IMediatorSendOnly _mediator;

        public bool EventPublished { get; private set; }

        public WriteOnlyConsumer(IMediatorSendOnly mediator) => _mediator = mediator;

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            _ = await _mediator
                .SendAsync<TestCommand, string>(new TestCommand(), cancellationToken)
                .ConfigureAwait(false);
            await _mediator.PublishAsync(new TestEvent(), cancellationToken).ConfigureAwait(false);
            EventPublished = true;
        }
    }
}
