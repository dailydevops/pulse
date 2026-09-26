namespace NetEvolve.Pulse.AspNetCore.Internals;

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

/// <summary>
/// Resolves the JSON contracts used by the outbox, audit and command dead letter inspector endpoints from the
/// application's <see cref="HttpJsonOptions"/>.
/// </summary>
/// <remarks>
/// The application's <see cref="JsonSerializerOptions"/> are copied once per instance and extended with
/// <see cref="TypeJsonConverter"/>, string enum converters for the Pulse enums and
/// <see cref="PulseInspectorJsonSerializerContext"/> appended to <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>.
/// Naming policy, converters and other settings of the application therefore apply, application converters take
/// precedence over the appended ones, and the Pulse-owned models stay serializable without reflection.
/// The configured instance is never mutated.
/// </remarks>
internal static class PulseInspectorJsonOptions
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> DerivedOptions = new();

    /// <summary>
    /// Gets the <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/> based on the application's HTTP JSON options.
    /// </summary>
    /// <typeparam name="T">The type of the response model.</typeparam>
    /// <param name="jsonOptions">The application's HTTP JSON options.</param>
    /// <returns>The contract for <typeparamref name="T"/>.</returns>
    public static JsonTypeInfo<T> GetTypeInfo<T>(IOptions<HttpJsonOptions> jsonOptions) =>
        (JsonTypeInfo<T>)DerivedOptions.GetValue(jsonOptions.Value.SerializerOptions, Create).GetTypeInfo(typeof(T));

    private static JsonSerializerOptions Create(JsonSerializerOptions configured)
    {
        // Copy instead of mutating, the configured instance is shared and may already be read-only.
        var options = new JsonSerializerOptions(configured);
        options.Converters.Add(new TypeJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter<OutboxMessageStatus>());
        options.Converters.Add(new JsonStringEnumConverter<AuditResult>());
        options.Converters.Add(new JsonStringEnumConverter<CommandDeadLetterStatus>());
        options.TypeInfoResolverChain.Add(PulseInspectorJsonSerializerContext.Default);
        return options;
    }
}
