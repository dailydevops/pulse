namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Unit tests for the exponential backoff and jitter logic in <see cref="OutboxProcessorHostedService"/>.
/// Covers: backoff calculation, jitter range, max-delay clamping, and <c>NextRetryAt</c> filtering.
/// </summary>
public sealed class OutboxProcessorBackoffTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);

    #region ComputeNextRetryAt — disabled backoff

    [Test]
    public async Task ComputeNextRetryAt_WhenBackoffDisabled_ReturnsNull()
    {
        var options = new OutboxProcessorOptions { EnableExponentialBackoff = false };

        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 0, FixedNow);

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ComputeNextRetryAt_WhenBackoffDisabled_IgnoresHighRetryCount()
    {
        var options = new OutboxProcessorOptions { EnableExponentialBackoff = false };

        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 10, FixedNow);

        _ = await Assert.That(result).IsNull();
    }

    #endregion

    #region ComputeNextRetryAt — backoff calculation (no jitter)

    [Test]
    public async Task ComputeNextRetryAt_RetryCount0_ReturnsBaseDelay()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromMinutes(10),
            AddJitter = false,
        };

        // newRetryCount = 0: delay = 10s * 2^0 = 10s
        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 0, FixedNow);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Value).IsEqualTo(FixedNow.AddSeconds(10));
    }

    [Test]
    public async Task ComputeNextRetryAt_FirstFailure_UsesPostIncrementRetryCount()
    {
        // A message with RetryCount = 0 fails for the first time.
        // The caller passes RetryCount + 1 = 1, so the exponent is 1.
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromMinutes(10),
            AddJitter = false,
        };

        // Simulates what ProcessMessageAsync passes: message.RetryCount + 1 = 0 + 1 = 1
        // delay = 10s * 2^1 = 20s
        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 1, FixedNow);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Value).IsEqualTo(FixedNow.AddSeconds(20));
    }

    [Test]
    public async Task ComputeNextRetryAt_RetryCount1_ReturnsDoubledDelay()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromMinutes(10),
            AddJitter = false,
        };

        // newRetryCount = 1: delay = 10s * 2^1 = 20s
        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 1, FixedNow);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Value).IsEqualTo(FixedNow.AddSeconds(20));
    }

    [Test]
    public async Task ComputeNextRetryAt_RetryCount2_ReturnsQuadrupleDelay()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromMinutes(10),
            AddJitter = false,
        };

        // newRetryCount = 2: delay = 5s * 2^2 = 20s
        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 2, FixedNow);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Value).IsEqualTo(FixedNow.AddSeconds(20));
    }

    [Test]
    public async Task ComputeNextRetryAt_WithCustomMultiplier_UsesConfiguredValue()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(3),
            BackoffMultiplier = 3.0,
            MaxRetryDelay = TimeSpan.FromMinutes(10),
            AddJitter = false,
        };

        // newRetryCount = 2: delay = 3s * 3^2 = 27s
        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 2, FixedNow);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Value).IsEqualTo(FixedNow.AddSeconds(27));
    }

    #endregion

    #region ComputeNextRetryAt — MaxRetryDelay clamping

    [Test]
    public async Task ComputeNextRetryAt_WhenComputedExceedsMax_ClampsToMaxDelay()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromSeconds(30),
            AddJitter = false,
        };

        // newRetryCount = 5: delay = 10s * 2^5 = 320s → clamped to 30s
        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 5, FixedNow);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Value).IsEqualTo(FixedNow.AddSeconds(30));
    }

    [Test]
    public async Task ComputeNextRetryAt_WhenComputedBelowMax_IsNotClamped()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromSeconds(100),
            AddJitter = false,
        };

        // RetryCount = 2: delay = 5s * 2^2 = 20s, max = 100s → not clamped
        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 2, FixedNow);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Value).IsEqualTo(FixedNow.AddSeconds(20));
    }

    #endregion

    #region ComputeNextRetryAt — jitter range

    [Test]
    public async Task ComputeNextRetryAt_WithJitterEnabled_IsAtLeastBaseDelay()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromMinutes(10),
            AddJitter = true,
        };

        // RetryCount = 0: base delay = 10s; jitter can only add, never subtract
        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 0, FixedNow);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Value).IsGreaterThanOrEqualTo(FixedNow.AddSeconds(10));
    }

    [Test]
    public async Task ComputeNextRetryAt_WithJitterEnabled_IsAtMost120PercentOfBaseDelay()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromMinutes(10),
            AddJitter = true,
        };

        // RetryCount = 0: base delay = 10s, max jitter = 20% = 2s → result ≤ now + 12s
        var result = OutboxProcessorHostedService.ComputeNextRetryAt(options, 0, FixedNow);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Value).IsLessThanOrEqualTo(FixedNow.AddSeconds(12));
    }

    [Test]
    public async Task ComputeNextRetryAt_WithJitterDisabled_IsDeterministic()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromMinutes(10),
            AddJitter = false,
        };

        var result1 = OutboxProcessorHostedService.ComputeNextRetryAt(options, 1, FixedNow);
        var result2 = OutboxProcessorHostedService.ComputeNextRetryAt(options, 1, FixedNow);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result1).IsNotNull();
            _ = await Assert.That(result2).IsNotNull();
            _ = await Assert.That(result1!.Value).IsEqualTo(result2!.Value);
        }
    }

    #endregion

    #region NextRetryAt filtering

    [Test]
    public async Task GetFailedForRetryAsync_MessageWithFutureNextRetryAt_IsNotReturned()
    {
        var repo = new BackoffTestRepository();
        await repo.AddAsync(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = "TestEvent",
                    Payload = "{}",
                    Status = OutboxMessageStatus.Failed,
                    RetryCount = 1,
                    NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5),
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                    UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                }
            )
            .ConfigureAwait(false);

        var results = await repo.GetFailedForRetryAsync(3, 10).ConfigureAwait(false);

        _ = await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task GetFailedForRetryAsync_MessageWithPastNextRetryAt_IsReturned()
    {
        var repo = new BackoffTestRepository();
        await repo.AddAsync(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = "TestEvent",
                    Payload = "{}",
                    Status = OutboxMessageStatus.Failed,
                    RetryCount = 1,
                    NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                    UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                }
            )
            .ConfigureAwait(false);

        var results = await repo.GetFailedForRetryAsync(3, 10).ConfigureAwait(false);

        _ = await Assert.That(results).HasSingleItem();
    }

    [Test]
    public async Task GetFailedForRetryAsync_MessageWithNullNextRetryAt_IsReturned()
    {
        var repo = new BackoffTestRepository();
        await repo.AddAsync(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = "TestEvent",
                    Payload = "{}",
                    Status = OutboxMessageStatus.Failed,
                    RetryCount = 1,
                    NextRetryAt = null,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                }
            )
            .ConfigureAwait(false);

        var results = await repo.GetFailedForRetryAsync(3, 10).ConfigureAwait(false);

        _ = await Assert.That(results).HasSingleItem();
    }

    #endregion

    #region Integration: processor sets NextRetryAt when backoff enabled

    [Test]
    public async Task ExecuteAsync_WithBackoffEnabled_SetsNextRetryAtOnFirstFailure()
    {
        var repo = new BackoffTestRepository();
        var transport = new AlwaysFailingTransport();
        var options = Options.Create(
            new OutboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50),
                MaxRetryCount = 5,
                EnableExponentialBackoff = true,
                BaseRetryDelay = TimeSpan.FromSeconds(60),
                AddJitter = false,
            }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repo, transport, options, logger);

        var messageId = Guid.NewGuid();
        await repo.AddAsync(
                new OutboxMessage
                {
                    Id = messageId,
                    EventType = "TestEvent",
                    Payload = "{}",
                    Status = OutboxMessageStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                }
            )
            .ConfigureAwait(false);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(200).ConfigureAwait(false);
        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        var msg = repo.GetMessage(messageId);
        using (Assert.Multiple())
        {
            _ = await Assert.That(msg).IsNotNull();
            _ = await Assert.That(msg!.Status).IsEqualTo(OutboxMessageStatus.Failed);
            _ = await Assert.That(msg.NextRetryAt).IsNotNull();
            _ = await Assert.That(msg.NextRetryAt!.Value).IsGreaterThan(DateTimeOffset.UtcNow);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithBackoffDisabled_DoesNotSetNextRetryAt()
    {
        var repo = new BackoffTestRepository();
        var transport = new AlwaysFailingTransport();
        var options = Options.Create(
            new OutboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50),
                MaxRetryCount = 5,
                EnableExponentialBackoff = false,
            }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repo, transport, options, logger);

        var messageId = Guid.NewGuid();
        await repo.AddAsync(
                new OutboxMessage
                {
                    Id = messageId,
                    EventType = "TestEvent",
                    Payload = "{}",
                    Status = OutboxMessageStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                }
            )
            .ConfigureAwait(false);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(200).ConfigureAwait(false);
        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        var msg = repo.GetMessage(messageId);
        using (Assert.Multiple())
        {
            _ = await Assert.That(msg).IsNotNull();
            // NextRetryAt must never be set when backoff is disabled, regardless of status
            _ = await Assert.That(msg!.NextRetryAt).IsNull();
        }
    }

    #endregion

    #region Test doubles

    private static ILogger<OutboxProcessorHostedService> CreateLogger() =>
        new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider()
            .GetRequiredService<ILogger<OutboxProcessorHostedService>>();

    private sealed class AlwaysFailingTransport : IMessageTransport
    {
        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated transport failure");

        public Task SendBatchAsync(
            IEnumerable<OutboxMessage> messages,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Simulated transport failure");
    }

    private sealed class BackoffTestRepository : IOutboxRepository
    {
        private readonly List<OutboxMessage> _messages = [];
        private readonly object _lock = new();

        public OutboxMessage? GetMessage(Guid id)
        {
            lock (_lock)
            {
                return _messages.FirstOrDefault(m => m.Id == id);
            }
        }

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _messages.Add(message);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                var messages = _messages.Where(m => m.Status == OutboxMessageStatus.Pending).Take(batchSize).ToList();

                foreach (var msg in messages)
                {
                    msg.Status = OutboxMessageStatus.Processing;
                }

                return Task.FromResult<IReadOnlyList<OutboxMessage>>(messages);
            }
        }

        public Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
            int maxRetryCount,
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var messages = _messages
                    .Where(m =>
                        m.Status == OutboxMessageStatus.Failed
                        && m.RetryCount < maxRetryCount
                        && (m.NextRetryAt is null || m.NextRetryAt <= now)
                    )
                    .Take(batchSize)
                    .ToList();

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
                var msg = _messages.FirstOrDefault(m => m.Id == messageId);
                if (msg is not null)
                {
                    msg.Status = OutboxMessageStatus.Completed;
                    msg.NextRetryAt = null;
                }
            }

            return Task.CompletedTask;
        }

        public Task MarkAsFailedAsync(
            Guid messageId,
            string errorMessage,
            DateTimeOffset? nextRetryAt = null,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                var msg = _messages.FirstOrDefault(m => m.Id == messageId);
                if (msg is not null)
                {
                    msg.Status = OutboxMessageStatus.Failed;
                    msg.Error = errorMessage;
                    msg.RetryCount++;
                    msg.NextRetryAt = nextRetryAt;
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
                var msg = _messages.FirstOrDefault(m => m.Id == messageId);
                if (msg is not null)
                {
                    msg.Status = OutboxMessageStatus.DeadLetter;
                    msg.Error = errorMessage;
                }
            }

            return Task.CompletedTask;
        }

        public Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    #endregion
}
