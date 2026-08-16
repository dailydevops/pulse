namespace NetEvolve.Pulse.Tests.Unit.Dapr;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using global::Dapr.Client;
using Google.Protobuf;

/// <summary>
/// Minimal <see cref="DaprClient"/> test double that records <see cref="PublishByteEventAsync"/> and
/// <see cref="PublishEventAsync{TData}(string, string, TData, CancellationToken)"/> invocations.
/// All other abstract members are not exercised by <see cref="DaprMessageTransportTests"/> and throw
/// <see cref="NotSupportedException"/> if invoked.
/// </summary>
internal sealed class FakeDaprClient : DaprClient
{
    public string? PublishedPubsubName { get; private set; }

    public string? PublishedTopicName { get; private set; }

    public byte[]? PublishedBytes { get; private set; }

    public string? PublishedContentType { get; private set; }

    public bool PublishByteEventAsyncCalled { get; private set; }

    public bool PublishEventAsyncCalled { get; private set; }

    public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;

    public override Task PublishByteEventAsync(
        string pubsubName,
        string topicName,
        ReadOnlyMemory<byte> data,
        string dataContentType = "application/json",
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        PublishByteEventAsyncCalled = true;
        PublishedPubsubName = pubsubName;
        PublishedTopicName = topicName;
        PublishedBytes = data.ToArray();
        PublishedContentType = dataContentType;

        return Task.CompletedTask;
    }

    public override Task PublishEventAsync<TData>(
        string pubsubName,
        string topicName,
        TData data,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        PublishEventAsyncCalled = true;
        return Task.CompletedTask;
    }

    public override Task PublishEventAsync<TData>(
        string pubsubName,
        string topicName,
        TData data,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task PublishEventAsync(
        string pubsubName,
        string topicName,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task PublishEventAsync(
        string pubsubName,
        string topicName,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<BulkPublishResponse<TValue>> BulkPublishEventAsync<TValue>(
        string pubsubName,
        string topicName,
        IReadOnlyList<TValue> events,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task InvokeBindingAsync<TRequest>(
        string bindingName,
        string operation,
        TRequest data,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<TResponse> InvokeBindingAsync<TRequest, TResponse>(
        string bindingName,
        string operation,
        TRequest data,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<BindingResponse> InvokeBindingAsync(
        BindingRequest request,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override HttpRequestMessage CreateInvokeMethodRequest(
        HttpMethod httpMethod,
        string appId,
        string methodName
    ) => throw new NotSupportedException();

    public override HttpRequestMessage CreateInvokeMethodRequest(
        HttpMethod httpMethod,
        string appId,
        string methodName,
        IReadOnlyCollection<KeyValuePair<string, string>> queryStringParameters
    ) => throw new NotSupportedException();

    public override HttpRequestMessage CreateInvokeMethodRequest<TRequest>(
        HttpMethod httpMethod,
        string appId,
        string methodName,
        IReadOnlyCollection<KeyValuePair<string, string>> queryStringParameters,
        TRequest data
    ) => throw new NotSupportedException();

    public override Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public override Task<bool> CheckOutboundHealthAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public override Task WaitForSidecarAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public override Task ShutdownSidecarAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public override Task<DaprMetadata> GetMetadataAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public override Task SetMetadataAsync(
        string attributeName,
        string attributeValue,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

#pragma warning disable CS0672 // overriding obsolete members with non-obsolete no-op implementations
    public override Task<HttpResponseMessage> InvokeMethodWithResponseAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override HttpClient CreateInvokableHttpClient(string? appId = null) => throw new NotSupportedException();

    public override Task InvokeMethodAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public override Task<TResponse> InvokeMethodAsync<TResponse>(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task InvokeMethodGrpcAsync(
        string appId,
        string methodName,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task InvokeMethodGrpcAsync<TRequest>(
        string appId,
        string methodName,
        TRequest data,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<TResponse> InvokeMethodGrpcAsync<TResponse>(
        string appId,
        string methodName,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<TResponse> InvokeMethodGrpcAsync<TRequest, TResponse>(
        string appId,
        string methodName,
        TRequest data,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();
#pragma warning restore CS0672

    public override Task<TValue> GetStateAsync<TValue>(
        string storeName,
        string key,
        ConsistencyMode? consistencyMode = default,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<IReadOnlyList<BulkStateItem>> GetBulkStateAsync(
        string storeName,
        IReadOnlyList<string> keys,
        int? parallelism,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<IReadOnlyList<BulkStateItem<TValue>>> GetBulkStateAsync<TValue>(
        string storeName,
        IReadOnlyList<string> keys,
        int? parallelism,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task SaveBulkStateAsync<TValue>(
        string storeName,
        IReadOnlyList<SaveStateItem<TValue>> items,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task DeleteBulkStateAsync(
        string storeName,
        IReadOnlyList<BulkDeleteStateItem> items,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<(TValue value, string etag)> GetStateAndETagAsync<TValue>(
        string storeName,
        string key,
        ConsistencyMode? consistencyMode = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task SaveStateAsync<TValue>(
        string storeName,
        string key,
        TValue value,
        StateOptions? stateOptions = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task SaveByteStateAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> binaryValue,
        StateOptions? stateOptions = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<bool> TrySaveByteStateAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> binaryValue,
        string etag,
        StateOptions? stateOptions = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<ReadOnlyMemory<byte>> GetByteStateAsync(
        string storeName,
        string key,
        ConsistencyMode? consistencyMode = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<(ReadOnlyMemory<byte>, string etag)> GetByteStateAndETagAsync(
        string storeName,
        string key,
        ConsistencyMode? consistencyMode = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<bool> TrySaveStateAsync<TValue>(
        string storeName,
        string key,
        TValue value,
        string etag,
        StateOptions? stateOptions = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task ExecuteStateTransactionAsync(
        string storeName,
        IReadOnlyList<StateTransactionRequest> operations,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task DeleteStateAsync(
        string storeName,
        string key,
        StateOptions? stateOptions = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<bool> TryDeleteStateAsync(
        string storeName,
        string key,
        string etag,
        StateOptions? stateOptions = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<StateQueryResponse<TValue>> QueryStateAsync<TValue>(
        string storeName,
        string jsonQuery,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<Dictionary<string, string>> GetSecretAsync(
        string storeName,
        string key,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<Dictionary<string, Dictionary<string, string>>> GetBulkSecretAsync(
        string storeName,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<GetConfigurationResponse> GetConfiguration(
        string storeName,
        IReadOnlyList<string> keys,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<SubscribeConfigurationResponse> SubscribeConfiguration(
        string storeName,
        IReadOnlyList<string> keys,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<UnsubscribeConfigurationResponse> UnsubscribeConfiguration(
        string storeName,
        string id,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<ReadOnlyMemory<byte>> EncryptAsync(
        string vaultResourceName,
        ReadOnlyMemory<byte> plaintextBytes,
        string keyName,
        EncryptionOptions encryptionOptions,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override IAsyncEnumerable<ReadOnlyMemory<byte>> EncryptAsync(
        string vaultResourceName,
        Stream plaintextStream,
        string keyName,
        EncryptionOptions encryptionOptions,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<ReadOnlyMemory<byte>> DecryptAsync(
        string vaultResourceName,
        ReadOnlyMemory<byte> ciphertextBytes,
        string keyName,
        DecryptionOptions options,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<ReadOnlyMemory<byte>> DecryptAsync(
        string vaultResourceName,
        ReadOnlyMemory<byte> ciphertextBytes,
        string keyName,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override IAsyncEnumerable<ReadOnlyMemory<byte>> DecryptAsync(
        string vaultResourceName,
        Stream ciphertextStream,
        string keyName,
        DecryptionOptions options,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override IAsyncEnumerable<ReadOnlyMemory<byte>> DecryptAsync(
        string vaultResourceName,
        Stream ciphertextStream,
        string keyName,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

#pragma warning disable DAPR_DISTRIBUTEDLOCK // Distributed Lock API is experimental in the Dapr SDK
    public override Task<TryLockResponse> Lock(
        string storeName,
        string resourceId,
        string lockOwner,
        int expiryInSeconds,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public override Task<UnlockResponse> Unlock(
        string storeName,
        string resourceId,
        string lockOwner,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();
#pragma warning restore DAPR_DISTRIBUTEDLOCK
}
