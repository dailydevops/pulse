namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides fluent extension methods for registering the idempotency interceptor
/// with the Pulse mediator.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The idempotency interceptor automatically enforces at-most-once execution semantics
/// for commands that implement <see cref="IIdempotentCommand{TResponse}"/>.
/// Before delegating to the command handler, the interceptor queries the registered
/// <see cref="IIdempotencyStore"/>. If the idempotency key has already been stored,
/// an <see cref="IdempotencyConflictException"/> is thrown. Otherwise the handler executes
/// and the key is persisted afterwards.
/// <para><strong>Optional Dependency:</strong></para>
/// If <see cref="IIdempotencyStore"/> is not registered in the DI container, the interceptor
/// passes through without any store interaction and without error.
/// <para><strong>Idempotency:</strong></para>
/// Calling <see cref="AddIdempotency"/> multiple times is safe — the interceptor is registered
/// via <c>TryAddEnumerable</c> and will not be duplicated.
/// </remarks>
public static class IdempotencyMediatorConfiguratorExtensions
{
    /// <summary>
    /// Registers the idempotency enforcement interceptor for all commands that implement
    /// <see cref="IIdempotentCommand{TResponse}"/>.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Required:</strong></para>
    /// An <see cref="IIdempotencyStore"/> implementation MUST be registered separately in the DI container.
    /// Without it, the interceptor is a no-op pass-through.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register the idempotency interceptor
    /// services.AddPulse(c =&gt; c.AddIdempotency());
    ///
    /// // Register an IIdempotencyStore implementation
    /// services.AddSingleton&lt;IIdempotencyStore, MyIdempotencyStore&gt;();
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddIdempotency(this IMediatorConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IRequestInterceptor<,>), typeof(IdempotencyCommandInterceptor<,>))
        );

        return configurator;
    }
}
