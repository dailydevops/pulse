namespace NetEvolve.Pulse.Interceptors;

/// <summary>
/// Options for the built-in request timeout interceptor registered via <c>AddRequestTimeout()</c>.
/// </summary>
/// <remarks>
/// <para><strong>Global Timeout:</strong></para>
/// <see cref="GlobalTimeout"/> is only applied to requests that implement
/// <see cref="Extensibility.ITimeoutRequest"/> and return <see langword="null"/> from
/// <see cref="Extensibility.ITimeoutRequest.Timeout"/>. A non-<see langword="null"/>
/// <see cref="Extensibility.ITimeoutRequest.Timeout"/> always takes precedence, and requests that do not
/// implement <see cref="Extensibility.ITimeoutRequest"/> are never subject to a deadline.
/// </remarks>
/// <example>
/// <code>
/// services.AddPulse(c =&gt; c.AddRequestTimeout(TimeSpan.FromSeconds(30)));
/// </code>
/// </example>
/// <seealso cref="Extensibility.ITimeoutRequest"/>
public sealed class TimeoutRequestInterceptorOptions
{
    /// <summary>
    /// Gets or sets the global fallback timeout applied to <see cref="Extensibility.ITimeoutRequest"/>
    /// implementations that return <see langword="null"/> from <see cref="Extensibility.ITimeoutRequest.Timeout"/>.
    /// Requests that do not implement <see cref="Extensibility.ITimeoutRequest"/> are always passed through
    /// regardless of this value.
    /// When <see langword="null"/> (default), requests with a <see langword="null"/>
    /// <see cref="Extensibility.ITimeoutRequest.Timeout"/> are not subject to any deadline.
    /// </summary>
    public TimeSpan? GlobalTimeout { get; set; }
}
