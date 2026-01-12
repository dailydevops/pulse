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
    private readonly IOutboxRepository _repository;
    private readonly IMessageTransport _transport;
    private readonly OutboxProcessorOptions _options;
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

    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var messages = await _repository.GetPendingAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);

        if (messages.Count == 0)
        {
            // Also check for failed messages eligible for retry
            messages = await _repository
                .GetFailedForRetryAsync(_options.MaxRetryCount, _options.BatchSize, cancellationToken)
                .ConfigureAwait(false);
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

    private async Task ProcessBatchSendAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ProcessingTimeout);

            await _transport.SendBatchAsync(messages, timeoutCts.Token).ConfigureAwait(false);

            // Mark all as completed
            foreach (var message in messages)
            {
                await _repository.MarkAsCompletedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            }

            LogBatchProcessed(_logger, messages.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogBatchSendFailed(_logger, ex);

            // Fallback to individual processing
            await ProcessIndividuallyAsync(messages, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Outbox processor started. Polling interval: {PollingInterval}, Batch size: {BatchSize}"
    )]
    private static partial void LogProcessorStarted(ILogger logger, TimeSpan pollingInterval, int batchSize);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox processor stopped")]
    private static partial void LogProcessorStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during outbox processing cycle")]
    private static partial void LogProcessingCycleError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing {MessageCount} outbox messages")]
    private static partial void LogProcessingMessages(ILogger logger, int messageCount);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Successfully processed outbox message {MessageId} of type {EventType}"
    )]
    private static partial void LogMessageProcessed(ILogger logger, Guid messageId, string eventType);

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

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} moved to dead letter after {MaxRetry} retries"
    )]
    private static partial void LogMessageMovedToDeadLetter(ILogger logger, Guid messageId, int maxRetry);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Successfully processed batch of {Count} outbox messages")]
    private static partial void LogBatchProcessed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Batch send failed, falling back to individual processing")]
    private static partial void LogBatchSendFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message transport is unhealthy, skipping processing cycle")]
    private static partial void LogTransportUnhealthy(ILogger logger);
}
