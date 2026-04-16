namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event interceptor that validates incoming events using BCL <see cref="Validator"/> before passing them to handlers.
/// </summary>
/// <typeparam name="TEvent">The type of event to intercept, which must implement <see cref="IEvent"/>.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>The event is validated using <see cref="Validator"/> with all properties checked.</description></item>
/// <item><description>If no validation attributes are present, the interceptor passes through without error.</description></item>
/// <item><description>If validation fails, a <see cref="ValidationException"/> is thrown before any handler executes.</description></item>
/// <item><description>If validation passes, the event is forwarded to the handler unchanged.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddEventInterceptor</c> on the <see cref="IMediatorBuilder"/> to register this interceptor for a specific event type.
/// </remarks>
/// <seealso cref="Validator"/>
/// <seealso cref="ValidationException"/>
internal sealed class DataAnnotationsEventInterceptor<TEvent> : IEventInterceptor<TEvent>
    where TEvent : IEvent
{
    /// <inheritdoc />
    public async Task HandleAsync(
        TEvent message,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(message, context, results, validateAllProperties: true) && results.Count > 0)
        {
            throw new ValidationException(results[0], null, message);
        }

        await handler(message, cancellationToken).ConfigureAwait(false);
    }
}
