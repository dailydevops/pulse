namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines the contract for serializing and deserializing payloads within the Pulse pipeline.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// <see cref="IPayloadSerializer"/> decouples Pulse internals from any concrete serialization library,
/// allowing consumers to substitute System.Text.Json, Newtonsoft.Json, MessagePack, or any other
/// serializer without modifying Pulse source code.
/// <para><strong>Usage:</strong></para>
/// This interface is consumed by payload-writing components such as the Command Dead Letter Store,
/// Audit Trail interceptor, and Outbox payload handling. Register a custom implementation via the
/// dependency-injection container before those components are built.
/// <para><strong>Implementation Guidelines:</strong></para>
/// <list type="bullet">
/// <item><description>Implementations MUST be thread-safe — the same instance may be called concurrently from multiple pipeline stages.</description></item>
/// <item><description>Implementations MUST NOT return <see langword="null"/> from <c>Serialize</c> overloads; return an empty string or empty array instead.</description></item>
/// <item><description>Implementations SHOULD propagate serialization exceptions rather than swallowing them, so the pipeline can apply appropriate error handling.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Custom implementation using Newtonsoft.Json
/// public sealed class NewtonsoftJsonPayloadSerializer : IPayloadSerializer
/// {
///     public string Serialize&lt;T&gt;(T value) =&gt; JsonConvert.SerializeObject(value);
///     public byte[] SerializeToBytes&lt;T&gt;(T value) =&gt; Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
///     public T? Deserialize&lt;T&gt;(string payload) =&gt; JsonConvert.DeserializeObject&lt;T&gt;(payload);
///     public T? Deserialize&lt;T&gt;(byte[] payload) =&gt; JsonConvert.DeserializeObject&lt;T&gt;(Encoding.UTF8.GetString(payload));
///     public string Serialize(object value, Type type) =&gt; JsonConvert.SerializeObject(value, type, null);
///     public object? Deserialize(string payload, Type type) =&gt; JsonConvert.DeserializeObject(payload, type);
/// }
/// </code>
/// </example>
public interface IPayloadSerializer
{
    /// <summary>
    /// Serializes <paramref name="value"/> to its string representation.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="value">The value to serialize. May be <see langword="null"/> when <typeparamref name="T"/> is a reference type.</param>
    /// <returns>A string containing the serialized representation of <paramref name="value"/>.</returns>
    string Serialize<T>(T value);

    /// <summary>
    /// Serializes <paramref name="value"/> to its string representation using the specified <paramref name="type"/>
    /// as the serialization contract, enabling correct polymorphic serialization.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">
    /// The <see cref="Type"/> that describes the serialization contract for <paramref name="value"/>.
    /// Typically the declared (compile-time) type rather than the runtime type of the object.
    /// </param>
    /// <returns>A string containing the serialized representation of <paramref name="value"/>.</returns>
    string Serialize(object value, Type type);

    /// <summary>
    /// Serializes <paramref name="value"/> to a binary representation.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="value">The value to serialize. May be <see langword="null"/> when <typeparamref name="T"/> is a reference type.</param>
    /// <returns>A byte array containing the serialized representation of <paramref name="value"/>.</returns>
    byte[] SerializeToBytes<T>(T value);

    /// <summary>
    /// Deserializes a value of type <typeparamref name="T"/> from the given string <paramref name="payload"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize into.</typeparam>
    /// <param name="payload">The string payload to deserialize.</param>
    /// <returns>
    /// The deserialized value, or <see langword="null"/> if the payload represents a <see langword="null"/> value
    /// or if deserialization yields no result.
    /// </returns>
    T? Deserialize<T>(string payload);

    /// <summary>
    /// Deserializes a value of type <typeparamref name="T"/> from the given binary <paramref name="payload"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize into.</typeparam>
    /// <param name="payload">The binary payload to deserialize.</param>
    /// <returns>
    /// The deserialized value, or <see langword="null"/> if the payload represents a <see langword="null"/> value
    /// or if deserialization yields no result.
    /// </returns>
    T? Deserialize<T>(byte[] payload);

    /// <summary>
    /// Deserializes an object from the given string <paramref name="payload"/> using the specified <paramref name="type"/>
    /// as the deserialization contract.
    /// </summary>
    /// <param name="payload">The string payload to deserialize.</param>
    /// <param name="type">The <see cref="Type"/> to deserialize the payload into.</param>
    /// <returns>
    /// The deserialized object, or <see langword="null"/> if the payload represents a <see langword="null"/> value
    /// or if deserialization yields no result.
    /// </returns>
    object? Deserialize(string payload, Type type);
}
