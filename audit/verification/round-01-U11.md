# U11 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse.AzureServiceBus/AzureServiceBusExtensions.cs:51-58` — silently `Remove()`s any prior `IMessageTransport` descriptor and adds the ASB transport with no diagnostic, log, or warning.
- `src/NetEvolve.Pulse.Kafka/KafkaExtensions.cs:50-57` — identical silent remove-and-replace pattern.
- `src/NetEvolve.Pulse.RabbitMQ/RabbitMqExtensions.cs:53-60` — same pattern (verified by inspection).
- `src/NetEvolve.Pulse/OutboxExtensions.cs:94-101` — `UseMessageTransport<T>()` does the same.

**Reasoning:**
All four `Use*Transport` extensions linear-scan the `IServiceCollection` for an existing `IMessageTransport` descriptor and call `services.Remove(existing)` without logging, throwing, or surfacing any diagnostic. Calling `UseAzureServiceBusTransport(...)` followed by `UseKafkaTransport()` (or any other order/combination) leaves exactly one transport — the last one registered — and the consumer has no way to learn this short of inspecting the `IServiceCollection` themselves. The existing test `UseAzureServiceBusTransport_replaces_existing_transport` (line 116-126) actually asserts the silent-overwrite behavior as if it were correct. The failing test below registers BOTH ASB and Kafka transports on the same builder and asserts that a diagnostic surface (logged warning, thrown exception, or `IOptions`-backed flag) exists — currently nothing is emitted.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/AzureServiceBus/AzureServiceBusExtensionsTests.cs` (added `TransportOverwriteDiagnosticTests` class) and equivalent under Kafka folder for completeness — but the canonical repro is placed at `tests/NetEvolve.Pulse.Tests.Unit/Outbox/TransportOverwriteDiagnosticTests.cs`.
- Status: written
- Test code:
```csharp
namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using TUnit.Core;

// U11 — Registering two transports on the same builder must surface a diagnostic.
// Today UseAzureServiceBusTransport followed by UseKafkaTransport silently drops the ASB transport.
[TestGroup("U11")]
public sealed class TransportOverwriteDiagnosticTests
{
    private const string FakeAsbConnectionString =
        "Endpoint=sb://localhost/;SharedAccessKeyName=Root;SharedAccessKey=Fake=";

    [Test]
    public async Task Registering_Both_AzureServiceBus_And_Kafka_Should_Surface_Diagnostic()
    {
        // Arrange
        var loggerProvider = new CapturingLoggerProvider();
        IServiceCollection services = new ServiceCollection();
        _ = services.AddLogging(builder => builder.AddProvider(loggerProvider));

        // Pre-register fake Kafka prerequisites so KafkaMessageTransport can resolve.
        _ = services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = "localhost:9092" }).Build());
        _ = services.AddSingleton<IAdminClient>(_ => new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = "localhost:9092" }).Build());

        // Act — last write wins today; we want either an exception OR a logged warning.
        _ = services.AddPulse(config =>
        {
            _ = config.UseAzureServiceBusTransport(o => o.ConnectionString = FakeAsbConnectionString);
            _ = config.UseKafkaTransport();
        });

        // Assert — at least one of the following diagnostic surfaces must exist:
        //   (a) a Warning-or-higher log entry referencing IMessageTransport / overwrite, OR
        //   (b) UseKafkaTransport threw, OR
        //   (c) both registrations remain (so the user can detect the conflict via DI).
        var transportDescriptorCount = services.Count(d => d.ServiceType == typeof(IMessageTransport));
        var warningLogged = loggerProvider.Entries.Exists(e =>
            e.Level >= LogLevel.Warning
            && (e.Message.Contains("IMessageTransport", System.StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("overwrit", System.StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("replac", System.StringComparison.OrdinalIgnoreCase)));

        var diagnosticSurfaced = warningLogged || transportDescriptorCount > 1;

        _ = await Assert.That(diagnosticSurfaced)
            .IsTrue()
            .Because(
                "Registering two transports must not silently drop the first; "
                    + $"got {transportDescriptorCount} IMessageTransport descriptor(s) and "
                    + $"{loggerProvider.Entries.Count} log entries (none at Warning+ mentioning transport overwrite).");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose() { }

        public sealed record LogEntry(string Category, LogLevel Level, string Message);

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly List<LogEntry> _entries;

            public CapturingLogger(string category, List<LogEntry> entries)
            {
                _category = category;
                _entries = entries;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _entries.Add(new LogEntry(_category, logLevel, formatter(state, exception)));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
```

**Notes:**
- The existing test `UseAzureServiceBusTransport_replaces_existing_transport` at `tests/NetEvolve.Pulse.Tests.Unit/AzureServiceBus/AzureServiceBusExtensionsTests.cs:116-126` essentially codifies the current footgun. Phase 3 should decide whether the desired behavior is: (a) throw when a second transport is registered, (b) keep both and let consumer pick via keyed services, or (c) log a Warning with a clear message.
- Same pattern exists in `RabbitMqExtensions.cs:53-60` and the generic `UseMessageTransport<T>` in `src/NetEvolve.Pulse/OutboxExtensions.cs:94-101`.
- The repro intentionally uses fake Kafka prerequisites so it can run as a unit test without a broker. `BuildServiceProvider()` is not invoked because the assertion is about the diagnostic surface emitted by `Use*Transport`, not transport instantiation.
