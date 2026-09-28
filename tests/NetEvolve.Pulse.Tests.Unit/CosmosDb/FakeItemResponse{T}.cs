namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Net;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Test double for <see cref="ItemResponse{T}"/> exposing a resource and an ETag.
/// </summary>
internal sealed class FakeItemResponse<T> : ItemResponse<T>
{
    private readonly T _resource;
    private readonly string? _etag;

    public FakeItemResponse(T resource, string? etag = null)
    {
        _resource = resource;
        _etag = etag;
    }

    public override T Resource => _resource;

    public override string ETag => _etag!;

    public override Headers Headers => new Headers();

    public override HttpStatusCode StatusCode => HttpStatusCode.OK;

    public override CosmosDiagnostics Diagnostics => throw new NotImplementedException();

    public override double RequestCharge => 0;

    public override string ActivityId => string.Empty;
}
