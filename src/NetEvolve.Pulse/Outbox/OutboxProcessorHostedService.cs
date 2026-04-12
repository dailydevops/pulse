namespace NetEvolve.Pulse;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Background service that processes outbox messages and dispatches them via <see cref="IMessageTransport"/>.
/// Implements reliable message processing with retry logic driven by the polling interval.
/// </summary>
/// <remarks>
/// <para><strong>Processing Flow:</strong></para>
/// <list type="number">
/// <item><description>Poll repository for pending messages</description></item>
/// <item><description>Send each message via transport (single or batch)</description></item>
/// <item><description>Mark successfully sent messages as completed</description></item>
/// <item><description>Mark failed messages for retry or dead letter</description></item>
/// <item><description>Wait for polling interval and repeat</description></item>
/// </list>
/// <para><strong>Error Handling:</strong></para>
/// <list type="bullet">
/// <item><description>Transient failures: Retry on subsequent polling cycles using the configured polling interval</description></item>
/// <item><description>Exceeded retries: Move to dead letter status</description></item>
/// <item><description>Processor errors: Log and continue (resilient)</description></item>
/// </list>
/// <para><strong>Graceful Shutdown:</strong></para>
/// The processor respects cancellation tokens and completes current batch processing
/// before shutdown.
/// </remarks>
internal sealed partial class OutboxProcessorHostedService : BackgroundService
{
#pragma warning disable IDE1006 // Naming rule violation - matching existing static metric field naming conventions
    /// <summary>Counter tracking the total number of successfully processed outbox messages.</summary>
    private static readonly Counter<long> ProcessedCounter = Defaults.Meter.CreateCounter<long>(
        "pulse.outbox.processed.total",
        "messages",
        "Cumulative number of successfully processed outbox messages."
    );

    /// <summary>Counter tracking the total number of failed outbox processing attempts.</summary>
    private static readonly Counter<long> FailedCounter = Defaults.Meter.CreateCounter<long>(
        "pulse.outbox.failed.total",
        "messages",
        "Cumulative number of failed outbox processing attempts."
    );

    /// <summary>Counter tracking the total number of outbox messages moved to dead-letter.</summary>
    private static readonly Counter<long> DeadLetterCounter = Defaults.Meter.CreateCounter<long>(
        "pulse.outbox.deadletter.total",
        "messages",
        "Cumulative number of outbox messages moved to dead-letter."
    );

    /// <summary>Histogram measuring the duration of each outbox processing batch in milliseconds.</summary>
    private static readonly Histogram<double> ProcessingDurationHistogram = Defaults.Meter.CreateHistogram<double>(
        "pulse.outbox.processing.duration",
        "ms",
        "Duration of each outbox processing batch in milliseconds."
    );
#pragma warning restore IDE1006

    /// <summary>The repository for reading and updating outbox message state.</summary>
    private readonly IOutboxRepository _repository;

    /// <summary>The transport used to deliver outbox messages to their destination.</summary>
    private readonly IMessageTransport _transport;

    /// <summary>The application lifetime used to defer processing until the application has fully started.</summary>
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>The resolved processor configuration options controlling polling, batch size, and retry behaviour.</summary>
    private readonly OutboxProcessorOptions _options;

    /// <summary>The logger used for diagnostic output during processing cycles.</summary>
    private readonly ILogger<OutboxProcessorHostedService> _logger;

    /// <summary>Cached count of pending outbox messages, refreshed each polling cycle.</summary>
    private long _pendingCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxProcessorHostedService"/> class.
    /// </summary>
    /// <param name="repository">The repository for outbox message persistence.</param>
    /// <param name="transport">The transport for sending messages.</param>
    /// <param name="lifetime">The application lifetime for coordinating startup and shutdown.</param>
    /// <param name="options">The processor configuration options.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public OutboxProcessorHostedService(
        IOutboxRepository repository,
        IMessageTransport transport,
        IHostApplicationLifetime lifetime,
        IOptions<OutboxProcessorOptions> options,
        ILogger<OutboxProcessorHostedService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _transport = transport;
        _lifetime = lifetime;
        _options = options.Value;
        _logger = logger;

        _ = Defaults.Meter.CreateObservableGauge(
            "pulse.outbox.pending",
            observeValue: () => Volatile.Read(ref _pendingCount),
            unit: "messages",
            description: "Current number of pending outbox messages."
        );
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var applicationStarted = new TaskCompletionSource();
        _ = _lifetime.ApplicationStarted.Register(() => applicationStarted.TrySetResult());

        await applicationStarted.Task.ConfigureAwait(false);

        LogProcessorStarted(_logger, _options.PollingInterval, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_options.DisableProcessing)
            {
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                // Check database health before processing
                var isDatabaseHealthy = await _repository.IsHealthyAsync(stoppingToken).ConfigureAwait(false);
                if (!isDatabaseHealthy)
                {
                    LogDatabaseUnhealthy(_logger);
                    await Task.Delay(_options.PollingInterval * 2, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Check transport health before processing
                var isTransportHealthy = await _transport.IsHealthyAsync(stoppingToken).ConfigureAwait(false);
                if (!isTransportHealthy)
                {
                    LogTransportUnhealthy(_logger);
                    await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var batchStartTime = Stopwatch.GetTimestamp();
                var processedCount = await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
                var elapsed = Stopwatch.GetElapsedTime(batchStartTime).TotalMilliseconds;

                try
                {
                    ProcessingDurationHistogram.Record(elapsed);
                }
                catch (Exception ex)
                {
                    LogMetricRecordingWarning(_logger, ex);
                }

                if (processedCount == 0)
                {
                    // No messages found, wait before next poll
                    await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                LogProcessingCycleError(_logger, ex);
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
        }

        LogProcessorStopped(_logger);
    }

    /// <summary>
    /// Queries the repository for the current pending message count and updates the cached value.
    /// Exceptions are caught and logged at Warning level so that metric failures never interrupt processing.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    private async Task RefreshPendingCountAsync(CancellationToken cancellationToken)
    {
        try
        {
            var count = await _repository.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _pendingCount, count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogMetricRecordingWarning(_logger, ex);
        }
    }

    /// <summary>
    /// Retrieves and processes a single batch of pending or retriable outbox messages.
    /// Messages are grouped by event type; each group is then dispatched concurrently via
    /// <see cref="ProcessBatchSendAsync"/> or <see cref="ProcessIndividuallyAsync"/> based on the
    /// effective <see cref="OutboxProcessorOptions.EnableBatchSending"/> value for that event type.
    /// </summary>
    /// <remarks>
    /// <para><strong>Processing Strategy:</strong></para>
    /// <list type="bullet">
    /// <item><description>Retrieves up to <see cref="OutboxProcessorOptions.BatchSize"/> pending messages</description></item>
    /// <item><description>Fills remaining capacity with failed messages eligible for retry</description></item>
    /// <item><description>Groups messages by event type to apply per-type configuration overrides</description></item>
    /// <item><description>Processes each group concurrently using <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, CancellationToken, Func{TSource, CancellationToken, ValueTask})"/></description></item>
    /// <item><description>Respects cancellation requests for graceful shutdown</description></item>
    /// </list>
    /// <para><strong>Concurrency:</strong></para>
    /// Event type groups are processed in parallel to maximize throughput. However, messages within
    /// a group are processed according to the group's <see cref="OutboxProcessorOptions.EnableBatchSending"/> setting.
    /// </remarks>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The number of messages processed in this batch; <c>0</c> when no messages are available.</returns>
    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var batchSize = _options.BatchSize;
        // Refresh the pending count gauge before processing
        await RefreshPendingCountAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<OutboxMessage> messages = [];

        if (Volatile.Read(ref _pendingCount) > 0)
        {
            messages = await _repository.GetPendingAsync(batchSize, cancellationToken).ConfigureAwait(false);
            batchSize -= messages.Count;
        }

        if (batchSize > 0)
        {
            // Also check for failed messages eligible for retry
            var failedMessages = await _repository
                .GetFailedForRetryAsync(_options.MaxRetryCount, batchSize, cancellationToken)
                .ConfigureAwait(false);
            messages = [.. messages, .. failedMessages];
        }

        if (messages.Count == 0)
        {
            return 0;
        }

        LogProcessingMessages(_logger, messages.Count);

        // Group by event type so per-type EnableBatchSending overrides can be applied
        var messagesByEventType = messages.GroupBy(m => m.EventType).ToDictionary(m => m.Key, m => m.ToArray());

        await Parallel
            .ForEachAsync(
                messagesByEventType,
                cancellationToken,
                async (messageGroup, token) =>
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (!_options.GetEffectiveEnableBatchSending(messageGroup.Key))
                    {
                        await ProcessIndividuallyAsync(messageGroup.Value, token).ConfigureAwait(false);
                        return;
                    }

                    await ProcessBatchSendAsync(messageGroup.Value, token).ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

        return messages.Count;
    }

    /// <summary>
    /// Sends each message in the batch individually via <see cref="IMessageTransport.SendAsync"/>,
    /// stopping early if the cancellation token is triggered.
    /// </summary>
    /// <remarks>
    /// <para><strong>Processing Model:</strong></para>
    /// Messages are processed sequentially (not concurrently) in the order provided.
    /// Each message's success or failure is tracked independently; failure of one message
    /// does not prevent processing of subsequent messages.
    /// <para><strong>Cancellation Behavior:</strong></para>
    /// Processing stops immediately when the cancellation token is triggered, leaving
    /// remaining messages unprocessed. These messages will be re-polled and processed in subsequent cycles.
    /// </remarks>
    /// <param name="messages">The ordered array of outbox messages to process sequentially.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessIndividuallyAsync(OutboxMessage[] messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends a single outbox message via the transport, then marks it as completed or failed/dead-lettered
    /// based on the outcome. Applies a per-message processing timeout using a linked
    /// <see cref="CancellationTokenSource"/>.
    /// </summary>
    /// <param name="message">The outbox message to process.</param>
    /// <param name="cancellationToken">A token to monitor for external cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var maxRetryCount = _options.GetEffectiveMaxRetryCount(message.EventType);
        var processingTimeout = _options.GetEffectiveProcessingTimeout(message.EventType);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(processingTimeout);

            await _transport.SendAsync(message, timeoutCts.Token).ConfigureAwait(false);
            await _repository.MarkAsCompletedAsync(message.Id, cancellationToken).ConfigureAwait(false);

            LogMessageProcessed(_logger, message.Id, message.EventType.Name);

            try
            {
                ProcessedCounter.Add(1);
            }
            catch (Exception ex)
            {
                LogMetricRecordingWarning(_logger, ex);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogMessageProcessingFailed(_logger, ex, message.Id, message.RetryCount + 1, maxRetryCount);

            if (message.RetryCount + 1 >= maxRetryCount)
            {
                await _repository
                    .MarkAsDeadLetterAsync(message.Id, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                LogMessageMovedToDeadLetter(_logger, message.Id, maxRetryCount);

                try
                {
                    DeadLetterCounter.Add(1);
                }
                catch (Exception metricEx)
                {
                    LogMetricRecordingWarning(_logger, metricEx);
                }
            }
            else
            {
                DateTimeOffset? nextRetryAt = null;
                if (_options.EnableExponentialBackoff)
                {
                    nextRetryAt = _options.ComputeNextRetryAt(DateTimeOffset.UtcNow, message.RetryCount);
                }

                await _repository
                    .MarkAsFailedAsync(message.Id, ex.Message, nextRetryAt, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    FailedCounter.Add(1);
                }
                catch (Exception metricEx)
                {
                    LogMetricRecordingWarning(_logger, metricEx);
                }
            }
        }
    }

    /// <summary>
    /// Sends all messages in the batch atomically via <see cref="IMessageTransport.SendBatchAsync"/>.
    /// On success, marks every message as completed. On failure, marks every message as failed or
    /// dead-lettered based on individual retry counts and per-type <see cref="OutboxProcessorOptions.MaxRetryCount"/> overrides.
    /// </summary>
    /// <remarks>
    /// <para><strong>Atomicity Assumptions:</strong></para>
    /// The batch send operation is assumed to be atomic (all-or-nothing). If the transport supports
    /// true atomic delivery, either all messages succeed or all fail. This design avoids the need
    /// to track partial batch success and prevents duplicate message delivery.
    /// <para><strong>Error Handling:</strong></para>
    /// If batch send fails, the processor:
    /// <list type="number">
    /// <item><description>Marks messages exceeding their per-type retry limit as dead-letter</description></item>
    /// <item><description>Marks remaining messages as failed for future retry</description></item>
    /// <item><description>Logs dead-letter transitions for each affected message</description></item>
    /// <item><description>Records metrics for failed and dead-lettered message counts</description></item>
    /// </list>
    /// <para><strong>Timeout Handling:</strong></para>
    /// A per-batch timeout is applied using the global <see cref="OutboxProcessorOptions.ProcessingTimeout"/>,
    /// not per-message timeouts.
    /// </remarks>
    /// <param name="messages">The ordered array of outbox messages to send as a batch.</param>
    /// <param name="cancellationToken">A token to monitor for external cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessBatchSendAsync(OutboxMessage[] messages, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ProcessingTimeout);

            await _transport.SendBatchAsync(messages, timeoutCts.Token).ConfigureAwait(false);

            // Mark all as completed in a single batch operation
            var ids = messages.Select(static m => m.Id).ToArray();
            await _repository.MarkAsCompletedAsync(ids, cancellationToken).ConfigureAwait(false);

            LogBatchProcessed(_logger, messages.Length);

            try
            {
                ProcessedCounter.Add(messages.Length);
            }
            catch (Exception ex)
            {
                LogMetricRecordingWarning(_logger, ex);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogBatchSendFailed(_logger, ex);

            // Mark all messages as failed to avoid duplicate delivery from partial failures.
            // Batch implementations should be atomic (all-or-nothing), but if partial success
            // occurred, falling back to individual processing would re-send already-delivered messages.
            // Instead, mark all as failed and let the retry mechanism handle them on next poll.
            // Respect per-event-type MaxRetryCount overrides when determining dead-letter eligibility.
            var deadLetterMessages = messages
                .Where(m => m.RetryCount + 1 >= _options.GetEffectiveMaxRetryCount(m.EventType))
                .Select(m => m.Id)
                .ToArray();
            var failedMessages = messages
                .Where(m => m.RetryCount + 1 < _options.GetEffectiveMaxRetryCount(m.EventType))
                .ToArray();

            // Compute NextRetryAt for failed messages if exponential backoff is enabled
            var failedMessageIds = failedMessages.Select(m => m.Id).ToArray();
            if (_options.EnableExponentialBackoff && failedMessageIds.Length > 0)
            {
                var now = DateTimeOffset.UtcNow;
                var failedWithRetryTime = failedMessages
                    .Select(m => (messageId: m.Id, nextRetryAt: _options.ComputeNextRetryAt(now, m.RetryCount)))
                    .ToArray();

                // Mark failed messages with backoff scheduling
                await Parallel
                    .ForEachAsync(
                        failedWithRetryTime,
                        cancellationToken,
                        async (item, token) =>
                            await _repository
                                .MarkAsFailedAsync(item.messageId, ex.Message, item.nextRetryAt, token)
                                .ConfigureAwait(false)
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                // Mark failed messages without backoff scheduling
                await _repository
                    .MarkAsFailedAsync(failedMessageIds, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task.WhenAll(
                    _repository.MarkAsDeadLetterAsync(deadLetterMessages, ex.Message, cancellationToken),
                    Parallel.ForEachAsync(
                        messages.Where(m => m.RetryCount + 1 >= _options.GetEffectiveMaxRetryCount(m.EventType)),
                        (message, _) =>
                        {
                            LogMessageMovedToDeadLetter(
                                _logger,
                                message.Id,
                                _options.GetEffectiveMaxRetryCount(message.EventType)
                            );
                            return ValueTask.CompletedTask;
                        }
                    )
                )
                .ConfigureAwait(false);

            try
            {
                if (failedMessages.Length > 0)
                {
                    FailedCounter.Add(failedMessages.Length);
                }

                if (deadLetterMessages.Length > 0)
                {
                    DeadLetterCounter.Add(deadLetterMessages.Length);
                }
            }
            catch (Exception metricEx)
            {
                LogMetricRecordingWarning(_logger, metricEx);
            }
        }
    }

    /// <summary>Logs that the outbox processor has started with its configured <paramref name="pollingInterval"/> and <paramref name="batchSize"/>.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Outbox processor started. Polling interval: {PollingInterval}, Batch size: {BatchSize}"
    )]
    private static partial void LogProcessorStarted(ILogger logger, TimeSpan pollingInterval, int batchSize);

    /// <summary>Logs that the outbox processor has stopped cleanly.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox processor stopped")]
    private static partial void LogProcessorStopped(ILogger logger);

    /// <summary>Logs an unhandled error that occurred during an outbox processing cycle.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Error during outbox processing cycle")]
    private static partial void LogProcessingCycleError(ILogger logger, Exception exception);

    /// <summary>Logs the number of outbox messages being processed in the current batch.</summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing {MessageCount} outbox messages")]
    private static partial void LogProcessingMessages(ILogger logger, int messageCount);

    /// <summary>Logs that a single outbox message was successfully processed.</summary>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Successfully processed outbox message {MessageId} of type {EventType}"
    )]
    private static partial void LogMessageProcessed(ILogger logger, Guid messageId, string eventType);

    /// <summary>Logs a warning when processing a single outbox message fails, including retry progress.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to process outbox message {MessageId} (retry {RetryCount}/{MaxRetry})"
    )]
    private static partial void LogMessageProcessingFailed(
        ILogger logger,
        Exception exception,
        Guid messageId,
        int retryCount,
        int maxRetry
    );

    /// <summary>Logs that a message has exhausted all retries and been moved to the dead-letter status.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} moved to dead letter after {MaxRetry} retries"
    )]
    private static partial void LogMessageMovedToDeadLetter(ILogger logger, Guid messageId, int maxRetry);

    /// <summary>Logs that a batch of outbox messages was successfully processed.</summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Successfully processed batch of {Count} outbox messages")]
    private static partial void LogBatchProcessed(ILogger logger, int count);

    /// <summary>Logs a warning when a batch send operation fails; all messages in the batch will be marked as failed.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Batch send failed; marking all messages as failed")]
    private static partial void LogBatchSendFailed(ILogger logger, Exception exception);

    /// <summary>Logs a warning that the message transport is currently unhealthy and the processing cycle is being skipped.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Message transport is unhealthy, skipping processing cycle")]
    private static partial void LogTransportUnhealthy(ILogger logger);

    /// <summary>Logs a warning when recording an outbox metric fails, so metric errors never interrupt processing.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to record outbox metric")]
    private static partial void LogMetricRecordingWarning(ILogger logger, Exception exception);

    /// <summary>Logs an error that the database is currently unhealthy and the processing cycle is being skipped.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Database is unhealthy, skipping processing cycle")]
    private static partial void LogDatabaseUnhealthy(ILogger logger);
}
