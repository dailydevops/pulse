namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Request interceptor that validates incoming requests using all registered
/// <see cref="IValidator{T}"/> instances before passing them to the handler.
/// </summary>
/// <typeparam name="TRequest">The type of request to intercept, which must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>All <see cref="IValidator{T}"/> instances registered for <typeparamref name="TRequest"/> are resolved from the DI container.</description></item>
/// <item><description>If no validators are registered, the interceptor passes through without error.</description></item>
/// <item><description>All validators are executed and their failures are aggregated.</description></item>
/// <item><description>If any failures exist, a <see cref="ValidationException"/> is thrown before the handler executes.</description></item>
/// <item><description>If all validators pass, the request is forwarded to the handler unchanged.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddFluentValidation()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="IValidator{T}"/>
/// <seealso cref="ValidationException"/>
internal sealed class FluentValidationRequestInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="FluentValidationRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve <see cref="IValidator{T}"/> instances.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public FluentValidationRequestInterceptor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        var validators = _serviceProvider.GetServices<IValidator<TRequest>>().ToList();

        if (validators.Count == 0)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await handler(request, cancellationToken).ConfigureAwait(false);
    }
}
