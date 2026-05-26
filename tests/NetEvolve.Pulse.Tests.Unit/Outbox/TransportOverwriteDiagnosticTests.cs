namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Audit Round 01 — U11: Transport extensions silently overwrite previous registration.
/// Repro registers both <see cref="AzureServiceBusExtensions.UseAzureServiceBusTransport"/>
/// and <see cref="KafkaExtensions.UseKafkaTransport"/> on the same builder and asserts that
/// some diagnostic surface is emitted (logged warning OR multiple descriptors retained OR exception).
/// Currently FAILS — last-write-wins is silent.
/// </summary>
[TestGroup("U11")]
public sealed class TransportOverwriteDiagnosticTests
{
    private const string FakeAsbConnectionString =
        "Endpoint=sb://localhost/;SharedAccessKeyName=Root;SharedAccessKey=Fake=";

    [Test]
    public async Task Registering_Both_AzureServiceBus_And_Kafka_Should_Surface_Diagnostic()
    {
        // Arrange — capturing logger provider for diagnostic detection.
        using var loggerProvider = new CapturingLoggerProvider();
        IServiceCollection services = new ServiceCollection();
        _ = services.AddLogging(builder => builder.AddProvider(loggerProvider));

        // Kafka prerequisites — required so the builder call doesn't bail on missing services.
        _ = services.AddSingleton<IProducer<string, string>>(_ =>
            new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = "localhost:9092" }).Build()
        );
        _ = services.AddSingleton<IAdminClient>(_ =>
            new AdminClientBuilder(new AdminClientConfig { BootstrapServers = "localhost:9092" }).Build()
        );

        // Act — register both transports on the same builder.
        _ = services.AddPulse(config =>
        {
            _ = config.UseAzureServiceBusTransport(o => o.ConnectionString = FakeAsbConnectionString);
            _ = config.UseKafkaTransport();
        });

        // Assert — at least one diagnostic surface MUST exist:
        //   (a) a Warning-or-higher log entry mentioning IMessageTransport / overwrite / replace
        //   (b) more than one IMessageTransport descriptor retained
        //   (c) UseKafkaTransport threw (the test would have failed at Act)
        var transportDescriptorCount = services.Count(d => d.ServiceType == typeof(IMessageTransport));
        var warningLogged = loggerProvider.Entries.Exists(e =>
            e.Level >= LogLevel.Warning
            && (
                e.Message.Contains("IMessageTransport", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("overwrit", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("replac", StringComparison.OrdinalIgnoreCase)
            )
        );

        var diagnosticSurfaced = warningLogged || transportDescriptorCount > 1;

        _ = await Assert
            .That(diagnosticSurfaced)
            .IsTrue()
            .Because(
                "U11: registering two transports on the same builder must NOT be silent. "
                    + $"Got {transportDescriptorCount} IMessageTransport descriptor(s); "
                    + $"{loggerProvider.Entries.Count} log entries; "
                    + "no Warning+ entry mentioned transport replacement."
            );
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

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
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
