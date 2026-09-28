namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Serialization;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[SuppressMessage(
    "IDisposableAnalyzers.Correctness",
    "CA2000:Dispose objects before losing scope",
    Justification = "ServiceProvider instances are short-lived within test methods"
)]
[TestGroup("Interceptors")]
public sealed class CommandDeadLetterInterceptorTests
{
    private static IPayloadSerializer DefaultSerializer =>
        new SystemTextJsonPayloadSerializer(Options.Create(JsonSerializerOptions.Default));

    [Test]
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new CommandDeadLetterInterceptor<TestCommand, string>(null!, DefaultSerializer))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NullPayloadSerializer_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new CommandDeadLetterInterceptor<TestCommand, string>(
                    new ServiceCollection().BuildServiceProvider(),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task HandleAsync_FailingCommandWithStoreRegistered_StoresEntryAndRethrows(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = new FakeCommandDeadLetterStore();
        var services = new ServiceCollection();
        _ = services.AddSingleton<ICommandDeadLetterStore>(store);
        var provider = services.BuildServiceProvider();
        var interceptor = new CommandDeadLetterInterceptor<TestCommand, string>(provider, DefaultSerializer);
        var command = new TestCommand { Value = "payload-value" };
        var thrown = new InvalidOperationException("handler failed");

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(command, (_, _) => Task.FromException<string>(thrown), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(store.StoreCallCount).IsEqualTo(1);
            _ = await Assert.That(store.LastCommandType).IsEqualTo(typeof(TestCommand).AssemblyQualifiedName);
            _ = await Assert.That(store.LastPayload).Contains("payload-value");
            _ = await Assert.That(store.LastException).IsSameReferenceAs(thrown);
        }
    }

    [Test]
    public async Task HandleAsync_StoreThrows_RethrowsOriginalExceptionAndLogsFailure(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storeException = new InvalidCastException("store unreachable");
        var store = Mock.Of<ICommandDeadLetterStore>();
        _ = store
            .StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .Throws(storeException);
        var logger = Mock.Logger<CommandDeadLetterInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(store.Object, DefaultSerializer, logger);
        var thrown = new InvalidOperationException("handler failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(
                    new TestCommand { Value = "x" },
                    (_, _) => Task.FromException<string>(thrown),
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        var errors = logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToList();
        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsSameReferenceAs(thrown);
            _ = await Assert.That(errors.Count).IsEqualTo(1);
            _ = await Assert.That(errors[0].Exception).IsSameReferenceAs(storeException);
            _ = await Assert.That(errors[0].Message).Contains(typeof(TestCommand).FullName!);
        }
    }

    [Test]
    public async Task HandleAsync_SerializerThrows_RethrowsOriginalExceptionAndLogsFailure(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serializerException = new NotSupportedException("no JsonTypeInfo");
        var serializer = Mock.Of<IPayloadSerializer>();
        _ = serializer.Serialize(Arg.Any<TestCommand>()).Throws(serializerException);
        var store = Mock.Of<ICommandDeadLetterStore>();
        var logger = Mock.Logger<CommandDeadLetterInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(store.Object, serializer.Object, logger);
        var thrown = new InvalidOperationException("handler failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(
                    new TestCommand { Value = "x" },
                    (_, _) => Task.FromException<string>(thrown),
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        var errors = logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToList();
        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsSameReferenceAs(thrown);
            _ = await Assert.That(errors.Count).IsEqualTo(1);
            _ = await Assert.That(errors[0].Exception).IsSameReferenceAs(serializerException);
        }

        store
            .StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .WasCalled(Times.Never);
    }

    [Test]
    public async Task HandleAsync_StoreThrows_PreservesOriginalStackTrace(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = Mock.Of<ICommandDeadLetterStore>();
        _ = store
            .StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidCastException("store unreachable"));
        var interceptor = CreateInterceptor(
            store.Object,
            DefaultSerializer,
            Mock.Logger<CommandDeadLetterInterceptor<TestCommand, string>>()
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(new TestCommand { Value = "x" }, (_, _) => ThrowingHandlerAsync(), cancellationToken)
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception!.StackTrace).Contains(nameof(ThrowingHandlerAsync));
    }

    [Test]
    public async Task HandleAsync_SuccessfulCommand_NeverCallsStoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = new FakeCommandDeadLetterStore();
        var services = new ServiceCollection();
        _ = services.AddSingleton<ICommandDeadLetterStore>(store);
        var provider = services.BuildServiceProvider();
        var interceptor = new CommandDeadLetterInterceptor<TestCommand, string>(provider, DefaultSerializer);
        var command = new TestCommand { Value = "ok" };

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(store.StoreCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task HandleAsync_NoStoreRegistered_FailingCommandStillRethrowsWithoutError(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new CommandDeadLetterInterceptor<TestCommand, string>(provider, DefaultSerializer);
        var command = new TestCommand { Value = "no-store" };
        var thrown = new InvalidOperationException("handler failed without store");

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(command, (_, _) => Task.FromException<string>(thrown), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task HandleAsync_FailingQuery_IsNotInterceptedAndStoreIsNeverCalled(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = new FakeCommandDeadLetterStore();
        var services = new ServiceCollection();
        _ = services.AddSingleton<ICommandDeadLetterStore>(store);
        var provider = services.BuildServiceProvider();
        var interceptor = new CommandDeadLetterInterceptor<TestQuery, string>(provider, DefaultSerializer);
        var query = new TestQuery();
        var thrown = new InvalidOperationException("query handler failed");

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(query, (_, _) => Task.FromException<string>(thrown), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        _ = await Assert.That(store.StoreCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task HandleAsync_FailingQuery_NoStoreRegistered_StillRethrows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new CommandDeadLetterInterceptor<TestQuery, string>(provider, DefaultSerializer);
        var query = new TestQuery();
        var thrown = new InvalidOperationException("query handler failed without store");

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(query, (_, _) => Task.FromException<string>(thrown), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task HandleAsync_FailingReplayedCommand_SkipsStoreAndRethrows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = Mock.Of<ICommandDeadLetterStore>();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse(builder => builder.AddCommandDeadLetter());
        _ = services.AddSingleton<ICommandDeadLetterStore>(store.Object);
        _ = services.AddScoped<ICommandHandler<TestCommand, string>, FailingTestCommandHandler>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ReplayAsync(provider, new TestCommand { Value = "replayed" }, cancellationToken)
                    .ConfigureAwait(false)
            );

            _ = await Assert.That(exception!.Message).IsEqualTo(FailingTestCommandHandler.ErrorMessage);
        }

        store
            .StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .WasCalled(Times.Never);
    }

    [Test]
    public async Task HandleAsync_EqualCommandSentDuringReplay_StoresOnlyNestedCommand(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = Mock.Of<ICommandDeadLetterStore>();
        var calls = new StrongBox<int>();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse(builder => builder.AddCommandDeadLetter());
        _ = services.AddSingleton<ICommandDeadLetterStore>(store.Object);
        _ = services.AddScoped<ICommandHandler<TestCommand, string>>(sp => new ResendingTestCommandHandler(
            sp.GetRequiredService<IMediatorSendOnly>(),
            calls
        ));
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ReplayAsync(provider, new TestCommand { Value = "replayed" }, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        _ = await Assert.That(calls.Value).IsEqualTo(2);
        store
            .StoreAsync(
                typeof(TestCommand).AssemblyQualifiedName!,
                Arg.Any<string>(),
                Arg.Any<Exception>(),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ReplayAsync_FailedIdempotentCommand_ThrowsConflictWithoutNewEntry(
        bool deadLetterRegisteredFirst,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = Mock.Of<ICommandDeadLetterStore>();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IIdempotencyStore>(new InMemoryIdempotencyStore());
        _ = services.AddPulse(builder =>
        {
            if (deadLetterRegisteredFirst)
            {
                _ = builder.AddCommandDeadLetter().AddIdempotency();
            }
            else
            {
                _ = builder.AddIdempotency().AddCommandDeadLetter();
            }
        });
        _ = services.AddSingleton<ICommandDeadLetterStore>(store.Object);
        _ = services.AddScoped<ICommandHandler<IdempotentTestCommand, string>, FailingIdempotentTestCommandHandler>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var command = new IdempotentTestCommand { IdempotencyKey = "replay-key" };
            var scope = provider.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediatorSendOnly>();
                _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await mediator
                        .SendAsync<IdempotentTestCommand, string>(command, cancellationToken)
                        .ConfigureAwait(false)
                );
            }

            _ = await Assert.ThrowsAsync<IdempotencyConflictException>(async () =>
                await ReplayAsync(provider, command, cancellationToken).ConfigureAwait(false)
            );
        }

        store
            .StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    private static CommandDeadLetterInterceptor<TestCommand, string> CreateInterceptor(
        ICommandDeadLetterStore store,
        IPayloadSerializer serializer,
        ILogger<CommandDeadLetterInterceptor<TestCommand, string>> logger
    )
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(store);
        _ = services.AddSingleton(logger);
        return ActivatorUtilities.CreateInstance<CommandDeadLetterInterceptor<TestCommand, string>>(
            services.BuildServiceProvider(),
            serializer
        );
    }

    private static async Task<string> ThrowingHandlerAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("handler failed");
    }

    private static async Task ReplayAsync<TCommand>(
        IServiceProvider provider,
        TCommand command,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

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

    private sealed class FailingTestCommandHandler : ICommandHandler<TestCommand, string>
    {
        public const string ErrorMessage = "replay failed";

        public Task<string> HandleAsync(TestCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(ErrorMessage);
    }

    private sealed class ResendingTestCommandHandler(IMediatorSendOnly mediator, StrongBox<int> calls)
        : ICommandHandler<TestCommand, string>
    {
        public Task<string> HandleAsync(TestCommand command, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref calls.Value) == 1
                ? mediator.SendAsync<TestCommand, string>(command with { }, cancellationToken)
                : throw new InvalidOperationException("nested failed");
    }

    private sealed class FailingIdempotentTestCommandHandler : ICommandHandler<IdempotentTestCommand, string>
    {
        public Task<string> HandleAsync(IdempotentTestCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("idempotent failed");
    }

    private sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);

        public Task<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_keys.ContainsKey(idempotencyKey));

        [SuppressMessage(
            "Usage",
            "NE0009:Method or local function has a CancellationToken parameter but does not check for cancellation at the start of its body",
            Justification = "This recording test double must record the call before honoring cancellation, so tests can assert that the call happened even when the token is already cancelled."
        )]
        public Task StoreAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            _ = _keys.TryAdd(idempotencyKey, 0);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed record IdempotentTestCommand : IIdempotentCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string IdempotencyKey { get; init; } = string.Empty;
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Value { get; init; } = string.Empty;
    }

    private sealed record TestQuery : IQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class FakeCommandDeadLetterStore : ICommandDeadLetterStore
    {
        public int StoreCallCount { get; private set; }
        public string? LastCommandType { get; private set; }
        public string? LastPayload { get; private set; }
        public Exception? LastException { get; private set; }

        public Task StoreAsync(
            string commandType,
            string payload,
            Exception exception,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            StoreCallCount++;
            LastCommandType = commandType;
            LastPayload = payload;
            LastException = exception;
            return Task.CompletedTask;
        }
    }
}
