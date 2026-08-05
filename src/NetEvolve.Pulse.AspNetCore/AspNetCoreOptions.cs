namespace NetEvolve.Pulse;

/// <summary>
/// Provides configuration options for the ASP.NET Core Minimal API integration of the Pulse
/// mediator, such as the endpoints mapped via <see cref="EndpointRouteBuilderExtensions"/>.
/// </summary>
public sealed class AspNetCoreOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether OpenAPI metadata (summary, description, tags,
    /// and produced response types) is automatically applied to endpoints mapped via
    /// <see cref="EndpointRouteBuilderExtensions"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool OpenApiMetadataEnabled { get; set; }
}
