namespace NetEvolve.Pulse;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to map read/administrative
/// HTTP endpoints for inspecting and replaying command dead-letter entries.
/// </summary>
public static class CommandDeadLetterInspectorEndpoints
{
    private const int MaxCount = 1000;

    /// <summary>
    /// Maps the command dead letter inspector endpoints, backed by
    /// <see cref="ICommandDeadLetterManagement"/>, as a route group under
    /// <see cref="CommandDeadLetterInspectorOptions.BasePath"/>.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the endpoints to.</param>
    /// <param name="configure">
    /// An optional delegate to configure the <see cref="CommandDeadLetterInspectorOptions"/> used
    /// to map the endpoints. When <see langword="null"/>, the default options are used.
    /// </param>
    /// <returns>
    /// An <see cref="IEndpointConventionBuilder"/> representing the mapped route group, which
    /// callers can further configure, for example to require authorization.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Endpoints:</strong></para>
    /// <list type="bullet">
    /// <item><description><c>GET {BasePath}/stats</c> — dead letter statistics.</description></item>
    /// <item><description><c>GET {BasePath}/entries?count=50&amp;skip=0</c> — pending dead-letter entries, oldest first.</description></item>
    /// <item><description><c>GET {BasePath}/entries/{{id:guid}}</c> — a single dead-letter entry, or <c>404</c> if not found.</description></item>
    /// <item><description><c>POST {BasePath}/entries/{{id:guid}}/replay</c> — replays a dead-letter entry, or <c>404</c> if not found.</description></item>
    /// <item><description><c>POST {BasePath}/entries/{{id:guid}}/dismiss</c> — dismisses a dead-letter entry, or <c>404</c> if not found.</description></item>
    /// </list>
    /// <para>
    /// <c>count</c> must be between 1 and 1000 and <c>skip</c> must not be negative; otherwise the endpoint
    /// returns <c>400</c> with a validation problem response.
    /// </para>
    /// <para><strong>Authorization:</strong></para>
    /// No authorization is applied by this method. Callers are responsible for securing the
    /// returned route group, for example via <c>RequireAuthorization()</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Default base path "/pulse/commands"
    /// app.MapCommandDeadLetterInspector();
    ///
    /// // Custom base path and group name, with authorization applied by the caller
    /// app.MapCommandDeadLetterInspector(options =>
    /// {
    ///     options.BasePath = "/admin/commands";
    ///     options.RouteGroupName = "Admin Command Dead Letter Inspector";
    /// }).RequireAuthorization();
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapCommandDeadLetterInspector(
        [NotNull] this IEndpointRouteBuilder endpoints,
        Action<CommandDeadLetterInspectorOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new CommandDeadLetterInspectorOptions();
        configure?.Invoke(options);

        var group = endpoints.MapGroup(options.BasePath).WithGroupName(options.RouteGroupName);

        _ = group.MapGet("/stats", GetStatisticsAsync);
        _ = group.MapGet("/entries", GetPendingEntriesAsync);
        _ = group.MapGet("/entries/{id:guid}", GetEntryAsync);
        _ = group.MapPost("/entries/{id:guid}/replay", ReplayEntryAsync);
        _ = group.MapPost("/entries/{id:guid}/dismiss", DismissEntryAsync);

        return group;
    }

    private static async Task<IResult> GetStatisticsAsync(
        ICommandDeadLetterManagement commandDeadLetterManagement,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await commandDeadLetterManagement.GetStatisticsAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetPendingEntriesAsync(
        ICommandDeadLetterManagement commandDeadLetterManagement,
        CancellationToken cancellationToken,
        int count = 50,
        int skip = 0
    )
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (count is <= 0 or > MaxCount)
        {
            errors[nameof(count)] = [$"Must be between 1 and {MaxCount}."];
        }

        if (skip < 0)
        {
            errors[nameof(skip)] = ["Must not be negative."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(
            await commandDeadLetterManagement.GetPendingAsync(count, skip, cancellationToken).ConfigureAwait(false)
        );
    }

    private static async Task<IResult> GetEntryAsync(
        Guid id,
        ICommandDeadLetterManagement commandDeadLetterManagement,
        CancellationToken cancellationToken
    )
    {
        var entry = await commandDeadLetterManagement.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);

        return entry is null ? TypedResults.NotFound() : TypedResults.Ok(entry);
    }

    private static async Task<IResult> ReplayEntryAsync(
        Guid id,
        ICommandDeadLetterManagement commandDeadLetterManagement,
        CancellationToken cancellationToken
    )
    {
        // Catch only the dedicated exception: replay runs the user's command handler, whose own
        // KeyNotFoundException must not be reported as a missing entry.
        try
        {
            await commandDeadLetterManagement.ReplayAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (CommandDeadLetterEntryNotFoundException)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DismissEntryAsync(
        Guid id,
        ICommandDeadLetterManagement commandDeadLetterManagement,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await commandDeadLetterManagement.DismissAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (CommandDeadLetterEntryNotFoundException)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.NoContent();
    }
}
