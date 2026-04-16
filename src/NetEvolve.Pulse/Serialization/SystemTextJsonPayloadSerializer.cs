namespace NetEvolve.Pulse.Serialization;

using System.Text;
using System.Text.Json;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Default implementation of <see cref="IPayloadSerializer"/> using System.Text.Json.
/// </summary>
/// <remarks>
/// <para><strong>Thread Safety:</strong></para>
/// This implementation is thread-safe and can be used concurrently from multiple threads.
/// <para><strong>Serialization Options:</strong></para>
/// Uses the provided <see cref="JsonSerializerOptions"/> or defaults to <see cref="JsonSerializerOptions.Default"/> when none are specified.
/// </remarks>
internal sealed class SystemTextJsonPayloadSerializer : IPayloadSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTextJsonPayloadSerializer"/> class.
    /// </summary>
    /// <param name="options">Optional JSON serializer options. When null, defaults to <see cref="JsonSerializerOptions.Default"/>.</param>
    public SystemTextJsonPayloadSerializer(JsonSerializerOptions? options = null) =>
        _options = options ?? JsonSerializerOptions.Default;

    /// <inheritdoc />
    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, _options);

    /// <inheritdoc />
    public string Serialize(object value, Type type) => JsonSerializer.Serialize(value, type, _options);

    /// <inheritdoc />
    public byte[] SerializeToBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, _options);

    /// <inheritdoc />
    public T? Deserialize<T>(string payload) => JsonSerializer.Deserialize<T>(payload, _options);

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] payload) => JsonSerializer.Deserialize<T>(payload, _options);
}
