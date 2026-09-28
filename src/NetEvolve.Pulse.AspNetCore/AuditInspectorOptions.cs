namespace NetEvolve.Pulse;

/// <summary>
/// Provides configuration options for the audit inspector endpoints mapped via
/// <see cref="AuditInspectorEndpoints.MapAuditInspector"/>.
/// </summary>
public sealed class AuditInspectorOptions
{
    /// <summary>
    /// Gets or sets the base route path under which the audit inspector endpoints are mapped.
    /// Defaults to <c>/pulse/audit</c>.
    /// </summary>
    public string BasePath { get; set; } = "/pulse/audit";

    /// <summary>
    /// Gets or sets the endpoint group name assigned to the mapped route group.
    /// Defaults to <c>Pulse Audit Inspector</c>.
    /// </summary>
    public string RouteGroupName { get; set; } = "Pulse Audit Inspector";
}
