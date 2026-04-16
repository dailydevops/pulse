namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides a convenience extension method for registering all DataAnnotations validation interceptors
/// with the Pulse mediator in a single call.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Registers the DataAnnotations request, event, and stream query interceptors as Singleton services,
/// allowing opt-in to BCL validation with a single startup configuration call.
/// <para><strong>No Separate Validator Registration Required:</strong></para>
/// Unlike FluentValidation, DataAnnotations validation is driven entirely by attributes on the
/// request or event class itself. No additional validator types need to be registered in the DI container.
/// <para><strong>Idempotency:</strong></para>
/// Calling <see cref="AddDataAnnotationsValidation"/> multiple times is safe — the interceptors are registered
/// via <c>TryAddEnumerable</c> and will not be duplicated.
/// </remarks>
public static class DataAnnotationsMediatorBuilderExtensions
{
    /// <summary>
    /// Registers all three DataAnnotations validation interceptors for requests, events, and stream queries.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
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
    /// // Register all DataAnnotations validation interceptors with a single call
    /// services.AddPulse(c =&gt; c.AddDataAnnotationsValidation());
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
    public static IMediatorBuilder AddDataAnnotationsValidation(this IMediatorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(DataAnnotationsRequestInterceptor<,>))
        );

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IEventInterceptor<>), typeof(DataAnnotationsEventInterceptor<>))
        );

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(
                typeof(IStreamQueryInterceptor<,>),
                typeof(DataAnnotationsStreamQueryInterceptor<,>)
            )
        );

        return builder;
    }
}
