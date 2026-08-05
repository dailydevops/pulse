namespace NetEvolve.Pulse;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetEvolve.Pulse.AspNetCore.Internals;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to map read/administrative
/// HTTP endpoints for inspecting and replaying outbox dead-letter messages.
/// </summary>
public static class OutboxInspectorEndpoints
{
    /// <summary>
    /// Maps the outbox inspector endpoints, backed by <see cref="IOutboxManagement"/>, as a route
    /// group under <see cref="OutboxInspectorOptions.BasePath"/>.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the endpoints to.</param>
    /// <param name="configure">
    /// An optional delegate to configure the <see cref="OutboxInspectorOptions"/> used to map the
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
    /// <item><description><c>GET {BasePath}/stats</c> — outbox statistics.</description></item>
    /// <item><description><c>GET {BasePath}/dead-letters</c> — paginated dead-letter messages.</description></item>
    /// <item><description><c>GET {BasePath}/dead-letters/count</c> — dead-letter message count.</description></item>
    /// <item><description><c>GET {BasePath}/dead-letters/{{id:guid}}</c> — a single dead-letter message.</description></item>
    /// <item><description><c>POST {BasePath}/dead-letters/{{id:guid}}/replay</c> — replays a single dead-letter message.</description></item>
    /// <item><description><c>POST {BasePath}/dead-letters/replay-all</c> — replays all dead-letter messages.</description></item>
    /// </list>
    /// <para><strong>Authorization:</strong></para>
    /// No authorization is applied by this method. Callers are responsible for securing the
    /// returned route group, for example via <c>RequireAuthorization()</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Default base path "/pulse/outbox"
    /// app.MapOutboxInspector();
    ///
    /// // Custom base path and group name, with authorization applied by the caller
    /// app.MapOutboxInspector(options =>
    /// {
    ///     options.BasePath = "/admin/outbox";
    ///     options.RouteGroupName = "Admin Outbox Inspector";
    /// }).RequireAuthorization();
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapOutboxInspector(
        [NotNull] this IEndpointRouteBuilder endpoints,
        Action<OutboxInspectorOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new OutboxInspectorOptions();
        configure?.Invoke(options);

        var group = endpoints.MapGroup(options.BasePath).WithGroupName(options.RouteGroupName);

        _ = group.MapGet("/stats", GetStatisticsAsync);
        _ = group.MapGet("/dead-letters", GetDeadLetterMessagesAsync);
        _ = group.MapGet("/dead-letters/count", GetDeadLetterCountAsync);
        _ = group.MapGet("/dead-letters/{id:guid}", GetDeadLetterMessageAsync);
        _ = group.MapPost("/dead-letters/{id:guid}/replay", ReplayMessageAsync);
        _ = group.MapPost("/dead-letters/replay-all", ReplayAllDeadLetterAsync);

        return group;
    }

    /// <summary>
    /// JSON options used to write <see cref="OutboxMessage"/> responses, since the type of
    /// <see cref="OutboxMessage.EventType"/> is not serializable by <see cref="JsonSerializer"/> by default.
    /// </summary>
    private static readonly JsonSerializerOptions OutboxMessageSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new TypeJsonConverter() },
    };

    private static async Task<IResult> GetStatisticsAsync(
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await outboxManagement.GetStatisticsAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetDeadLetterMessagesAsync(
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken,
        int pageSize = 50,
        int page = 0
    )
    {
        var messages = await outboxManagement
            .GetDeadLetterMessagesAsync(pageSize, page, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Json(messages, OutboxMessageSerializerOptions);
    }

    private static async Task<IResult> GetDeadLetterCountAsync(
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await outboxManagement.GetDeadLetterCountAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetDeadLetterMessageAsync(
        Guid id,
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken
    )
    {
        var message = await outboxManagement.GetDeadLetterMessageAsync(id, cancellationToken).ConfigureAwait(false);

        return message is null ? TypedResults.NotFound() : TypedResults.Json(message, OutboxMessageSerializerOptions);
    }

    private static async Task<IResult> ReplayMessageAsync(
        Guid id,
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken
    )
    {
        var replayed = await outboxManagement.ReplayMessageAsync(id, cancellationToken).ConfigureAwait(false);

        return replayed ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<IResult> ReplayAllDeadLetterAsync(
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken
    )
    {
        var count = await outboxManagement.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new OutboxReplayAllResult(count));
    }

    /// <summary>
    /// Represents the result payload of the replay-all dead-letter operation.
    /// </summary>
    /// <param name="Count">The number of dead-letter messages that were reset for replay.</param>
    private sealed record OutboxReplayAllResult(int Count);
}
