namespace NetEvolve.Pulse;

using System;
using System.Collections.Generic;
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
/// HTTP endpoints for inspecting outbox messages and for replaying or dismissing dead-letter messages.
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
    /// <item><description><c>GET {BasePath}/messages?pageSize=50&amp;page=0&amp;status=</c> — paginated, read-only list of messages in any status, optionally filtered by <see cref="OutboxMessageStatus"/>.</description></item>
    /// <item><description><c>GET {BasePath}/messages/{{id:guid}}</c> — a single message in any status.</description></item>
    /// <item><description><c>POST {BasePath}/messages/{{id:guid}}/replay</c> — alias of <c>POST {BasePath}/dead-letters/{{id:guid}}/replay</c>; only dead-letter messages can be replayed.</description></item>
    /// <item><description><c>GET {BasePath}/dead-letters?pageSize=50&amp;page=0</c> — paginated dead-letter messages.</description></item>
    /// <item><description><c>GET {BasePath}/dead-letters/count</c> — dead-letter message count.</description></item>
    /// <item><description><c>GET {BasePath}/dead-letters/{{id:guid}}</c> — a single dead-letter message.</description></item>
    /// <item><description><c>POST {BasePath}/dead-letters/{{id:guid}}/replay</c> — replays a single dead-letter message.</description></item>
    /// <item><description><c>POST {BasePath}/dead-letters/{{id:guid}}/dismiss</c> — dismisses (permanently deletes) a single dead-letter message.</description></item>
    /// <item><description><c>POST {BasePath}/dead-letters/replay-all</c> — replays all dead-letter messages.</description></item>
    /// </list>
    /// <para><strong>Status codes:</strong></para>
    /// Paging values outside their valid range (<c>pageSize</c> below 1, negative <c>page</c>, or an offset beyond
    /// <see cref="int.MaxValue"/>) and undefined <c>status</c> values are rejected with <c>400 Bad Request</c>.
    /// Single-message routes return <c>404 Not Found</c> when no matching message exists, and replay and dismiss
    /// return <c>204 No Content</c> on success.
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
        _ = group.MapGet("/messages", GetMessagesAsync);
        _ = group.MapGet("/messages/{id:guid}", GetMessageAsync);
        _ = group.MapPost("/messages/{id:guid}/replay", ReplayMessageAsync);
        _ = group.MapGet("/dead-letters", GetDeadLetterMessagesAsync);
        _ = group.MapGet("/dead-letters/count", GetDeadLetterCountAsync);
        _ = group.MapGet("/dead-letters/{id:guid}", GetDeadLetterMessageAsync);
        _ = group.MapPost("/dead-letters/{id:guid}/replay", ReplayMessageAsync);
        _ = group.MapPost("/dead-letters/{id:guid}/dismiss", DismissMessageAsync);
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

    private static async Task<IResult> GetMessagesAsync(
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken,
        int pageSize = 50,
        int page = 0,
        OutboxMessageStatus? status = null
    )
    {
        var errors = ValidatePaging(pageSize, page);
        if (status is { } value && !Enum.IsDefined(value))
        {
            errors ??= new Dictionary<string, string[]>(StringComparer.Ordinal);
            errors[nameof(status)] = ["The status is not a defined outbox message status."];
        }

        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var messages = await outboxManagement
            .GetMessagesAsync(pageSize, page, status, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Json(messages, OutboxMessageSerializerOptions);
    }

    private static async Task<IResult> GetMessageAsync(
        Guid id,
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken
    )
    {
        var message = await outboxManagement.GetMessageAsync(id, cancellationToken).ConfigureAwait(false);

        return message is null ? TypedResults.NotFound() : TypedResults.Json(message, OutboxMessageSerializerOptions);
    }

    private static async Task<IResult> GetDeadLetterMessagesAsync(
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken,
        int pageSize = 50,
        int page = 0
    )
    {
        if (ValidatePaging(pageSize, page) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

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

    private static async Task<IResult> DismissMessageAsync(
        Guid id,
        IOutboxManagement outboxManagement,
        CancellationToken cancellationToken
    )
    {
        var dismissed = await outboxManagement.DismissMessageAsync(id, cancellationToken).ConfigureAwait(false);

        return dismissed ? TypedResults.NoContent() : TypedResults.NotFound();
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
    /// Validates the <c>pageSize</c> and <c>page</c> query parameters, so invalid values are answered
    /// with <c>400 Bad Request</c> instead of surfacing the provider's <see cref="ArgumentOutOfRangeException"/>
    /// as <c>500 Internal Server Error</c>.
    /// </summary>
    /// <param name="pageSize">The requested page size; must be greater than zero.</param>
    /// <param name="page">The requested zero-based page index; must not be negative.</param>
    /// <returns>The validation errors keyed by parameter name, or <see langword="null"/> when both values are valid.</returns>
    private static Dictionary<string, string[]>? ValidatePaging(int pageSize, int page)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (pageSize <= 0)
        {
            errors[nameof(pageSize)] = ["The page size must be greater than zero."];
        }

        if (page < 0)
        {
            errors[nameof(page)] = ["The page must not be negative."];
        }
        else if (pageSize > 0 && page > int.MaxValue / pageSize)
        {
            errors[nameof(page)] = ["The requested page is too large for the given page size."];
        }

        return errors.Count == 0 ? null : errors;
    }

    /// <summary>
    /// Represents the result payload of the replay-all dead-letter operation.
    /// </summary>
    /// <param name="Count">The number of dead-letter messages that were reset for replay.</param>
    private sealed record OutboxReplayAllResult(int Count);
}
