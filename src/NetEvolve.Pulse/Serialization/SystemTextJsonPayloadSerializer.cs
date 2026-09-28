namespace NetEvolve.Pulse.Serialization;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Default implementation of <see cref="IPayloadSerializer"/> using System.Text.Json.
/// </summary>
/// <remarks>
/// <para><strong>Thread Safety:</strong></para>
/// This implementation is thread-safe and can be used concurrently from multiple threads.
/// <para><strong>Serialization Options:</strong></para>
/// Uses the provided <see cref="JsonSerializerOptions"/> from the options pattern,
/// or a new default <see cref="JsonSerializerOptions"/> instance when none are configured.
/// <para><strong>NativeAOT and Trimming:</strong></para>
/// Contracts are always resolved through <see cref="JsonSerializerOptions.GetTypeInfo(Type)"/>, so no
/// reflection-based <see cref="JsonSerializer"/> overload is referenced. While reflection-based serialization
/// is enabled (the default for JIT-compiled applications), options without a
/// <see cref="JsonSerializerOptions.TypeInfoResolver"/> fall back to the reflection resolver. Trimmed and
/// NativeAOT applications MUST add a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// for their payload types to <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>.
/// </remarks>
internal sealed class SystemTextJsonPayloadSerializer : IPayloadSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTextJsonPayloadSerializer"/> class.
    /// </summary>
    /// <param name="options">The JSON serializer options.</param>
    public SystemTextJsonPayloadSerializer(IOptions<JsonSerializerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = WithDefaultResolver(options.Value ?? new JsonSerializerOptions());
    }

    /// <inheritdoc />
    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, GetTypeInfo<T>());

    /// <inheritdoc />
    public string Serialize(object value, Type type) => JsonSerializer.Serialize(value, _options.GetTypeInfo(type));

    /// <inheritdoc />
    public byte[] SerializeToBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, GetTypeInfo<T>());

    /// <inheritdoc />
    public T? Deserialize<T>(string payload) => JsonSerializer.Deserialize(payload, GetTypeInfo<T>());

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] payload) => JsonSerializer.Deserialize(payload, GetTypeInfo<T>());

    private JsonTypeInfo<T> GetTypeInfo<T>() => (JsonTypeInfo<T>)_options.GetTypeInfo(typeof(T));

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Guarded by JsonSerializer.IsReflectionEnabledByDefault, a feature switch that is false in trimmed and NativeAOT applications, so the reflection resolver is never rooted there."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Guarded by JsonSerializer.IsReflectionEnabledByDefault, a feature switch that is false in trimmed and NativeAOT applications, so the reflection resolver is never rooted there."
    )]
    private static JsonSerializerOptions WithDefaultResolver(JsonSerializerOptions options)
    {
        if (options.TypeInfoResolver is not null || !JsonSerializer.IsReflectionEnabledByDefault)
        {
            return options;
        }

        // Copy instead of mutating, the configured instance may be shared or already read-only.
        return new JsonSerializerOptions(options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    }
}
