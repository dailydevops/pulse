namespace NetEvolve.Pulse;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
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
public sealed partial class OutboxProcessorHostedService : BackgroundService
{
    /// <summary>The repository for reading and updating outbox message state.</summary>
    private readonly IOutboxRepository _repository;

    /// <summary>The transport used to deliver outbox messages to their destination.</summary>
    private readonly IMessageTransport _transport;

    /// <summary>The resolved processor configuration options controlling polling, batch size, and retry behaviour.</summary>
    private readonly OutboxProcessorOptions _options;

    /// <summary>The logger used for diagnostic output during processing cycles.</summary>
    private readonly ILogger<OutboxProcessorHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxProcessorHostedService"/> class.
    /// </summary>
    /// <param name="repository">The repository for outbox message persistence.</param>
    /// <param name="transport">The transport for sending messages.</param>
    /// <param name="options">The processor configuration options.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public OutboxProcessorHostedService(
        IOutboxRepository repository,
        IMessageTransport transport,
        IOptions<OutboxProcessorOptions> options,
        ILogger<OutboxProcessorHostedService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _transport = transport;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogProcessorStarted(_logger, _options.PollingInterval, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check transport health before processing
                var isHealthy = await _transport.IsHealthyAsync(stoppingToken).ConfigureAwait(false);
                if (!isHealthy)
                {
                    LogTransportUnhealthy(_logger);
                    await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var processedCount = await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);

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
    /// Retrieves and processes a single batch of pending or retriable outbox messages.
    /// Delegates to <see cref="ProcessBatchSendAsync"/> or <see cref="ProcessIndividuallyAsync"/> based on
    /// <see cref="OutboxProcessorOptions.EnableBatchSending"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The number of messages processed in this batch; <c>0</c> when no messages are available.</returns>
    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var batchSize = _options.BatchSize;

        var messages = await _repository.GetPendingAsync(batchSize, cancellationToken).ConfigureAwait(false);

        batchSize -= messages.Count;

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

        if (_options.EnableBatchSending)
        {
            await ProcessBatchSendAsync(messages, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ProcessIndividuallyAsync(messages, cancellationToken).ConfigureAwait(false);
        }

        return messages.Count;
    }

    /// <summary>
    /// Sends each message in the batch individually via <see cref="IMessageTransport.SendAsync"/>,
    /// stopping early if the cancellation token is triggered.
    /// </summary>
    /// <param name="messages">The ordered list of outbox messages to process.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessIndividuallyAsync(
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken
    )
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
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ProcessingTimeout);

            await _transport.SendAsync(message, timeoutCts.Token).ConfigureAwait(false);
            await _repository.MarkAsCompletedAsync(message.Id, cancellationToken).ConfigureAwait(false);

            LogMessageProcessed(_logger, message.Id, message.EventType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogMessageProcessingFailed(_logger, ex, message.Id, message.RetryCount + 1, _options.MaxRetryCount);

            if (message.RetryCount + 1 >= _options.MaxRetryCount)
            {
                await _repository
                    .MarkAsDeadLetterAsync(message.Id, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                LogMessageMovedToDeadLetter(_logger, message.Id, _options.MaxRetryCount);
            }
            else
            {
                await _repository.MarkAsFailedAsync(message.Id, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Sends all messages in the batch atomically via <see cref="IMessageTransport.SendBatchAsync"/>.
    /// On success, marks every message as completed. On failure, marks every message as failed or
    /// dead-lettered so they are retried on subsequent polling cycles.
    /// </summary>
    /// <param name="messages">The ordered list of outbox messages to send as a batch.</param>
    /// <param name="cancellationToken">A token to monitor for external cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessBatchSendAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ProcessingTimeout);

            await _transport.SendBatchAsync(messages, timeoutCts.Token).ConfigureAwait(false);

            // Mark all as completed in a single batch operation
            var ids = messages.Select(static m => m.Id).ToArray();
            await _repository.MarkAsCompletedAsync(ids, cancellationToken).ConfigureAwait(false);

            LogBatchProcessed(_logger, messages.Count);
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
            var deadLetterMessages = messages
                .Where(m => m.RetryCount + 1 >= _options.MaxRetryCount)
                .Select(m => m.Id)
                .ToArray();
            var failedMessages = messages
                .Where(m => m.RetryCount + 1 < _options.MaxRetryCount)
                .Select(m => m.Id)
                .ToArray();

            await Task.WhenAll(
                    _repository.MarkAsFailedAsync(failedMessages, ex.Message, cancellationToken),
                    _repository.MarkAsDeadLetterAsync(deadLetterMessages, ex.Message, cancellationToken),
                    Parallel.ForEachAsync(
                        deadLetterMessages,
                        (messageId, _) =>
                        {
                            LogMessageMovedToDeadLetter(_logger, messageId, _options.MaxRetryCount);
                            return ValueTask.CompletedTask;
                        }
                    )
                )
                .ConfigureAwait(false);
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
}
