namespace NetEvolve.Pulse.Tests.Integration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="OutboxProcessorHostedService"/>.
/// Tests the processor with real DI container integration and service lifecycle.
/// </summary>
public sealed class OutboxProcessorHostedServiceTests
{
    #region DI Integration Tests

    [Test]
    public async Task AddOutbox_RegistersHostedService()
    {
        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton<IOutboxRepository, TestOutboxRepository>()
            .AddPulse(configurator => configurator.AddOutbox());

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        var processorService = hostedServices.OfType<OutboxProcessorHostedService>().FirstOrDefault();

        _ = await Assert.That(processorService).IsNotNull();
    }

    [Test]
    public async Task AddOutbox_WithConfiguration_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton<IOutboxRepository, TestOutboxRepository>()
            .AddPulse(configurator =>
                configurator.AddOutbox(configureProcessorOptions: options =>
                {
                    options.BatchSize = 50;
                    options.PollingInterval = TimeSpan.FromSeconds(10);
                    options.MaxRetryCount = 5;
                })
            );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxProcessorOptions>>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.Value.BatchSize).IsEqualTo(50);
            _ = await Assert.That(options.Value.PollingInterval).IsEqualTo(TimeSpan.FromSeconds(10));
            _ = await Assert.That(options.Value.MaxRetryCount).IsEqualTo(5);
        }
    }

    #endregion

    #region Full Integration Tests

    [Test]
    public async Task ProcessorIntegration_WithPendingMessage_ProcessesSuccessfully()
    {
        var repository = new TestOutboxRepository();
        var transport = new TestMessageTransport();

        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton<IOutboxRepository>(repository)
            .AddPulse(configurator =>
            {
                _ = configurator.AddOutbox(configureProcessorOptions: options =>
                {
                    options.PollingInterval = TimeSpan.FromMilliseconds(50);
                    options.BatchSize = 10;
                });
                _ = configurator.UseMessageTransport(_ => transport);
            });

        await using var provider = services.BuildServiceProvider();

        // Add a message before starting
        var message = CreateMessage();
        await repository.AddAsync(message).ConfigureAwait(false);

        // Get and start the hosted service
        var hostedService = provider.GetServices<IHostedService>().OfType<OutboxProcessorHostedService>().Single();

        using var cts = new CancellationTokenSource();

        await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.SentMessages).HasSingleItem();
            _ = await Assert.That(transport.SentMessages.First().Id).IsEqualTo(message.Id);
            _ = await Assert.That(repository.CompletedCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ProcessorIntegration_WithFailingTransport_RetriesMessage()
    {
        var repository = new TestOutboxRepository();
        var transport = new FailingTransport(failCount: 1);

        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton<IOutboxRepository>(repository)
            .AddPulse(configurator =>
            {
                _ = configurator.AddOutbox(configureProcessorOptions: options =>
                {
                    options.PollingInterval = TimeSpan.FromMilliseconds(50);
                    options.MaxRetryCount = 3;
                });
                _ = configurator.UseMessageTransport(_ => transport);
            });

        await using var provider = services.BuildServiceProvider();

        var message = CreateMessage();
        await repository.AddAsync(message).ConfigureAwait(false);

        var hostedService = provider.GetServices<IHostedService>().OfType<OutboxProcessorHostedService>().Single();

        using var cts = new CancellationTokenSource();

        await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(400).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);

        // Should have retried and eventually succeeded
        _ = await Assert.That(repository.CompletedCount).IsEqualTo(1);
    }

    [Test]
    public async Task ProcessorIntegration_WithPermanentFailure_MovesToDeadLetter()
    {
        var repository = new TestOutboxRepository();
        var transport = new FailingTransport(failCount: int.MaxValue);

        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton<IOutboxRepository>(repository)
            .AddPulse(configurator =>
            {
                _ = configurator.AddOutbox(configureProcessorOptions: options =>
                {
                    options.PollingInterval = TimeSpan.FromMilliseconds(50);
                    options.MaxRetryCount = 2;
                });
                _ = configurator.UseMessageTransport(_ => transport);
            });

        await using var provider = services.BuildServiceProvider();

        // Add a message with 1 retry already (will exceed max of 2 on next failure)
        var message = CreateMessage();
        message.RetryCount = 1;
        await repository.AddAsync(message).ConfigureAwait(false);

        var hostedService = provider.GetServices<IHostedService>().OfType<OutboxProcessorHostedService>().Single();

        using var cts = new CancellationTokenSource();

        await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(200).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);

        _ = await Assert.That(repository.DeadLetterCount).IsEqualTo(1);
    }

    [Test]
    public async Task ProcessorIntegration_WithBatchSending_SendsBatches()
    {
        var repository = new TestOutboxRepository();
        var transport = new TestMessageTransport();

        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton<IOutboxRepository>(repository)
            .AddPulse(configurator =>
            {
                _ = configurator.AddOutbox(configureProcessorOptions: options =>
                {
                    options.PollingInterval = TimeSpan.FromMilliseconds(50);
                    options.EnableBatchSending = true;
                    options.BatchSize = 10;
                });
                _ = configurator.UseMessageTransport(_ => transport);
            });

        await using var provider = services.BuildServiceProvider();

        // Add multiple messages
        await repository.AddAsync(CreateMessage()).ConfigureAwait(false);
        await repository.AddAsync(CreateMessage()).ConfigureAwait(false);
        await repository.AddAsync(CreateMessage()).ConfigureAwait(false);

        var hostedService = provider.GetServices<IHostedService>().OfType<OutboxProcessorHostedService>().Single();

        using var cts = new CancellationTokenSource();

        await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.BatchSendCount).IsGreaterThanOrEqualTo(1);
            _ = await Assert.That(transport.SentMessages).Count().IsEqualTo(3);
            _ = await Assert.That(repository.CompletedCount).IsEqualTo(3);
        }
    }

    [Test]
    public async Task ProcessorIntegration_WithGracefulShutdown_CompletesWithoutException()
    {
        var repository = new TestOutboxRepository();
        var transport = new SlowTransport(delay: TimeSpan.FromMilliseconds(50));

        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton<IOutboxRepository>(repository)
            .AddPulse(configurator =>
            {
                _ = configurator.AddOutbox(configureProcessorOptions: options =>
                    options.PollingInterval = TimeSpan.FromMilliseconds(50)
                );
                _ = configurator.UseMessageTransport(_ => transport);
            });

        await using var provider = services.BuildServiceProvider();

        await repository.AddAsync(CreateMessage()).ConfigureAwait(false);

        var hostedService = provider.GetServices<IHostedService>().OfType<OutboxProcessorHostedService>().Single();

        using var cts = new CancellationTokenSource();

        await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);

        // Request cancellation during processing
        await cts.CancelAsync().ConfigureAwait(false);

        // StopAsync should wait for current processing to complete
        await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);

        // Test passes if no exception thrown during shutdown - verify service was started
        _ = await Assert.That(repository.GetPendingCallCount).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ProcessorIntegration_WithMultipleCycles_ProcessesAllMessages()
    {
        var repository = new TestOutboxRepository();
        var transport = new TestMessageTransport();

        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton<IOutboxRepository>(repository)
            .AddPulse(configurator =>
            {
                _ = configurator.AddOutbox(configureProcessorOptions: options =>
                {
                    options.PollingInterval = TimeSpan.FromMilliseconds(50);
                    options.BatchSize = 2; // Small batch to force multiple cycles
                });
                _ = configurator.UseMessageTransport(_ => transport);
            });

        await using var provider = services.BuildServiceProvider();

        // Add more messages than batch size
        for (var i = 0; i < 5; i++)
        {
            await repository.AddAsync(CreateMessage()).ConfigureAwait(false);
        }

        var hostedService = provider.GetServices<IHostedService>().OfType<OutboxProcessorHostedService>().Single();

        using var cts = new CancellationTokenSource();

        await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(500).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);

        _ = await Assert.That(repository.CompletedCount).IsEqualTo(5);
    }

    #endregion

    #region Helper Methods

    private static OutboxMessage CreateMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = "TestEvent",
            Payload = """{"data":"test"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Pending,
        };

    #endregion

    #region Test Doubles

    private sealed class TestOutboxRepository : IOutboxRepository
    {
        private readonly List<OutboxMessage> _messages = [];
        private readonly object _lock = new();

        public int CompletedCount { get; private set; }
        public int FailedCount { get; private set; }
        public int DeadLetterCount { get; private set; }
        public int GetPendingCallCount { get; private set; }

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _messages.Add(message);
            }

            return Task.CompletedTask;
        }

        public Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
            int maxRetryCount,
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                var messages = _messages
                    .Where(m => m.Status == OutboxMessageStatus.Failed && m.RetryCount < maxRetryCount)
                    .Take(batchSize)
                    .ToList();

                foreach (var msg in messages)
                {
                    msg.Status = OutboxMessageStatus.Processing;
                }

                return Task.FromResult<IReadOnlyList<OutboxMessage>>(messages);
            }
        }

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                GetPendingCallCount++;

                var messages = _messages.Where(m => m.Status == OutboxMessageStatus.Pending).Take(batchSize).ToList();

                foreach (var msg in messages)
                {
                    msg.Status = OutboxMessageStatus.Processing;
                }

                return Task.FromResult<IReadOnlyList<OutboxMessage>>(messages);
            }
        }

        public Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                CompletedCount++;
                var message = _messages.FirstOrDefault(m => m.Id == messageId);
                if (message is not null)
                {
                    message.Status = OutboxMessageStatus.Completed;
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                }
            }

            return Task.CompletedTask;
        }

        public Task MarkAsDeadLetterAsync(
            Guid messageId,
            string errorMessage,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                DeadLetterCount++;
                var message = _messages.FirstOrDefault(m => m.Id == messageId);
                if (message is not null)
                {
                    message.Status = OutboxMessageStatus.DeadLetter;
                    message.Error = errorMessage;
                }
            }

            return Task.CompletedTask;
        }

        public Task MarkAsFailedAsync(
            Guid messageId,
            string errorMessage,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                FailedCount++;
                var message = _messages.FirstOrDefault(m => m.Id == messageId);
                if (message is not null)
                {
                    message.Status = OutboxMessageStatus.Failed;
                    message.Error = errorMessage;
                    message.RetryCount++;
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestMessageTransport : IMessageTransport
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<OutboxMessage> _sentMessages = [];
        private int _batchSendCount;

        public IReadOnlyCollection<OutboxMessage> SentMessages => _sentMessages.ToArray();
        public int BatchSendCount => _batchSendCount;

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            _sentMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
        {
            _ = Interlocked.Increment(ref _batchSendCount);
            foreach (var message in messages)
            {
                _sentMessages.Add(message);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FailingTransport : IMessageTransport
    {
        private readonly int _failCount;
        private int _attemptCount;

        public FailingTransport(int failCount) => _failCount = failCount;

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _attemptCount);
            if (attempt <= _failCount)
            {
                throw new InvalidOperationException($"Simulated transport failure on attempt {attempt}");
            }

            return Task.CompletedTask;
        }

        public Task SendBatchAsync(
            IEnumerable<OutboxMessage> messages,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class SlowTransport : IMessageTransport
    {
        private readonly TimeSpan _delay;

        public SlowTransport(TimeSpan delay) => _delay = delay;

        public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);

        public Task SendBatchAsync(
            IEnumerable<OutboxMessage> messages,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    #endregion
}
