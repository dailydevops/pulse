namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Serialization;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using TUnit.Mocks;
using TUnit.Mocks.Arguments;

[SuppressMessage(
    "IDisposableAnalyzers.Correctness",
    "CA2000:Dispose objects before losing scope",
    Justification = "ServiceProvider instances are short-lived within test methods"
)]
[TestGroup("Interceptors")]
public sealed class AuditRequestInterceptorTests
{
    private static IPayloadSerializer DefaultSerializer =>
        new SystemTextJsonPayloadSerializer(Options.Create(JsonSerializerOptions.Default));

    [Test]
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new AuditRequestInterceptor<TestCommand, string>(
                    null!,
                    Options.Create(new AuditOptions()),
                    DefaultSerializer,
                    new FakeAuditUserAccessor(),
                    new FakeTimeProvider(),
                    Mock.Logger<AuditRequestInterceptor<TestCommand, string>>()
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new AuditRequestInterceptor<TestCommand, string>(
                    new ServiceCollection().BuildServiceProvider(),
                    null!,
                    DefaultSerializer,
                    new FakeAuditUserAccessor(),
                    new FakeTimeProvider(),
                    Mock.Logger<AuditRequestInterceptor<TestCommand, string>>()
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NullPayloadSerializer_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new AuditRequestInterceptor<TestCommand, string>(
                    new ServiceCollection().BuildServiceProvider(),
                    Options.Create(new AuditOptions()),
                    null!,
                    new FakeAuditUserAccessor(),
                    new FakeTimeProvider(),
                    Mock.Logger<AuditRequestInterceptor<TestCommand, string>>()
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NullAuditUserAccessor_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new AuditRequestInterceptor<TestCommand, string>(
                    new ServiceCollection().BuildServiceProvider(),
                    Options.Create(new AuditOptions()),
                    DefaultSerializer,
                    null!,
                    new FakeTimeProvider(),
                    Mock.Logger<AuditRequestInterceptor<TestCommand, string>>()
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new AuditRequestInterceptor<TestCommand, string>(
                    new ServiceCollection().BuildServiceProvider(),
                    Options.Create(new AuditOptions()),
                    DefaultSerializer,
                    new FakeAuditUserAccessor(),
                    null!,
                    Mock.Logger<AuditRequestInterceptor<TestCommand, string>>()
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NullLogger_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new AuditRequestInterceptor<TestCommand, string>(
                    new ServiceCollection().BuildServiceProvider(),
                    Options.Create(new AuditOptions()),
                    DefaultSerializer,
                    new FakeAuditUserAccessor(),
                    new FakeTimeProvider(),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task HandleAsync_ExcludedCommandType_SuccessfulCommand_NeverCallsStoreAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new AuditOptions();
        _ = options.ExcludedCommandTypes.Add(typeof(TestCommand));
        var (interceptor, store) = CreateInterceptor(options);
        var command = new TestCommand { Value = "excluded" };

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task HandleAsync_ExcludedCommandType_FailingCommand_NeverCallsStoreAsyncAndRethrows(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new AuditOptions();
        _ = options.ExcludedCommandTypes.Add(typeof(TestCommand));
        var (interceptor, store) = CreateInterceptor(options);
        var command = new TestCommand { Value = "excluded" };
        var thrown = new InvalidOperationException("handler failed");

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(command, (_, _) => Task.FromException<string>(thrown), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        _ = await Assert.That(store.RecordCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task HandleAsync_SuccessfulCommand_RecordsSuccessWithExpectedValues(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeProvider = new FakeTimeProvider();
        var userAccessor = new FakeAuditUserAccessor { CurrentUser = "user-42" };
        var (interceptor, store) = CreateInterceptor(new AuditOptions(), timeProvider, userAccessor);
        var command = new TestCommand { Value = "ok", CorrelationId = "corr-1" };

        timeProvider.SetUtcNow(DateTimeOffset.UtcNow);
        var result = await interceptor
            .HandleAsync(
                command,
                async (_, _) =>
                {
                    timeProvider.Advance(TimeSpan.FromMilliseconds(5));
                    return await Task.FromResult("response").ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(1);
            _ = await Assert.That(store.LastRecord!.Result).IsEqualTo(AuditResult.Success);
            _ = await Assert.That(store.LastRecord.DurationMs).IsGreaterThanOrEqualTo(0);
            _ = await Assert.That(store.LastRecord.UserId).IsEqualTo("user-42");
            _ = await Assert.That(store.LastRecord.CorrelationId).IsEqualTo("corr-1");
            _ = await Assert.That(store.LastRecord.CommandType).IsEqualTo(typeof(TestCommand).AssemblyQualifiedName);
        }
    }

    [Test]
    public async Task HandleAsync_FailingCommand_RecordsFailureAndRethrows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (interceptor, store) = CreateInterceptor(new AuditOptions());
        var command = new TestCommand { Value = "fail" };
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
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(1);
            _ = await Assert.That(store.LastRecord!.Result).IsEqualTo(AuditResult.Failure);
            _ = await Assert.That(store.LastRecord.ExceptionMessage).IsEqualTo("handler failed");
        }
    }

    [Test]
    public async Task HandleAsync_QueryWithAuditQueriesDisabled_SuccessfulQuery_NeverCallsStoreAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (interceptor, store) = CreateInterceptorForQuery(new AuditOptions { AuditQueries = false });
        var query = new TestQuery();

        var result = await interceptor
            .HandleAsync(query, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task HandleAsync_QueryWithAuditQueriesDisabled_FailingQuery_NeverCallsStoreAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (interceptor, store) = CreateInterceptorForQuery(new AuditOptions { AuditQueries = false });
        var query = new TestQuery();
        var thrown = new InvalidOperationException("query handler failed");

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(query, (_, _) => Task.FromException<string>(thrown), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        _ = await Assert.That(store.RecordCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task HandleAsync_QueryWithAuditQueriesEnabled_SuccessfulQuery_CallsStoreAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (interceptor, store) = CreateInterceptorForQuery(new AuditOptions { AuditQueries = true });
        var query = new TestQuery();

        var result = await interceptor
            .HandleAsync(query, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(1);
            _ = await Assert.That(store.LastRecord!.Result).IsEqualTo(AuditResult.Success);
        }
    }

    [Test]
    public async Task HandleAsync_NoStoreRegistered_SuccessfulCommand_CompletesWithoutError(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new AuditRequestInterceptor<TestCommand, string>(
            provider,
            Options.Create(new AuditOptions()),
            DefaultSerializer,
            new FakeAuditUserAccessor(),
            new FakeTimeProvider(),
            Mock.Logger<AuditRequestInterceptor<TestCommand, string>>()
        );
        var command = new TestCommand { Value = "no-store" };

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("response");
    }

    [Test]
    public async Task HandleAsync_NoStoreRegistered_FailingCommand_StillRethrowsWithoutError(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new AuditRequestInterceptor<TestCommand, string>(
            provider,
            Options.Create(new AuditOptions()),
            DefaultSerializer,
            new FakeAuditUserAccessor(),
            new FakeTimeProvider(),
            Mock.Logger<AuditRequestInterceptor<TestCommand, string>>()
        );
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
    public async Task HandleAsync_CapturePayloadEnabled_PopulatesPayload(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (interceptor, store) = CreateInterceptor(new AuditOptions { CapturePayload = true });
        var command = new TestCommand { Value = "captured-value" };

        _ = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(store.LastRecord!.Payload).IsNotNull();
            _ = await Assert.That(store.LastRecord.Payload).Contains("captured-value");
        }
    }

    [Test]
    public async Task HandleAsync_CapturePayloadDisabled_LeavesPayloadNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (interceptor, store) = CreateInterceptor(new AuditOptions { CapturePayload = false });
        var command = new TestCommand { Value = "not-captured" };

        _ = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(store.LastRecord!.Payload).IsNull();
    }

    [Test]
    public async Task HandleAsync_SuccessfulCommand_StoreThrows_ReturnsResponseWithoutFailureRecord(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = Mock.Of<IAuditStore>();
        _ = store
            .RecordAsync(Arg.Any<AuditRecord>(), Arg.Any<CancellationToken>())
            .Throws(new TimeoutException("store timed out"));
        var logger = Mock.Logger<AuditRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(store.Object, logger: logger);
        var command = new TestCommand { Value = "ok" };

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(logger.Entries.Count).IsEqualTo(1);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Error);
            _ = await Assert.That(logger.Entries[0].Exception).IsTypeOf<TimeoutException>();
        }
        store
            .RecordAsync(
                Arg.Is<AuditRecord>(r => r is not null && r.Result == AuditResult.Success),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
        store
            .RecordAsync(
                Arg.Is<AuditRecord>(r => r is not null && r.Result == AuditResult.Failure),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Never);
    }

    [Test]
    public async Task HandleAsync_SuccessfulCommand_SerializerThrows_ReturnsResponse(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = new FakeAuditStore();
        var logger = Mock.Logger<AuditRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(
            store,
            new AuditOptions { CapturePayload = true },
            new ThrowingPayloadSerializer(),
            logger: logger
        );
        var command = new TestCommand { Value = "ok" };

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(0);
            _ = await Assert.That(logger.Entries.Count).IsEqualTo(1);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Error);
            _ = await Assert.That(logger.Entries[0].Exception).IsTypeOf<NotSupportedException>();
        }
    }

    [Test]
    public async Task HandleAsync_SuccessfulCommand_UserAccessorThrows_ReturnsResponse(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userAccessor = Mock.Of<IAuditUserAccessor>();
        _ = userAccessor.GetCurrentUser().Throws(new InvalidCastException("claims broken"));
        var store = new FakeAuditStore();
        var logger = Mock.Logger<AuditRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(store, userAccessor: userAccessor.Object, logger: logger);
        var command = new TestCommand { Value = "ok" };

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(0);
            _ = await Assert.That(logger.Entries.Count).IsEqualTo(1);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Error);
            _ = await Assert.That(logger.Entries[0].Exception).IsTypeOf<InvalidCastException>();
        }
    }

    [Test]
    public async Task HandleAsync_FailingCommand_StoreThrows_RethrowsOriginalException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = Mock.Of<IAuditStore>();
        _ = store
            .RecordAsync(Arg.Any<AuditRecord>(), Arg.Any<CancellationToken>())
            .Throws(new TimeoutException("store timed out"));
        var logger = Mock.Logger<AuditRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(store.Object, logger: logger);
        var command = new TestCommand { Value = "fail" };
        var thrown = new InvalidOperationException("handler failed");

        var exception = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(command, (_, _) => Task.FromException<string>(thrown), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        _ = await Assert.That(exception).IsSameReferenceAs(thrown);
        store
            .RecordAsync(
                Arg.Is<AuditRecord>(r =>
                    r is not null && r.Result == AuditResult.Failure && r.ExceptionMessage == "handler failed"
                ),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
        using (Assert.Multiple())
        {
            _ = await Assert.That(logger.Entries.Count).IsEqualTo(1);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Error);
            _ = await Assert.That(logger.Entries[0].Exception).IsTypeOf<TimeoutException>();
        }
    }

    [Test]
    public async Task HandleAsync_FailingCommand_SerializerThrows_RethrowsOriginalException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = new FakeAuditStore();
        var logger = Mock.Logger<AuditRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(
            store,
            new AuditOptions { CapturePayload = true },
            new ThrowingPayloadSerializer(),
            logger: logger
        );
        var command = new TestCommand { Value = "fail" };
        var thrown = new InvalidOperationException("handler failed");

        var exception = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(command, (_, _) => Task.FromException<string>(thrown), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsSameReferenceAs(thrown);
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(0);
            _ = await Assert.That(logger.Entries.Count).IsEqualTo(1);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Error);
            _ = await Assert.That(logger.Entries[0].Exception).IsTypeOf<NotSupportedException>();
        }
    }

    [Test]
    public async Task HandleAsync_FailingCommand_UserAccessorThrows_RethrowsOriginalException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userAccessor = Mock.Of<IAuditUserAccessor>();
        _ = userAccessor.GetCurrentUser().Throws(new InvalidCastException("claims broken"));
        var store = new FakeAuditStore();
        var logger = Mock.Logger<AuditRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(store, userAccessor: userAccessor.Object, logger: logger);
        var command = new TestCommand { Value = "fail" };
        var thrown = new InvalidOperationException("handler failed");

        var exception = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(command, (_, _) => Task.FromException<string>(thrown), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsSameReferenceAs(thrown);
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(0);
            _ = await Assert.That(logger.Entries.Count).IsEqualTo(1);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Error);
            _ = await Assert.That(logger.Entries[0].Exception).IsTypeOf<InvalidCastException>();
        }
    }

    [Test]
    public async Task HandleAsync_CallerCancelsAfterHandlerCompleted_RecordsSuccessAndReturnsResponse(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var store = new FakeAuditStore();
        var interceptor = CreateInterceptor(store);
        var command = new TestCommand { Value = "ok" };

        var result = await interceptor
            .HandleAsync(
                command,
                async (_, _) =>
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                    return "response";
                },
                cts.Token
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(1);
            _ = await Assert.That(store.LastRecord!.Result).IsEqualTo(AuditResult.Success);
        }
    }

    [Test]
    public async Task HandleAsync_CallerCancelsAfterHandlerFailed_RecordsFailureAndRethrowsOriginalException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var store = new FakeAuditStore();
        var interceptor = CreateInterceptor(store);
        var command = new TestCommand { Value = "fail" };
        var thrown = new InvalidOperationException("handler failed");

        var exception = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(
                        command,
                        async (_, _) =>
                        {
                            await cts.CancelAsync().ConfigureAwait(false);
                            throw thrown;
                        },
                        cts.Token
                    )
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsSameReferenceAs(thrown);
            _ = await Assert.That(store.RecordCallCount).IsEqualTo(1);
            _ = await Assert.That(store.LastRecord!.Result).IsEqualTo(AuditResult.Failure);
        }
    }

    private static AuditRequestInterceptor<TestCommand, string> CreateInterceptor(
        IAuditStore store,
        AuditOptions? options = null,
        IPayloadSerializer? payloadSerializer = null,
        IAuditUserAccessor? userAccessor = null,
        ILogger<AuditRequestInterceptor<TestCommand, string>>? logger = null
    )
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(store);
        var provider = services.BuildServiceProvider();

        return new AuditRequestInterceptor<TestCommand, string>(
            provider,
            Options.Create(options ?? new AuditOptions()),
            payloadSerializer ?? DefaultSerializer,
            userAccessor ?? new FakeAuditUserAccessor(),
            new FakeTimeProvider(),
            logger ?? Mock.Logger<AuditRequestInterceptor<TestCommand, string>>()
        );
    }

    private static (AuditRequestInterceptor<TestCommand, string> Interceptor, FakeAuditStore Store) CreateInterceptor(
        AuditOptions options,
        TimeProvider? timeProvider = null,
        IAuditUserAccessor? userAccessor = null
    )
    {
        var store = new FakeAuditStore();
        var services = new ServiceCollection();
        _ = services.AddSingleton<IAuditStore>(store);
        var provider = services.BuildServiceProvider();

        var interceptor = new AuditRequestInterceptor<TestCommand, string>(
            provider,
            Options.Create(options),
            DefaultSerializer,
            userAccessor ?? new FakeAuditUserAccessor(),
            timeProvider ?? new FakeTimeProvider(),
            Mock.Logger<AuditRequestInterceptor<TestCommand, string>>()
        );

        return (interceptor, store);
    }

    private static (
        AuditRequestInterceptor<TestQuery, string> Interceptor,
        FakeAuditStore Store
    ) CreateInterceptorForQuery(AuditOptions options)
    {
        var store = new FakeAuditStore();
        var services = new ServiceCollection();
        _ = services.AddSingleton<IAuditStore>(store);
        var provider = services.BuildServiceProvider();

        var interceptor = new AuditRequestInterceptor<TestQuery, string>(
            provider,
            Options.Create(options),
            DefaultSerializer,
            new FakeAuditUserAccessor(),
            new FakeTimeProvider(),
            Mock.Logger<AuditRequestInterceptor<TestQuery, string>>()
        );

        return (interceptor, store);
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

    private sealed class FakeAuditUserAccessor : IAuditUserAccessor
    {
        public string? CurrentUser { get; set; }

        public string? GetCurrentUser() => CurrentUser;
    }

    private sealed class ThrowingPayloadSerializer : IPayloadSerializer
    {
        public string Serialize<T>(T value) => throw new NotSupportedException("serializer failed");

        public string Serialize(object value, Type type) => throw new NotSupportedException("serializer failed");

        public byte[] SerializeToBytes<T>(T value) => throw new NotSupportedException("serializer failed");

        public T? Deserialize<T>(string payload) => throw new NotSupportedException("serializer failed");

        public T? Deserialize<T>(byte[] payload) => throw new NotSupportedException("serializer failed");
    }

    private sealed class FakeAuditStore : IAuditStore
    {
        public int RecordCallCount { get; private set; }
        public AuditRecord? LastRecord { get; private set; }

        public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RecordCallCount++;
            LastRecord = record;
            return Task.CompletedTask;
        }
    }
}
