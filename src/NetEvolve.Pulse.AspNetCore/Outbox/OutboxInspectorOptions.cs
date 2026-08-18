namespace NetEvolve.Pulse.Outbox;

/// <summary>
/// Provides configuration options for the outbox inspector endpoints mapped via
/// <see cref="OutboxInspectorEndpoints.MapOutboxInspector"/>.
/// </summary>
public sealed class OutboxInspectorOptions
{
    /// <summary>
    /// Gets or sets the base route path under which the outbox inspector endpoints are mapped.
    /// Defaults to <c>/pulse/outbox</c>.
    /// </summary>
    public string BasePath { get; set; } = "/pulse/outbox";

    /// <summary>
    /// Gets or sets the endpoint group name assigned to the mapped route group.
    /// Defaults to <c>Pulse Outbox Inspector</c>.
    /// </summary>
    public string RouteGroupName { get; set; } = "Pulse Outbox Inspector";
}
