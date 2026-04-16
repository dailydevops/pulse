namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Request interceptor that validates incoming requests using <see cref="Validator"/> and
/// <see cref="System.ComponentModel.DataAnnotations"/> attributes before passing them to the handler.
/// </summary>
/// <typeparam name="TRequest">The type of request to intercept, which must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>The request is validated using <see cref="Validator"/> with all properties validated.</description></item>
/// <item><description>If validation succeeds or the request has no validation attributes, the request is forwarded to the handler unchanged.</description></item>
/// <item><description>If any validation failures exist, a <see cref="ValidationException"/> is thrown before the handler executes.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddDataAnnotations()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="Validator"/>
/// <seealso cref="ValidationException"/>
internal sealed class DataAnnotationsRequestInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
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

        return await handler(request, cancellationToken).ConfigureAwait(false);
    }
}
