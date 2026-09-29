namespace NetEvolve.Pulse.Internals;

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

/// <summary>
/// Serializes the items streamed by <c>MapStreamQuery</c> with the application's <see cref="HttpJsonOptions"/>.
/// </summary>
/// <remarks>
/// The application's <see cref="JsonSerializerOptions"/> are copied once per instance with
/// <see cref="JsonSerializerOptions.WriteIndented"/> set to <see langword="false"/>, so every item is a single-line
/// JSON text that fits one NDJSON line or one SSE <c>data:</c> line. Contracts are resolved through
/// <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>. The configured instance is never mutated.
/// </remarks>
internal static class PulseStreamJsonOptions
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> DerivedOptions = new();

    /// <summary>
    /// Serializes <paramref name="item"/> to a single-line UTF-8 JSON text.
    /// </summary>
    /// <typeparam name="T">The declared item type.</typeparam>
    /// <param name="item">The item to serialize.</param>
    /// <param name="jsonOptions">The application's HTTP JSON options.</param>
    /// <returns>The UTF-8 encoded JSON text, without line breaks.</returns>
    public static byte[] SerializeToUtf8Bytes<T>(T item, IOptions<HttpJsonOptions> jsonOptions) =>
        JsonSerializer.SerializeToUtf8Bytes(item, ResolveTypeInfo(item, jsonOptions));

    /// <summary>
    /// Serializes <paramref name="item"/> to a single-line JSON text.
    /// </summary>
    /// <typeparam name="T">The declared item type.</typeparam>
    /// <param name="item">The item to serialize.</param>
    /// <param name="jsonOptions">The application's HTTP JSON options.</param>
    /// <returns>The JSON text, without line breaks.</returns>
    public static string Serialize<T>(T item, IOptions<HttpJsonOptions> jsonOptions) =>
        JsonSerializer.Serialize(item, ResolveTypeInfo(item, jsonOptions));

    private static JsonTypeInfo ResolveTypeInfo<T>(T item, IOptions<HttpJsonOptions> jsonOptions)
    {
        var options = DerivedOptions.GetValue(jsonOptions.Value.SerializerOptions, Create);
        var typeInfo = options.GetTypeInfo(typeof(T));

        // Same rule as TypedResults.Ok and ServerSentEventsResult: fall back to the runtime type unless the
        // declared contract already covers every possible runtime type.
        if (item is not null && !CoversRuntimeType(typeInfo, item.GetType()))
        {
            typeInfo = options.GetTypeInfo(item.GetType());
        }

        return typeInfo;
    }

    private static bool CoversRuntimeType(JsonTypeInfo typeInfo, Type runtimeType) =>
        typeInfo.Type == runtimeType
        || typeInfo.Type.IsSealed
        || typeInfo.Type.IsValueType
        || typeInfo.PolymorphismOptions is not null;

    private static JsonSerializerOptions Create(JsonSerializerOptions configured) =>
        // Copy instead of mutating, the configured instance is shared and may already be read-only.
        new(configured) { WriteIndented = false };
}
