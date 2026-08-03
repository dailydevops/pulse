namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides extension methods for registering the command dead letter interceptor
/// with the Pulse mediator.
/// </summary>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="ICommandDeadLetterStore"/>
public static class CommandDeadLetterMediatorBuilderExtensions
{
    /// <summary>
    /// Registers the command dead letter interceptor. Commands whose handler execution throws
    /// are recorded to <see cref="ICommandDeadLetterStore"/> before the original exception is rethrown.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method only registers the interceptor - it does not register an <see cref="ICommandDeadLetterStore"/>.
    /// Callers must also register a store, e.g. via one of the provider-specific
    /// <c>Add*CommandDeadLetterStore()</c> extensions (EF Core or an ADO.NET provider), for failed commands
    /// to actually be persisted. Without a registered store, the interceptor is a harmless no-op.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(builder =>
    /// {
    ///     builder.AddCommandDeadLetter();
    ///     // builder.AddSqlServerCommandDeadLetterStore(...); // registers the store
    /// });
    /// </code>
    /// </example>
    public static IMediatorBuilder AddCommandDeadLetter(this IMediatorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(CommandDeadLetterInterceptor<,>))
        );

        return builder;
    }
}
