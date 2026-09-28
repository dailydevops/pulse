namespace NetEvolve.Pulse;

/// <summary>
/// Provides configuration options for the command dead letter inspector endpoints mapped via
/// <see cref="CommandDeadLetterInspectorEndpoints.MapCommandDeadLetterInspector"/>.
/// </summary>
public sealed class CommandDeadLetterInspectorOptions
{
    /// <summary>
    /// Gets or sets the base route path under which the command dead letter inspector endpoints
    /// are mapped. Defaults to <c>/pulse/commands</c>.
    /// </summary>
    public string BasePath { get; set; } = "/pulse/commands";

    /// <summary>
    /// Gets or sets the endpoint group name assigned to the mapped route group.
    /// Defaults to <c>Pulse Command Dead Letter Inspector</c>.
    /// </summary>
    public string RouteGroupName { get; set; } = "Pulse Command Dead Letter Inspector";
}
