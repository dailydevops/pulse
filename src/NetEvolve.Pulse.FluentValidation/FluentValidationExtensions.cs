namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides fluent extension methods for registering the FluentValidation interceptor
/// with the Pulse mediator.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The FluentValidation interceptor automatically validates all commands and queries before they
/// reach their handler, using all <c>IValidator&lt;TRequest&gt;</c> instances registered in the
/// DI container. This centralizes validation at the pipeline boundary and keeps handlers focused
/// on domain logic.
/// <para><strong>Optional Dependency:</strong></para>
/// If no <c>IValidator&lt;TRequest&gt;</c> is registered for a request type, the interceptor
/// passes through without error. Validators must be registered separately in the DI container
/// (for example via <c>services.AddScoped&lt;IValidator&lt;MyCommand&gt;, MyCommandValidator&gt;()</c>
/// or using FluentValidation's assembly scanning helpers).
/// <para><strong>Idempotency:</strong></para>
/// Calling <see cref="AddFluentValidation"/> multiple times is safe — the interceptor is registered
/// via <c>TryAddEnumerable</c> and will not be duplicated.
/// </remarks>
public static class FluentValidationExtensions
{
    /// <summary>
    /// Registers the FluentValidation request interceptor for all commands and queries.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Behavior:</strong></para>
    /// Before each command or query reaches its handler, the interceptor resolves all registered
    /// <c>IValidator&lt;TRequest&gt;</c> instances, executes them, aggregates any failures, and
    /// throws a <c>ValidationException</c> if any failures exist. Requests with no registered
    /// validators pass through unchanged.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register the FluentValidation interceptor
    /// services.AddPulse(c =&gt; c.AddFluentValidation());
    ///
    /// // Register validators (separately or via assembly scanning)
    /// services.AddScoped&lt;IValidator&lt;MyCommand&gt;, MyCommandValidator&gt;();
    /// </code>
    /// </example>
    public static IMediatorBuilder AddFluentValidation(this IMediatorBuilder configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IRequestInterceptor<,>), typeof(FluentValidationRequestInterceptor<,>))
        );

        return configurator;
    }
}
