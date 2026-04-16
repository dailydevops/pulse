namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides fluent extension methods for registering the DataAnnotations validation interceptor
/// with the Pulse mediator.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The DataAnnotations interceptor automatically validates all commands, queries, and stream queries before they
/// reach their handler, using <see cref="System.ComponentModel.DataAnnotations.Validator"/> and
/// standard BCL attributes such as <c>[Required]</c>, <c>[Range]</c>, and <c>[MaxLength]</c>.
/// This centralizes validation at the pipeline boundary and keeps handlers focused on domain logic,
/// with zero additional dependencies beyond the BCL.
/// <para><strong>No Separate Validator Registration Required:</strong></para>
/// Unlike FluentValidation, DataAnnotations validation is driven entirely by attributes on the
/// request or event class itself. No additional validator types need to be registered in the DI container.
/// <para><strong>Idempotency:</strong></para>
/// Calling <see cref="AddDataAnnotations"/> multiple times is safe — the interceptors are registered
/// via <c>TryAddEnumerable</c> and will not be duplicated.
/// </remarks>
public static class DataAnnotationsExtensions
{
    /// <summary>
    /// Registers the DataAnnotations request and stream query interceptors for all commands, queries, stream queries, and events.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Behavior:</strong></para>
    /// Before each command, query, or stream query reaches its handler, the interceptor validates the request
    /// using <see cref="System.ComponentModel.DataAnnotations.Validator"/> with all properties
    /// validated. Before each event reaches its handlers, the same validation is applied.
    /// If validation fails, a
    /// <see cref="System.ComponentModel.DataAnnotations.ValidationException"/> is thrown.
    /// For stream queries, the exception is thrown before the first item is yielded.
    /// Requests and events with no validation attributes pass through unchanged.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register the DataAnnotations interceptor
    /// services.AddPulse(c =&gt; c.AddDataAnnotations());
    ///
    /// // Decorate request types with BCL validation attributes
    /// public sealed record CreateUserCommand : ICommand&lt;Guid&gt;
    /// {
    ///     public string? CorrelationId { get; set; }
    ///
    ///     [Required]
    ///     [MaxLength(100)]
    ///     public string Name { get; init; } = string.Empty;
    ///
    ///     [Range(0, 150)]
    ///     public int Age { get; init; }
    /// }
    /// </code>
    /// </example>
    public static IMediatorBuilder AddDataAnnotations(this IMediatorBuilder configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IRequestInterceptor<,>), typeof(DataAnnotationsRequestInterceptor<,>))
        );

        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IEventInterceptor<>), typeof(DataAnnotationsEventInterceptor<>))
        );

        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(
                typeof(IStreamQueryInterceptor<,>),
                typeof(DataAnnotationsStreamQueryInterceptor<,>)
            )
        );

        return configurator;
    }
}
