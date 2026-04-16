namespace NetEvolve.Pulse.Tests.Unit.Serialization;

using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Serialization;
using TUnit.Core;

[TestGroup("Serialization")]
public class SystemTextJsonPayloadSerializerTests
{
    [Test]
    public async Task Serialize_Generic_SerializesValue()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var testObject = new TestData { Id = 42, Name = "Test" };

        var result = serializer.Serialize(testObject);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result).Contains("\"Id\":42");
        _ = await Assert.That(result).Contains("\"Name\":\"Test\"");
    }

    [Test]
#pragma warning disable CA2263 // Prefer generic overload - This test specifically validates the non-generic method
    public async Task Serialize_NonGeneric_SerializesValue()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var testObject = new TestData { Id = 42, Name = "Test" };

        var result = serializer.Serialize(testObject, typeof(TestData));

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result).Contains("\"Id\":42");
        _ = await Assert.That(result).Contains("\"Name\":\"Test\"");
    }
#pragma warning restore CA2263

    [Test]
    public async Task SerializeToBytes_SerializesValueToBytes()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var testObject = new TestData { Id = 42, Name = "Test" };

        var result = serializer.SerializeToBytes(testObject);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result.Length).IsGreaterThan(0);

        var json = Encoding.UTF8.GetString(result);
        _ = await Assert.That(json).Contains("\"Id\":42");
        _ = await Assert.That(json).Contains("\"Name\":\"Test\"");
    }

    [Test]
    public async Task Deserialize_Generic_String_DeserializesValue()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var json = "{\"Id\":42,\"Name\":\"Test\"}";

        var result = serializer.Deserialize<TestData>(json);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(42);
        _ = await Assert.That(result.Name).IsEqualTo("Test");
    }

    [Test]
    public async Task Deserialize_Generic_Bytes_DeserializesValue()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var json = "{\"Id\":42,\"Name\":\"Test\"}";
        var bytes = Encoding.UTF8.GetBytes(json);

        var result = serializer.Deserialize<TestData>(bytes);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(42);
        _ = await Assert.That(result.Name).IsEqualTo("Test");
    }

    [Test]
    public async Task SerializeDeserialize_RoundTrip_Generic()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var original = new TestData { Id = 123, Name = "RoundTrip" };

        var json = serializer.Serialize(original);
        var result = serializer.Deserialize<TestData>(json);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(original.Id);
        _ = await Assert.That(result.Name).IsEqualTo(original.Name);
    }

    [Test]
#pragma warning disable CA2263 // Prefer generic overload - This test specifically validates the non-generic method
    public async Task SerializeDeserialize_RoundTrip_NonGeneric()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var original = new TestData { Id = 123, Name = "RoundTrip" };

        var json = serializer.Serialize(original, typeof(TestData));
        var result = serializer.Deserialize<TestData>(json);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(original.Id);
        _ = await Assert.That(result.Name).IsEqualTo(original.Name);
    }
#pragma warning restore CA2263

    [Test]
    public async Task SerializeToBytes_Deserialize_RoundTrip()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var original = new TestData { Id = 456, Name = "BytesRoundTrip" };

        var bytes = serializer.SerializeToBytes(original);
        var result = serializer.Deserialize<TestData>(bytes);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result!.Id).IsEqualTo(original.Id);
        _ = await Assert.That(result.Name).IsEqualTo(original.Name);
    }

    [Test]
    public async Task Serialize_NullValue_ReturnsNullJson()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        TestData? nullValue = null;

        var result = serializer.Serialize(nullValue);

        _ = await Assert.That(result).IsEqualTo("null");
    }

    [Test]
    public async Task Deserialize_NullJson_ReturnsNull()
    {
        var options = Options.Create(JsonSerializerOptions.Default);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var json = "null";

        var result = serializer.Deserialize<TestData>(json);

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Constructor_WithCustomOptions_UsesCustomOptions()
    {
        var customOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        var options = Options.Create(customOptions);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var testObject = new TestData { Id = 42, Name = "Test" };

        var result = serializer.Serialize(testObject);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result).Contains("\"id\"");
        _ = await Assert.That(result).Contains("\"name\"");
    }

    [Test]
    public async Task Constructor_WithNullOptionsValue_UsesDefaultOptions()
    {
        var options = Options.Create<JsonSerializerOptions>(null!);
        var serializer = new SystemTextJsonPayloadSerializer(options);
        var testObject = new TestData { Id = 42, Name = "Test" };

        var result = serializer.Serialize(testObject);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result).Contains("\"Id\"");
        _ = await Assert.That(result).Contains("\"Name\"");
    }

    private sealed class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
