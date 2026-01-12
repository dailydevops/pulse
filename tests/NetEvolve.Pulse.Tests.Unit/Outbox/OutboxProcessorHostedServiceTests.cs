namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="OutboxProcessorHostedService"/>.
/// Tests constructor validation, message processing logic, and error handling.
/// </summary>
public sealed class OutboxProcessorHostedServiceTests
{
    #region Constructor Tests

    [Test]
    public async Task Constructor_WithNullRepository_ThrowsArgumentNullException()
    {
        IOutboxRepository? repository = null;
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions());
        var logger = CreateLogger();

        _ = Assert.Throws<ArgumentNullException>(
            "repository",
            () => _ = new OutboxProcessorHostedService(repository!, transport, options, logger)
        );
    }

    [Test]
    public async Task Constructor_WithNullTransport_ThrowsArgumentNullException()
    {
        var repository = new InMemoryOutboxRepository();
        IMessageTransport? transport = null;
        var options = Options.Create(new OutboxProcessorOptions());
        var logger = CreateLogger();

        _ = Assert.Throws<ArgumentNullException>(
            "transport",
            () => _ = new OutboxProcessorHostedService(repository, transport!, options, logger)
        );
    }

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        IOptions<OutboxProcessorOptions>? options = null;
        var logger = CreateLogger();

        _ = Assert.Throws<ArgumentNullException>(
            "options",
            () => _ = new OutboxProcessorHostedService(repository, transport, options!, logger)
        );
    }

    [Test]
    public async Task Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions());
        ILogger<OutboxProcessorHostedService>? logger = null;

        _ = Assert.Throws<ArgumentNullException>(
            "logger",
            () => _ = new OutboxProcessorHostedService(repository, transport, options, logger!)
        );
    }

    [Test]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions());
        var logger = CreateLogger();

        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        _ = await Assert.That(service).IsNotNull();
    }

    #endregion

    #region StartAsync/StopAsync Tests

    [Test]
    public async Task StartAsync_WithCancellationToken_StartsProcessing()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);

        // Give it a moment to start
        await Task.Delay(100).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        // Test passes if no exceptions thrown - verify the service started by checking poll count
        _ = await Assert.That(repository.GetPendingCallCount).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task StopAsync_WhenRunning_StopsGracefully()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        // Test passes if no exceptions thrown during graceful shutdown
        _ = await Assert.That(repository.GetPendingCallCount).IsGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Message Processing Tests

    [Test]
    public async Task ExecuteAsync_WithPendingMessages_ProcessesAndCompletesMessages()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        // Add a pending message
        var message = CreateMessage();
        await repository.AddAsync(message).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);

        // Wait for processing
        await Task.Delay(200).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.SentMessages).HasSingleItem();
            _ = await Assert.That(transport.SentMessages[0].Id).IsEqualTo(message.Id);
            _ = await Assert.That(repository.CompletedMessageIds).Contains(message.Id);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithMultipleMessages_ProcessesAllMessages()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        // Add multiple messages
        var message1 = CreateMessage();
        var message2 = CreateMessage();
        var message3 = CreateMessage();
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);
        await repository.AddAsync(message3).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.SentMessages).Count().IsEqualTo(3);
            _ = await Assert.That(repository.CompletedMessageIds).Count().IsEqualTo(3);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithNoMessages_WaitsForPollingInterval()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(100) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(250).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        // Should have polled at least twice
        _ = await Assert.That(repository.GetPendingCallCount).IsGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task ExecuteAsync_WithTransportFailure_MarksMessageAsFailed()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: int.MaxValue);
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), MaxRetryCount = 3 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        var message = CreateMessage();
        await repository.AddAsync(message).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(200).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        _ = await Assert.That(repository.FailedMessageIds).Contains(message.Id);
    }

    [Test]
    public async Task ExecuteAsync_WithExceededRetries_MovesToDeadLetter()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: int.MaxValue);
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), MaxRetryCount = 2 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        // Add a message that has already been retried once
        var message = CreateMessage();
        message.RetryCount = 1; // One retry already attempted
        await repository.AddAsync(message).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(200).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        _ = await Assert.That(repository.DeadLetterMessageIds).Contains(message.Id);
    }

    [Test]
    public async Task ExecuteAsync_WithTransientFailure_RetriesAndSucceeds()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: 1); // Fail once, then succeed
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), MaxRetryCount = 3 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        var message = CreateMessage();
        await repository.AddAsync(message).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(400).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        // Message should eventually be completed after retry
        _ = await Assert.That(repository.CompletedMessageIds).Contains(message.Id);
    }

    #endregion

    #region Batch Processing Tests

    [Test]
    public async Task ExecuteAsync_WithBatchSendingEnabled_SendsInBatch()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), EnableBatchSending = true }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        var message1 = CreateMessage();
        var message2 = CreateMessage();
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(200).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.BatchSendCallCount).IsGreaterThanOrEqualTo(1);
            _ = await Assert.That(repository.CompletedMessageIds).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithBatchSendingFailure_MarkAsFailedForRetry()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new BatchFailingMessageTransport();
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), EnableBatchSending = true }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        var message1 = CreateMessage();
        var message2 = CreateMessage();
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        // Wait for first polling cycle to process the batch failure
        await Task.Delay(100).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        // Verify that batch send was not followed by individual send (no fallback to ProcessIndividuallyAsync)
        // Messages should be marked as failed or deadlettered (depending on retry cycles that ran),
        // but NOT sent individually since we're testing that the fallback was removed.
        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.IndividualSendCallCount).IsEqualTo(0);
            _ = await Assert.That(repository.CompletedMessageIds).Count().IsEqualTo(0);
            // Messages should be in either Failed or DeadLetter based on how many cycles ran
            var totalMarked = repository.FailedMessageIds.Count + repository.DeadLetterMessageIds.Count;
            _ = await Assert.That(totalMarked).IsGreaterThanOrEqualTo(2);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithBatchSize_RespectsLimit()
    {
        var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), BatchSize = 2 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, options, logger);

        // Add more messages than batch size
        for (var i = 0; i < 5; i++)
        {
            await repository.AddAsync(CreateMessage()).ConfigureAwait(false);
        }

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        // First batch should have processed 2 messages
        _ = await Assert.That(repository.LastBatchSizeRequested).IsEqualTo(2);
    }

    #endregion

    #region Helper Methods

    private static ILogger<OutboxProcessorHostedService> CreateLogger() =>
        new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider()
            .GetRequiredService<ILogger<OutboxProcessorHostedService>>();

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

    private sealed class InMemoryOutboxRepository : IOutboxRepository
    {
        private readonly List<OutboxMessage> _messages = [];
        private readonly object _lock = new();

        public List<Guid> CompletedMessageIds { get; } = [];
        public List<Guid> FailedMessageIds { get; } = [];
        public List<Guid> DeadLetterMessageIds { get; } = [];
        public int GetPendingCallCount { get; private set; }
        public int LastBatchSizeRequested { get; private set; }

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
                LastBatchSizeRequested = batchSize;

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
                CompletedMessageIds.Add(messageId);
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
                DeadLetterMessageIds.Add(messageId);
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
                FailedMessageIds.Add(messageId);
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

    private sealed class InMemoryMessageTransport : IMessageTransport
    {
        public List<OutboxMessage> SentMessages { get; } = [];
        public int BatchSendCallCount { get; private set; }

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
        {
            BatchSendCallCount++;
            SentMessages.AddRange(messages);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingMessageTransport : IMessageTransport
    {
        private readonly int _failCount;
        private int _attemptCount;

        public FailingMessageTransport(int failCount) => _failCount = failCount;

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

    private sealed class BatchFailingMessageTransport : IMessageTransport
    {
        public int IndividualSendCallCount { get; private set; }

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            IndividualSendCallCount++;
            return Task.CompletedTask;
        }

        public Task SendBatchAsync(
            IEnumerable<OutboxMessage> messages,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Batch send failed");
    }

    #endregion
}
