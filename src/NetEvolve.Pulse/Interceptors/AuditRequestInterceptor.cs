namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
/// <item><description>Otherwise, the handler is invoked and an <see cref="AuditRecord"/> is recorded before returning, with <see cref="AuditResult.Success"/> on success or <see cref="AuditResult.Failure"/> when the handler throws.</description></item>
/// <item><description>The serialized request payload is only captured when <see cref="AuditOptions.CapturePayload"/> is <see langword="true"/>.</description></item>
/// <item><description>The original exception always propagates unchanged - this interceptor never swallows a failure, it only optionally records it first.</description></item>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the optional <see cref="IAuditStore"/>.</param>
    /// <param name="options">The options that control audit trail behavior.</param>
    /// <param name="payloadSerializer">The serializer used to serialize the request payload.</param>
    /// <param name="auditUserAccessor">The accessor used to resolve the current user.</param>
    /// <param name="timeProvider">The time provider used to measure elapsed time.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="serviceProvider"/>, <paramref name="options"/>, <paramref name="payloadSerializer"/>,
    /// <paramref name="auditUserAccessor"/> or <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    public AuditRequestInterceptor(
        IServiceProvider serviceProvider,
        IOptions<AuditOptions> options,
        IPayloadSerializer payloadSerializer,
        IAuditUserAccessor auditUserAccessor,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        ArgumentNullException.ThrowIfNull(auditUserAccessor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _serviceProvider = serviceProvider;
        _options = options.Value;
        _payloadSerializer = payloadSerializer;
        _auditUserAccessor = auditUserAccessor;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
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

        try
        {
            var response = await handler(request, cancellationToken).ConfigureAwait(false);

            var elapsedMs = (_timeProvider.GetUtcNow() - startTime).TotalMilliseconds;

            var record = new AuditRecord
            {
                Id = Guid.NewGuid(),
                CommandType = typeof(TRequest).AssemblyQualifiedName!,
                UserId = _auditUserAccessor.GetCurrentUser(),
                CorrelationId = request.CorrelationId,
                OccurredAt = _timeProvider.GetUtcNow(),
                DurationMs = elapsedMs,
                Result = AuditResult.Success,
                Payload = _options.CapturePayload ? _payloadSerializer.Serialize(request) : null,
            };

            await store.RecordAsync(record, cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch (Exception ex)
        {
            var elapsedMs = (_timeProvider.GetUtcNow() - startTime).TotalMilliseconds;

            var record = new AuditRecord
            {
                Id = Guid.NewGuid(),
                CommandType = typeof(TRequest).AssemblyQualifiedName!,
                UserId = _auditUserAccessor.GetCurrentUser(),
                CorrelationId = request.CorrelationId,
                OccurredAt = _timeProvider.GetUtcNow(),
                DurationMs = elapsedMs,
                Result = AuditResult.Failure,
                Payload = _options.CapturePayload ? _payloadSerializer.Serialize(request) : null,
                ExceptionMessage = ex.Message,
            };

            await store.RecordAsync(record, cancellationToken).ConfigureAwait(false);

            throw;
        }
    }
}
