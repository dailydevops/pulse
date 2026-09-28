namespace NetEvolve.Pulse.Tests.Unit.MongoDB;

using System;
using System.Reflection;
using IMongoClient = global::MongoDB.Driver.IMongoClient;
using IMongoDatabase = global::MongoDB.Driver.IMongoDatabase;

/// <summary>
/// Minimal <see cref="IMongoClient"/> test double that hands out a single, preconfigured
/// <see cref="IMongoDatabase"/> for every <c>GetDatabase</c> call. All other members are
/// intentionally unsupported, since the code under test never invokes them.
/// </summary>
internal class FakeMongoClient : DispatchProxy
{
    private IMongoDatabase _database = null!;

    public static IMongoClient Create(IMongoDatabase database)
    {
        var proxy = Create<IMongoClient, FakeMongoClient>();
        ((FakeMongoClient)proxy)._database = database;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            nameof(IMongoClient.GetDatabase) => _database,
            nameof(IDisposable.Dispose) => null,
            _ => throw new NotSupportedException(
                $"{targetMethod?.Name} is not supported by {nameof(FakeMongoClient)}."
            ),
        };
}
