namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Stream query interceptor that validates incoming queries using <see cref="Validator"/> and
/// <see cref="System.ComponentModel.DataAnnotations"/> attributes before the first item is yielded.
/// </summary>
/// <typeparam name="TQuery">The type of stream query to intercept, which must implement <see cref="IStreamQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>The query is validated using <see cref="Validator"/> with all properties validated.</description></item>
/// <item><description>If validation succeeds or the query has no validation attributes, the query is forwarded to the handler unchanged.</description></item>
/// <item><description>If any validation failures exist, a <see cref="ValidationException"/> is thrown before the first item is yielded.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddDataAnnotations()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="Validator"/>
/// <seealso cref="ValidationException"/>
internal sealed class DataAnnotationsStreamQueryInterceptor<TQuery, TResponse>
    : IStreamQueryInterceptor<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        var validationContext = new ValidationContext(request!);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(request!, validationContext, results, validateAllProperties: true))
        {
            var memberNames = results.SelectMany(r => r.MemberNames).Distinct(StringComparer.Ordinal);
            var errorMessage = string.Join(Environment.NewLine, results.Select(r => r.ErrorMessage));
            throw new ValidationException(new ValidationResult(errorMessage, memberNames), null, request);
        }

        return HandleCoreAsync(request, handler, cancellationToken);
    }

    private static async IAsyncEnumerable<TResponse> HandleCoreAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (var item in handler(request, cancellationToken).WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }
}
