namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
                    new FakeTimeProvider()
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
                    new FakeTimeProvider()
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
                    new FakeTimeProvider()
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
                    new FakeTimeProvider()
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
            new FakeTimeProvider()
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
            new FakeTimeProvider()
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
            timeProvider ?? new FakeTimeProvider()
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
            new FakeTimeProvider()
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
