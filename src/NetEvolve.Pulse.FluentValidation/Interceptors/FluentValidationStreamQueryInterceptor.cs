namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Stream query interceptor that validates incoming stream queries using all registered
/// <see cref="IValidator{T}"/> instances before starting enumeration.
/// </summary>
/// <typeparam name="TQuery">The type of stream query to intercept, which must implement <see cref="IStreamQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>All <see cref="IValidator{T}"/> instances registered for <typeparamref name="TQuery"/> are resolved from the DI container.</description></item>
/// <item><description>If no validators are registered, the interceptor passes through without error.</description></item>
/// <item><description>All validators are executed and their failures are aggregated.</description></item>
/// <item><description>If any failures exist, a <see cref="ValidationException"/> is thrown before enumeration starts.</description></item>
/// <item><description>If all validators pass, items are forwarded from the handler unchanged.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddFluentValidation()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="IValidator{T}"/>
/// <seealso cref="ValidationException"/>
internal sealed class FluentValidationStreamQueryInterceptor<TQuery, TResponse>
    : IStreamQueryInterceptor<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="FluentValidationStreamQueryInterceptor{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve <see cref="IValidator{T}"/> instances.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public FluentValidationStreamQueryInterceptor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        var validators = _serviceProvider.GetServices<IValidator<TQuery>>().ToList();

        if (validators.Count > 0)
        {
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
        }

        await foreach (
            var item in handler(request, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            yield return item;
        }
    }
}
