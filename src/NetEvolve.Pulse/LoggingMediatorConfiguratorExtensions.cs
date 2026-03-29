namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides extension methods for registering structured logging interceptors with the Pulse mediator.
/// </summary>
public static class LoggingMediatorConfiguratorExtensions
{
    /// <summary>
    /// Adds structured <see cref="Microsoft.Extensions.Logging.ILogger"/>-based logging interceptors for all
    /// commands, queries, and events processed by the mediator.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configure">
    /// An optional action to configure <see cref="LoggingInterceptorOptions"/>.
    /// When <see langword="null"/>, the default options are used.
    /// </param>
    /// <returns>The current configurator instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Log Entries per Operation:</strong></para>
    /// <list type="bullet">
    /// <item><description>Begin entry at the configured <see cref="LoggingInterceptorOptions.LogLevel"/> (default <c>Debug</c>) before calling the handler.</description></item>
    /// <item><description>End entry at the configured level after successful execution, including elapsed milliseconds.</description></item>
    /// <item><description><c>Warning</c> entry when elapsed time exceeds <see cref="LoggingInterceptorOptions.SlowRequestThreshold"/>.</description></item>
    /// <item><description><c>Error</c> entry with the exception when the handler throws; the exception is re-thrown.</description></item>
    /// </list>
    /// <para><strong>Idempotency:</strong></para>
    /// Calling this method multiple times does not register duplicate interceptors.
    /// <para><strong>Zero-Allocation Logging:</strong></para>
    /// All log messages use <c>[LoggerMessage]</c> source generation for zero-allocation dispatch.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Basic usage with defaults
    /// services.AddPulse(config =&gt; config.AddLogging());
    ///
    /// // Custom options
    /// services.AddPulse(config =&gt; config.AddLogging(opts =&gt;
    /// {
    ///     opts.SlowRequestThreshold = TimeSpan.FromMilliseconds(200);
    ///     opts.LogLevel = LogLevel.Information;
    /// }));
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddLogging(
        this IMediatorConfigurator configurator,
        Action<LoggingInterceptorOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        _ = configurator.Services.AddOptions<LoggingInterceptorOptions>();

        if (configure is not null)
        {
            _ = configurator.Services.Configure(configure);
        }

        configurator.Services.TryAddSingleton<
            IValidateOptions<LoggingInterceptorOptions>,
            LoggingInterceptorOptionsValidator
        >();

        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IEventInterceptor<>), typeof(LoggingEventInterceptor<>))
        );
        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(LoggingRequestInterceptor<,>))
        );

        return configurator;
    }
}
