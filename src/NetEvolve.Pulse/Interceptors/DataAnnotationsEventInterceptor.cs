namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event interceptor that validates incoming events using <see cref="Validator"/> and
/// <see cref="System.ComponentModel.DataAnnotations"/> attributes before passing them to handlers.
/// </summary>
/// <typeparam name="TEvent">The type of event to intercept, which must implement <see cref="IEvent"/>.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>The event is validated using <see cref="Validator"/> with all properties validated.</description></item>
/// <item><description>If validation succeeds or the event has no validation attributes, the event is forwarded to the handlers unchanged.</description></item>
/// <item><description>If any validation failures exist, a <see cref="ValidationException"/> is thrown before any handler executes.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddDataAnnotations()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
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
            var memberNames = results.SelectMany(r => r.MemberNames).Distinct(StringComparer.Ordinal);
            var errorMessage = string.Join(Environment.NewLine, results.Select(r => r.ErrorMessage));
            throw new ValidationException(new ValidationResult(errorMessage, memberNames), null, message);
        }

        await handler(message, cancellationToken).ConfigureAwait(false);
    }
}
