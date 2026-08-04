namespace NetEvolve.Pulse;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to map read-only HTTP
/// endpoints for inspecting audit trail records.
/// </summary>
public static class AuditInspectorEndpoints
{
    /// <summary>
    /// Maps the audit inspector endpoints, backed by <see cref="IAuditManagement"/>, as a route
    /// group under <see cref="AuditInspectorOptions.BasePath"/>.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the endpoints to.</param>
    /// <param name="configure">
    /// An optional delegate to configure the <see cref="AuditInspectorOptions"/> used to map the
    /// endpoints. When <see langword="null"/>, the default options are used.
    /// </param>
    /// <returns>
    /// An <see cref="IEndpointConventionBuilder"/> representing the mapped route group, which
    /// callers can further configure, for example to require authorization.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Endpoints:</strong></para>
    /// <list type="bullet">
    /// <item><description><c>GET {BasePath}/stats</c> — aggregate audit result counts.</description></item>
    /// <item><description><c>GET {BasePath}/entries</c> — paginated, filterable audit records.</description></item>
    /// </list>
    /// <para><strong>Read-only:</strong></para>
    /// This method maps strictly read-only endpoints. No replay, dismiss, or other mutating
    /// operations are exposed, and no built-in authorization is applied. Callers are responsible
    /// for securing the returned route group, for example via <c>RequireAuthorization()</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Default base path "/pulse/audit"
    /// app.MapAuditInspector();
    ///
    /// // Custom base path and group name, with authorization applied by the caller
    /// app.MapAuditInspector(options =>
    /// {
    ///     options.BasePath = "/admin/audit";
    ///     options.RouteGroupName = "Admin Audit Inspector";
    /// }).RequireAuthorization();
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapAuditInspector(
        [NotNull] this IEndpointRouteBuilder endpoints,
        Action<AuditInspectorOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new AuditInspectorOptions();
        configure?.Invoke(options);

        var group = endpoints.MapGroup(options.BasePath).WithGroupName(options.RouteGroupName);

        _ = group.MapGet("/stats", GetStatisticsAsync);
        _ = group.MapGet("/entries", GetEntriesAsync);

        return group;
    }

    private static async Task<IResult> GetStatisticsAsync(
        IAuditManagement auditManagement,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await auditManagement.GetStatisticsAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetEntriesAsync(
        [AsParameters] AuditEntriesQuery query,
        IAuditManagement auditManagement,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await auditManagement.QueryAsync(query.ToFilter(), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Query-string binding target for <c>GET {BasePath}/entries</c>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="AuditFilter"/>, <see cref="Take"/> and <see cref="Skip"/> are nullable
    /// here. Minimal API <c>[AsParameters]</c> binding treats non-nullable value-type properties
    /// as required regardless of any C# property initializer, so binding <see cref="AuditFilter"/>
    /// directly would make <c>take</c> and <c>skip</c> mandatory query parameters and reject every
    /// request that omits them. This type restores <see cref="AuditFilter"/>'s own defaults for
    /// omitted values via <see cref="ToFilter"/>.
    /// </remarks>
    // Properties are set by ASP.NET Core's [AsParameters] query-string model binder via
    // reflection, which static analysis cannot observe, hence they appear "unassigned" and their
    // setters appear "unused".
#pragma warning disable S3459 // Remove unassigned auto-property, or set its value.
#pragma warning disable S1144 // Remove the unused private set accessor.
    private sealed class AuditEntriesQuery
    {
        public string? CommandType { get; set; }

        public string? UserId { get; set; }

        public DateTimeOffset? From { get; set; }

        public DateTimeOffset? To { get; set; }

        public AuditResult? Result { get; set; }

        public int? Take { get; set; }

        public int? Skip { get; set; }

        public AuditFilter ToFilter()
        {
            var filter = new AuditFilter
            {
                CommandType = CommandType,
                UserId = UserId,
                From = From,
                To = To,
                Result = Result,
            };

            if (Take.HasValue)
            {
                filter.Take = Take.Value;
            }

            if (Skip.HasValue)
            {
                filter.Skip = Skip.Value;
            }

            return filter;
        }
    }
#pragma warning restore S1144
#pragma warning restore S3459
}
