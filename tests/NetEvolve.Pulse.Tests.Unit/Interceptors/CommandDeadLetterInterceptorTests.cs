namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
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
    public async Task HandleAsync_SuccessfulCommand_NeverCallsStoreAsync(CancellationToken cancellationToken)
    {
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
            StoreCallCount++;
            LastCommandType = commandType;
            LastPayload = payload;
            LastException = exception;
            return Task.CompletedTask;
        }
    }
}
