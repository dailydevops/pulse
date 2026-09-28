namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Request interceptor that records commands, and optionally queries, to an <see cref="IAuditStore"/>
/// for audit trail purposes.
/// </summary>
/// <typeparam name="TRequest">The type of request being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>If <typeparamref name="TRequest"/> is listed in <see cref="AuditOptions.ExcludedCommandTypes"/>, the interceptor passes through without any try/catch overhead - the request is never audited.</description></item>
/// <item><description>If the request implements <see cref="IQuery{TResponse}"/> and <see cref="AuditOptions.AuditQueries"/> is <see langword="false"/>, the interceptor passes through unchanged - queries are not audited by default.</description></item>
/// <item><description>If <see cref="IAuditStore"/> is not registered in the DI container, the interceptor is a no-op - auditing is not possible without a store.</description></item>
/// <item><description>Otherwise, the handler is invoked and an <see cref="AuditRecord"/> is recorded afterwards, with <see cref="AuditResult.Success"/> when the handler completes or <see cref="AuditResult.Failure"/> when the handler throws. The record reflects the handler outcome only.</description></item>
/// <item><description>The serialized request payload is only captured when <see cref="AuditOptions.CapturePayload"/> is <see langword="true"/>.</description></item>
/// <item><description>The audit write is best effort (fail open): if resolving the user, serializing the payload or <see cref="IAuditStore.RecordAsync"/> throws, the error is logged and the handler outcome is kept - a successful handler still returns its response.</description></item>
/// <item><description>Once the handler has finished, the audit record is written with <see cref="CancellationToken.None"/>, so cancelling the caller's token cannot discard the record or turn a completed command into an <see cref="OperationCanceledException"/>.</description></item>
/// <item><description>The handler's original exception always propagates unchanged - handler failures are never swallowed; only failures of the audit write itself are logged and discarded (see above).</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddAudit()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="IAuditStore"/>
/// <seealso cref="IAuditUserAccessor"/>
/// <seealso cref="AuditOptions"/>
internal sealed class AuditRequestInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AuditOptions _options;
    private readonly IPayloadSerializer _payloadSerializer;
    private readonly IAuditUserAccessor _auditUserAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuditRequestInterceptor<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the optional <see cref="IAuditStore"/>.</param>
    /// <param name="options">The options that control audit trail behavior.</param>
    /// <param name="payloadSerializer">The serializer used to serialize the request payload.</param>
    /// <param name="auditUserAccessor">The accessor used to resolve the current user.</param>
    /// <param name="timeProvider">The time provider used to measure elapsed time.</param>
    /// <param name="logger">The logger used to report audit records that could not be written.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="serviceProvider"/>, <paramref name="options"/>, <paramref name="payloadSerializer"/>,
    /// <paramref name="auditUserAccessor"/>, <paramref name="timeProvider"/> or <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public AuditRequestInterceptor(
        IServiceProvider serviceProvider,
        IOptions<AuditOptions> options,
        IPayloadSerializer payloadSerializer,
        IAuditUserAccessor auditUserAccessor,
        TimeProvider timeProvider,
        ILogger<AuditRequestInterceptor<TRequest, TResponse>> logger
    )
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        ArgumentNullException.ThrowIfNull(auditUserAccessor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceProvider = serviceProvider;
        _options = options.Value;
        _payloadSerializer = payloadSerializer;
        _auditUserAccessor = auditUserAccessor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(handler);

        if (_options.ExcludedCommandTypes.Contains(typeof(TRequest)))
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        if (request is IQuery<TResponse> && !_options.AuditQueries)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        var store = _serviceProvider.GetService<IAuditStore>();
        if (store is null)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        var startTime = _timeProvider.GetUtcNow();

        // Once the handler has finished, the caller's token must not discard the audit record of its outcome.
        TResponse response;
        try
        {
            response = await handler(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await TryRecordAsync(store, request, startTime, AuditResult.Failure, ex).ConfigureAwait(false);
            throw;
        }

        await TryRecordAsync(store, request, startTime, AuditResult.Success, null).ConfigureAwait(false);
        return response;
    }

    [SuppressMessage(
        "Usage",
        "NE0010:Method returns Task and should accept a CancellationToken parameter",
        Justification = "The handler has already finished, so the audit write intentionally uses CancellationToken.None instead of the caller's token (decisions/2026-09-28-audit-write-fail-open.md)."
    )]
    private async Task TryRecordAsync(
        IAuditStore store,
        TRequest request,
        DateTimeOffset startTime,
        AuditResult result,
        Exception? handlerException
    )
    {
        try
        {
            var occurredAt = _timeProvider.GetUtcNow();

            var record = new AuditRecord
            {
                Id = Guid.NewGuid(),
                CommandType = typeof(TRequest).AssemblyQualifiedName!,
                UserId = _auditUserAccessor.GetCurrentUser(),
                CorrelationId = request.CorrelationId,
                OccurredAt = occurredAt,
                DurationMs = (occurredAt - startTime).TotalMilliseconds,
                Result = result,
                Payload = _options.CapturePayload ? _payloadSerializer.Serialize(request) : null,
                ExceptionMessage = handlerException?.Message,
            };

            await store.RecordAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Audit writes are best effort and must never replace the handler outcome.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogAuditRecordFailed(ex, result, typeof(TRequest).Name, request.CorrelationId);
        }
    }
}
