namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Test double for <see cref="FeedResponse{T}"/> wrapping an in-memory page.
/// </summary>
internal sealed class FakeFeedResponse<T> : FeedResponse<T>
{
    private readonly IReadOnlyList<T> _items;

    public FakeFeedResponse(IReadOnlyList<T> items) => _items = items;

    public override string ContinuationToken => throw new NotImplementedException();

    public override int Count => _items.Count;

    public override string IndexMetrics => throw new NotImplementedException();

    public override Headers Headers => new Headers();

    public override IEnumerable<T> Resource => _items;

    public override HttpStatusCode StatusCode => HttpStatusCode.OK;

    public override CosmosDiagnostics Diagnostics => throw new NotImplementedException();

    public override IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
}
