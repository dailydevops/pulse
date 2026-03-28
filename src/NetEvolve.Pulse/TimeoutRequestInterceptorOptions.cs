namespace NetEvolve.Pulse;

/// <summary>
/// Options for the built-in request timeout interceptor registered via <c>AddRequestTimeout()</c>.
/// </summary>
/// <remarks>
/// <para><strong>Global Timeout:</strong></para>
/// When <see cref="GlobalTimeout"/> is set, all requests that do not implement
/// <see cref="Extensibility.ITimeoutRequest"/> are also subject to the global deadline.
/// Requests that implement <see cref="Extensibility.ITimeoutRequest"/> always use their own
/// <see cref="Extensibility.ITimeoutRequest.Timeout"/> value, which takes precedence over
/// <see cref="GlobalTimeout"/>.
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
    /// Gets or sets the global fallback timeout applied to all requests that do not implement
    /// <see cref="Extensibility.ITimeoutRequest"/>.
    /// When <see langword="null"/> (default), requests without an explicit timeout are not affected.
    /// </summary>
    public TimeSpan? GlobalTimeout { get; set; }
}
