namespace NetEvolve.Pulse;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetEvolve.Pulse.AspNetCore.Internals;
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
    /// <item><description><c>GET {BasePath}/entries/{id}</c> — a single audit record, or <c>404</c> when not found.</description></item>
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
    [RequiresUnreferencedCode(
        "Minimal API endpoint mapping uses RequestDelegateFactory, which reflects over the handler signature and the bound request types."
    )]
    [RequiresDynamicCode(
        "Minimal API endpoint mapping can generate code at runtime to bind parameters and write results."
    )]
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
        _ = group.MapGet("/entries/{id:guid}", GetEntryAsync);

        return group;
    }

    private static async Task<IResult> GetStatisticsAsync(
        IAuditManagement auditManagement,
        CancellationToken cancellationToken
    ) =>
        TypedResults.Json(
            await auditManagement.GetStatisticsAsync(cancellationToken).ConfigureAwait(false),
            PulseInspectorJsonSerializerContext.Default.AuditStatistics
        );

    private static async Task<IResult> GetEntriesAsync(
        [AsParameters] AuditEntriesQuery query,
        IAuditManagement auditManagement,
        CancellationToken cancellationToken
    )
    {
        var errors = query.Validate();
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Json(
            await auditManagement.QueryAsync(query.ToFilter(), cancellationToken).ConfigureAwait(false),
            PulseInspectorJsonSerializerContext.Default.IReadOnlyListAuditRecord
        );
    }

    private static async Task<IResult> GetEntryAsync(
        Guid id,
        IAuditManagement auditManagement,
        CancellationToken cancellationToken
    )
    {
        var record = await auditManagement.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return record is null
            ? TypedResults.NotFound()
            : TypedResults.Json(record, PulseInspectorJsonSerializerContext.Default.AuditRecord);
    }

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

        /// <summary>
        /// The largest page size a single request may ask for, so one call cannot dump the whole audit table.
        /// </summary>
        private const int MaxTake = 1000;

        /// <summary>
        /// Rejects values the persistence providers cannot handle consistently: SQL Server rejects a
        /// <c>FETCH</c>/<c>OFFSET</c> count of zero or less, PostgreSQL and MySQL reject a negative
        /// <c>LIMIT</c>/<c>OFFSET</c>, and SQLite treats <c>LIMIT -1</c> as unbounded. Page sizes above
        /// <see cref="MaxTake"/> are rejected as well.
        /// </summary>
        /// <returns>The validation errors keyed by query parameter name; empty when valid.</returns>
        public Dictionary<string, string[]> Validate()
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

            if (Take is <= 0 or > MaxTake)
            {
                errors["take"] = [$"The value must be between 1 and {MaxTake}."];
            }

            if (Skip < 0)
            {
                errors["skip"] = ["The value must not be negative."];
            }

            if (From > To)
            {
                errors["from"] = ["The value must not be later than 'to'."];
            }

            return errors;
        }

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
