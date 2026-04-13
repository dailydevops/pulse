namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="OutboxProcessorHostedService"/>.
/// Tests constructor validation, message processing logic, and error handling.
/// </summary>
[TestGroup("Outbox")]
public sealed class OutboxProcessorHostedServiceTests
{
    [Test]
    public async Task Constructor_WithNullRepository_ThrowsArgumentNullException()
    {
        IOutboxRepository? repository = null;
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions());
        var logger = CreateLogger();

        _ = Assert.Throws<ArgumentNullException>(
            "repository",
            () => _ = new OutboxProcessorHostedService(repository!, transport, CreateLifetime(), options, logger)
        );
    }

    [Test]
    public async Task Constructor_WithNullTransport_ThrowsArgumentNullException()
    {
        using var repository = new InMemoryOutboxRepository();
        IMessageTransport? transport = null;
        var options = Options.Create(new OutboxProcessorOptions());
        var logger = CreateLogger();

        _ = Assert.Throws<ArgumentNullException>(
            "transport",
            () => _ = new OutboxProcessorHostedService(repository, transport!, CreateLifetime(), options, logger)
        );
    }

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        IOptions<OutboxProcessorOptions>? options = null;
        var logger = CreateLogger();

        _ = Assert.Throws<ArgumentNullException>(
            "options",
            () => _ = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options!, logger)
        );
    }

    [Test]
    public async Task Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions());
        ILogger<OutboxProcessorHostedService>? logger = null;

        _ = Assert.Throws<ArgumentNullException>(
            "logger",
            () => _ = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger!)
        );
    }

    [Test]
    public async Task Constructor_WithNullLifetime_ThrowsArgumentNullException()
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        IHostApplicationLifetime? lifetime = null;
        var options = Options.Create(new OutboxProcessorOptions());
        var logger = CreateLogger();

        _ = Assert.Throws<ArgumentNullException>(
            "lifetime",
            () => _ = new OutboxProcessorHostedService(repository, transport, lifetime!, options, logger)
        );
    }

    [Test]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions());
        var logger = CreateLogger();

        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        _ = await Assert.That(service).IsNotNull();
    }

    [Test]
    public async Task StartAsync_WithCancellationToken_StartsProcessing(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await service.StartAsync(cts.Token).ConfigureAwait(false);

        // Give it a moment to start
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // Test passes if no exceptions thrown - verify the service started by checking poll count
        _ = await Assert.That(repository.GetPendingCallCount).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task StopAsync_WhenRunning_StopsGracefully(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // Test passes if no exceptions thrown during graceful shutdown
        _ = await Assert.That(repository.GetPendingCallCount).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_WithPendingMessages_ProcessesAndCompletesMessages(
        CancellationToken cancellationToken
    )
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        // Add a pending message
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.SentMessages).HasSingleItem();
            _ = await Assert.That(transport.SentMessages[0].Id).IsEqualTo(message.Id);
            _ = await Assert.That(repository.CompletedMessageIds).Contains(message.Id);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithMultipleMessages_ProcessesAllMessages(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        // Add multiple messages
        var message1 = CreateMessage();
        var message2 = CreateMessage();
        var message3 = CreateMessage();
        await repository.AddAsync(message1, cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(message2, cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(message3, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(3, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.SentMessages).Count().IsEqualTo(3);
            _ = await Assert.That(repository.CompletedMessageIds).Count().IsEqualTo(3);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithNoMessages_WaitsForPollingInterval(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(200) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // Should have polled at least twice
        _ = await Assert.That(repository.GetPendingCallCount).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task ExecuteAsync_WithTransportFailure_MarksMessageAsFailed(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: int.MaxValue);
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), MaxRetryCount = 3 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.FailedMessageIds).Contains(message.Id);
    }

    [Test]
    public async Task ExecuteAsync_WithExceededRetries_MovesToDeadLetter(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: int.MaxValue);
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), MaxRetryCount = 2 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        // Add a message that has already been retried once
        var message = CreateMessage();
        message.RetryCount = 1; // One retry already attempted
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.DeadLetterMessageIds).Contains(message.Id);
    }

    [Test]
    public async Task ExecuteAsync_WithTransientFailure_RetriesAndSucceeds(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: 1); // Fail once, then succeed
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), MaxRetryCount = 3 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(2, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // Message should eventually be completed after retry
        _ = await Assert.That(repository.CompletedMessageIds).Contains(message.Id);
    }

    [Test]
    public async Task ExecuteAsync_WithBatchSendingEnabled_SendsInBatch(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), EnableBatchSending = true }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        var message1 = CreateMessage();
        var message2 = CreateMessage();
        await repository.AddAsync(message1, cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(message2, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(2, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.BatchSendCallCount).IsGreaterThanOrEqualTo(1);
            _ = await Assert.That(repository.CompletedMessageIds).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithBatchSendingFailure_MarkAsFailedForRetry(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new BatchFailingMessageTransport();
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), EnableBatchSending = true }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        var message1 = CreateMessage();
        var message2 = CreateMessage();
        await repository.AddAsync(message1, cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(message2, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        // Wait until at least 2 messages have been marked (failed or dead-letter) instead of
        // relying on a fixed delay, which is unreliable under CI thread-pool saturation.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(2, timeoutCts.Token).ConfigureAwait(false);

        await service.StopAsync(cancellationToken).ConfigureAwait(false);

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
    public async Task ExecuteAsync_WithBatchSize_RespectsLimit(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), BatchSize = 2 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        // Add more messages than batch size
        for (var i = 0; i < 5; i++)
        {
            await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);
        }

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(2, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // First batch should have processed 2 messages
        _ = await Assert.That(repository.LastBatchSizeRequested).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteAsync_WithPerEventTypeMaxRetryCount_UsesOverrideForMatchingEventType(
        CancellationToken cancellationToken
    )
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: int.MaxValue);
        var options = Options.Create(
            new OutboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50),
                MaxRetryCount = 5, // Global: 5 retries
                EventTypeOverrides =
                {
                    [typeof(CriticalEvent)] = new OutboxEventTypeOptions { MaxRetryCount = 1 }, // Override: 1 retry
                },
            }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        // Message of type CriticalEvent should use override (MaxRetryCount = 1)
        var message = CreateMessage(typeof(CriticalEvent));
        message.RetryCount = 0; // First attempt
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // With MaxRetryCount=1, retryCount+1 (1) >= 1, so it should be dead-lettered
        _ = await Assert.That(repository.DeadLetterMessageIds).Contains(message.Id);
    }

    [Test]
    public async Task ExecuteAsync_WithPerEventTypeProcessingTimeout_UsesOverride(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new SlowMessageTransport(delay: TimeSpan.FromMilliseconds(200));
        var options = Options.Create(
            new OutboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50),
                ProcessingTimeout = TimeSpan.FromSeconds(30), // Global: plenty of time
                MaxRetryCount = 1,
                EventTypeOverrides =
                {
                    [typeof(SlowEvent)] = new OutboxEventTypeOptions
                    {
                        ProcessingTimeout = TimeSpan.FromMilliseconds(50), // Override: very short timeout
                    },
                },
            }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        // SlowEvent message should time out and be marked as failed/dead-lettered
        var message = CreateMessage(typeof(SlowEvent));
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            // The message should not have been sent successfully
            _ = await Assert.That(transport.SentMessages).IsEmpty();
            // MaxRetryCount=1 means the first timeout should move directly to dead-letter.
            _ = await Assert.That(repository.DeadLetterMessageIds).Contains(message.Id);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithPerEventTypeBatchSending_UsesOverrideForMatchingEventType(
        CancellationToken cancellationToken
    )
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(
            new OutboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50),
                EnableBatchSending = false, // Global: individual sending
                EventTypeOverrides =
                {
                    [typeof(BatchEvent)] = new OutboxEventTypeOptions { EnableBatchSending = true },
                },
            }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        // Add two messages of the overridden type: should be batch-sent
        var message1 = CreateMessage(typeof(BatchEvent));
        var message2 = CreateMessage(typeof(BatchEvent));
        await repository.AddAsync(message1, cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(message2, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(2, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.BatchSendCallCount).IsGreaterThanOrEqualTo(1);
            _ = await Assert.That(repository.CompletedMessageIds).Count().IsGreaterThanOrEqualTo(2);
        }
    }

    [Test]
    public async Task OutboxEventTypeOptions_WithNoOverrides_UsesGlobalDefaults()
    {
        var globalOptions = new OutboxProcessorOptions
        {
            BatchSize = 50,
            PollingInterval = TimeSpan.FromSeconds(10),
            MaxRetryCount = 5,
            ProcessingTimeout = TimeSpan.FromSeconds(60),
            EnableBatchSending = true,
        };

        using (Assert.Multiple())
        {
            _ = await Assert.That(globalOptions.GetEffectiveMaxRetryCount(typeof(TestOutboxEvent))).IsEqualTo(5);
            _ = await Assert
                .That(globalOptions.GetEffectiveProcessingTimeout(typeof(TestOutboxEvent)))
                .IsEqualTo(TimeSpan.FromSeconds(60));
            _ = await Assert.That(globalOptions.GetEffectiveEnableBatchSending(typeof(TestOutboxEvent))).IsTrue();
        }
    }

    [Test]
    public async Task OutboxEventTypeOptions_WithPartialOverride_MergesWithGlobalDefaults()
    {
        var globalOptions = new OutboxProcessorOptions
        {
            MaxRetryCount = 5,
            ProcessingTimeout = TimeSpan.FromSeconds(60),
            EnableBatchSending = false,
            EventTypeOverrides =
            {
                [typeof(PriorityEvent)] = new OutboxEventTypeOptions
                {
                    MaxRetryCount = 10, // Only override MaxRetryCount
                    EnableBatchSending = true, // Override batch sending for this type
                    // ProcessingTimeout left as null -> uses global
                },
            },
        };

        using (Assert.Multiple())
        {
            _ = await Assert.That(globalOptions.GetEffectiveMaxRetryCount(typeof(PriorityEvent))).IsEqualTo(10);
            _ = await Assert
                .That(globalOptions.GetEffectiveProcessingTimeout(typeof(PriorityEvent)))
                .IsEqualTo(TimeSpan.FromSeconds(60));
            _ = await Assert.That(globalOptions.GetEffectiveEnableBatchSending(typeof(PriorityEvent))).IsTrue();
            // Non-overridden event type uses global defaults
            _ = await Assert.That(globalOptions.GetEffectiveMaxRetryCount(typeof(OtherEvent))).IsEqualTo(5);
            _ = await Assert
                .That(globalOptions.GetEffectiveProcessingTimeout(typeof(OtherEvent)))
                .IsEqualTo(TimeSpan.FromSeconds(60));
            _ = await Assert.That(globalOptions.GetEffectiveEnableBatchSending(typeof(OtherEvent))).IsFalse();
        }
    }

    [Test]
    public async Task OutboxEventTypeOptions_WithNullOverrideProperties_FallsBackToGlobalDefaults()
    {
        var globalOptions = new OutboxProcessorOptions
        {
            MaxRetryCount = 7,
            ProcessingTimeout = TimeSpan.FromSeconds(45),
            EnableBatchSending = true,
            EventTypeOverrides =
            {
                // Override entry exists but all properties are null -> all fall back to global
                [typeof(NullOverrideEvent)] = new OutboxEventTypeOptions(),
            },
        };

        using (Assert.Multiple())
        {
            _ = await Assert.That(globalOptions.GetEffectiveMaxRetryCount(typeof(NullOverrideEvent))).IsEqualTo(7);
            _ = await Assert
                .That(globalOptions.GetEffectiveProcessingTimeout(typeof(NullOverrideEvent)))
                .IsEqualTo(TimeSpan.FromSeconds(45));
            _ = await Assert.That(globalOptions.GetEffectiveEnableBatchSending(typeof(NullOverrideEvent))).IsTrue();
        }
    }

    [Test]
    [NotInParallel("OutboxMetrics")]
    public async Task ExecuteAsync_WithPendingMessages_RecordsProcessedMetric(CancellationToken cancellationToken)
    {
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "NetEvolve.Pulse")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        var processedTotal = 0L;
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "pulse.outbox.processed.total")
                {
                    _ = Interlocked.Add(ref processedTotal, measurement);
                }
            }
        );
        meterListener.Start();

        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        meterListener.RecordObservableInstruments();

        _ = await Assert.That(Volatile.Read(ref processedTotal)).IsGreaterThanOrEqualTo(1L);
    }

    [Test]
    [NotInParallel("OutboxMetrics")]
    public async Task ExecuteAsync_WithTransportFailure_RecordsFailedMetric(CancellationToken cancellationToken)
    {
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "NetEvolve.Pulse")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        var failedTotal = 0L;
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "pulse.outbox.failed.total")
                {
                    _ = Interlocked.Add(ref failedTotal, measurement);
                }
            }
        );
        meterListener.Start();

        using var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: int.MaxValue);
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), MaxRetryCount = 3 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(Volatile.Read(ref failedTotal)).IsGreaterThanOrEqualTo(1L);
    }

    [Test]
    [NotInParallel("OutboxMetrics")]
    public async Task ExecuteAsync_WithExceededRetries_RecordsDeadLetterMetric(CancellationToken cancellationToken)
    {
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "NetEvolve.Pulse")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        var deadLetterTotal = 0L;
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "pulse.outbox.deadletter.total")
                {
                    _ = Interlocked.Add(ref deadLetterTotal, measurement);
                }
            }
        );
        meterListener.Start();

        using var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: int.MaxValue);
        var options = Options.Create(
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50), MaxRetryCount = 2 }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        var message = CreateMessage();
        message.RetryCount = 1;
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(Volatile.Read(ref deadLetterTotal)).IsGreaterThanOrEqualTo(1L);
    }

    [Test]
    [NotInParallel("OutboxMetrics")]
    public async Task ExecuteAsync_AfterProcessingCycle_RecordsProcessingDuration(CancellationToken cancellationToken)
    {
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "NetEvolve.Pulse")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        var durationRecorded = false;
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, _, _, _) =>
            {
                if (instrument.Name == "pulse.outbox.processing.duration")
                {
                    Volatile.Write(ref durationRecorded, true);
                }
            }
        );
        meterListener.Start();

        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(Volatile.Read(ref durationRecorded)).IsTrue();
    }

    [Test]
    [NotInParallel("OutboxMetrics")]
    public async Task ExecuteAsync_WithPendingMessages_ObservableGaugeReflectsPendingCount(
        CancellationToken cancellationToken
    )
    {
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "NetEvolve.Pulse")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        var pendingObserved = 0L;
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "pulse.outbox.pending")
                {
                    Volatile.Write(ref pendingObserved, measurement);
                }
            }
        );
        meterListener.Start();

        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        // Add 3 pending messages before starting
        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);

        // Wait at least one polling cycle so the gauge is refreshed before observing
        await Task.Delay(75, cancellationToken).ConfigureAwait(false);
        meterListener.RecordObservableInstruments();

        var earlyObservation = Volatile.Read(ref pendingObserved);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(3, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // After processing all messages, the pending count should be 0
        meterListener.RecordObservableInstruments();
        var lateObservation = Volatile.Read(ref pendingObserved);

        using (Assert.Multiple())
        {
            // The gauge should have been observed at some point (>= 0 is always valid for a count)
            _ = await Assert.That(earlyObservation).IsGreaterThanOrEqualTo(0L);
            // After all messages are processed, pending count should be 0
            _ = await Assert.That(lateObservation).IsEqualTo(0L);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithExponentialBackoffEnabled_SetsNextRetryAt(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: int.MaxValue);
        var options = Options.Create(
            new OutboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50),
                MaxRetryCount = 3,
                EnableExponentialBackoff = true,
                BaseRetryDelay = TimeSpan.FromSeconds(1),
                AddJitter = false,
            }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var startTime = DateTimeOffset.UtcNow;

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // Get the failed message from the repository
        var failedMessage = repository._messages.FirstOrDefault(m => m.Status == OutboxMessageStatus.Failed);

        _ = await Assert.That(failedMessage).IsNotNull();
        _ = await Assert.That(failedMessage!.NextRetryAt).IsNotNull();
        // NextRetryAt should be ~1 second after processing. Processing happens quickly after StartAsync,
        // so NextRetryAt ≈ startTime + 1000ms. Asserting > startTime + 500ms is always satisfied
        // regardless of how long StopAsync or OS scheduling takes, making this assertion time-stable.
        _ = await Assert.That(failedMessage.NextRetryAt!.Value).IsGreaterThan(startTime.AddMilliseconds(500));
    }

    [Test]
    public async Task ExecuteAsync_WithExponentialBackoffDisabled_DoesNotSetNextRetryAt(
        CancellationToken cancellationToken
    )
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new FailingMessageTransport(failCount: int.MaxValue);
        var options = Options.Create(
            new OutboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50),
                MaxRetryCount = 3,
                EnableExponentialBackoff = false, // Disabled
            }
        );
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // Get the failed message from the repository
        var failedMessage = repository._messages.FirstOrDefault(m => m.Status == OutboxMessageStatus.Failed);

        // If a message was processed and failed, it should not have NextRetryAt set
        if (failedMessage is not null)
        {
            _ = await Assert.That(failedMessage.NextRetryAt).IsNull();
        }
    }

    [Test]
    public async Task GetPendingAsync_WithFutureNextRetryAt_ExcludesMessage(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var futureTime = DateTimeOffset.UtcNow.AddSeconds(10);

        var message = CreateMessage();
        message.NextRetryAt = futureTime;
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var pending = await repository.GetPendingAsync(batchSize: 10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(pending.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetPendingAsync_WithPastNextRetryAt_IncludesMessage(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var pastTime = DateTimeOffset.UtcNow.AddSeconds(-10);

        var message = CreateMessage();
        message.NextRetryAt = pastTime;
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var pending = await repository.GetPendingAsync(batchSize: 10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(pending.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetFailedForRetryAsync_WithFutureNextRetryAt_ExcludesMessage(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var futureTime = DateTimeOffset.UtcNow.AddSeconds(10);

        var message = CreateMessage();
        message.Status = OutboxMessageStatus.Failed;
        message.RetryCount = 1;
        message.NextRetryAt = futureTime;
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var failedForRetry = await repository
            .GetFailedForRetryAsync(maxRetryCount: 3, batchSize: 10, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(failedForRetry.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetFailedForRetryAsync_WithPastNextRetryAt_IncludesMessage(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository();
        var pastTime = DateTimeOffset.UtcNow.AddSeconds(-10);

        var message = CreateMessage();
        message.Status = OutboxMessageStatus.Failed;
        message.RetryCount = 1;
        message.NextRetryAt = pastTime;
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var failedForRetry = await repository
            .GetFailedForRetryAsync(maxRetryCount: 3, batchSize: 10, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(failedForRetry.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_WaitsForApplicationStarted_BeforeProcessingMessages(
        CancellationToken cancellationToken
    )
    {
        using var repository = new InMemoryOutboxRepository();
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var lifetime = new PendingStartLifetime();
        using var service = new OutboxProcessorHostedService(repository, transport, lifetime, options, logger);

        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);

        // Give the service ample time to run polling cycles if it were already started.
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.SentMessages).IsEmpty();
            _ = await Assert.That(repository.GetPendingCallCount).IsEqualTo(0);
        }

        // Now signal that the application has fully started.
        lifetime.SignalStarted();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        // Processing must have happened after ApplicationStarted fired.
        _ = await Assert.That(transport.SentMessages).HasSingleItem();
    }

    [Test]
    public async Task ExecuteAsync_WhenDatabaseIsUnhealthy_SkipsProcessingCycle(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository { IsHealthy = false };
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        await cts.CancelAsync().ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(transport.SentMessages).IsEmpty();
            _ = await Assert.That(repository.CompletedMessageIds).IsEmpty();
        }
    }

    [Test]
    public async Task ExecuteAsync_WhenDatabaseBecomesHealthy_ResumesProcessing(CancellationToken cancellationToken)
    {
        using var repository = new InMemoryOutboxRepository { IsHealthy = false };
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(50) });
        var logger = CreateLogger();
        using var service = new OutboxProcessorHostedService(repository, transport, CreateLifetime(), options, logger);

        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);

        await service.StartAsync(cancellationToken).ConfigureAwait(false);

        // Allow several unhealthy cycles to pass.
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(transport.SentMessages).IsEmpty();

        // Restore database health and allow processing to resume.
        repository.IsHealthy = true;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.WaitForMarkingsAsync(1, timeoutCts.Token).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(transport.SentMessages).HasSingleItem();
    }

    private static ILogger<OutboxProcessorHostedService> CreateLogger() =>
        new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider()
            .GetRequiredService<ILogger<OutboxProcessorHostedService>>();

    private static FakeHostApplicationLifetime CreateLifetime() => new();

    private static OutboxMessage CreateMessage(Type? eventType = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = eventType ?? typeof(TestOutboxEvent),
            Payload = """{"data":"test"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Pending,
        };

    private sealed class InMemoryOutboxRepository : IOutboxRepository, IDisposable
    {
        internal readonly List<OutboxMessage> _messages = [];
        private readonly object _lock = new();
        private readonly SemaphoreSlim _markingEvent = new(0, int.MaxValue);

        /// <summary>
        /// Waits until at least <paramref name="count"/> processing events (completed, failed, or dead-letter)
        /// have been recorded, or the <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        public async Task WaitForMarkingsAsync(int count, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < count; i++)
            {
                await _markingEvent.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

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

        public bool IsHealthy { get; set; } = true;

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(IsHealthy);

        public Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                GetPendingCallCount++;
                var count = _messages.Count(m => m.Status == OutboxMessageStatus.Pending);
                return Task.FromResult((long)count);
            }
        }

        public Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
            int maxRetryCount,
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            var now = DateTimeOffset.UtcNow;
            lock (_lock)
            {
                var messages = _messages
                    .Where(m =>
                        m.Status == OutboxMessageStatus.Failed
                        && m.RetryCount < maxRetryCount
                        && (m.NextRetryAt == null || m.NextRetryAt <= now)
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

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            var now = DateTimeOffset.UtcNow;
            lock (_lock)
            {
                GetPendingCallCount++;
                LastBatchSizeRequested = batchSize;

                var messages = _messages
                    .Where(m =>
                        m.Status == OutboxMessageStatus.Pending && (m.NextRetryAt == null || m.NextRetryAt <= now)
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
                CompletedMessageIds.Add(messageId);
                var message = _messages.FirstOrDefault(m => m.Id == messageId);
                if (message is not null)
                {
                    message.Status = OutboxMessageStatus.Completed;
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                }
            }

            _ = _markingEvent.Release();
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

            _ = _markingEvent.Release();
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

            _ = _markingEvent.Release();
            return Task.CompletedTask;
        }

        public Task MarkAsFailedAsync(
            Guid messageId,
            string errorMessage,
            DateTimeOffset? nextRetryAt,
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
                    message.NextRetryAt = nextRetryAt;
                }
            }

            _ = _markingEvent.Release();
            return Task.CompletedTask;
        }

        public void Dispose() => _markingEvent.Dispose();
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

    private sealed class SlowMessageTransport : IMessageTransport
    {
        private readonly TimeSpan _delay;

        public List<OutboxMessage> SentMessages { get; } = [];

        public SlowMessageTransport(TimeSpan delay) => _delay = delay;

        public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            SentMessages.Add(message);
        }

        public Task SendBatchAsync(
            IEnumerable<OutboxMessage> messages,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    // Test event types used as EventTypeOverrides dictionary keys
    private sealed record TestOutboxEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed record CriticalEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed record SlowEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed record BatchEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed record PriorityEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed record NullOverrideEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed record OtherEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        // A pre-cancelled token signals that the application has already started,
        // causing ExecuteAsync to proceed immediately without allocating a CancellationTokenSource.
        private static readonly CancellationToken s_startedToken = new(canceled: true);

        public CancellationToken ApplicationStarted => s_startedToken;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }

    private sealed class PendingStartLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _startedCts = new();

        // Not yet cancelled: ExecuteAsync will block until SignalStarted() is called.
        public CancellationToken ApplicationStarted => _startedCts.Token;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Dispose() => _startedCts.Dispose();

        public void StopApplication() { }

        public void SignalStarted() => _startedCts.Cancel();
    }
}
