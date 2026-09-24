namespace NetEvolve.Pulse.Tests.Unit.MongoDB;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BsonDocument = global::MongoDB.Bson.BsonDocument;
using IMongoDatabase = global::MongoDB.Driver.IMongoDatabase;

/// <summary>
/// Minimal <see cref="IMongoDatabase"/> test double whose <c>RunCommandAsync</c> either completes
/// successfully or fails with a preconfigured exception, so the caller's exception-handling branches
/// can be exercised deterministically without a real MongoDB server.
/// </summary>
internal class FakeMongoDatabase : DispatchProxy
{
    private Exception? _exceptionToThrow;

    public static IMongoDatabase ThatSucceeds() => Create<IMongoDatabase, FakeMongoDatabase>();

    public static IMongoDatabase ThatThrows(Exception exceptionToThrow)
    {
        var proxy = Create<IMongoDatabase, FakeMongoDatabase>();
        ((FakeMongoDatabase)proxy)._exceptionToThrow = exceptionToThrow;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IMongoDatabase.RunCommandAsync))
        {
            throw new NotSupportedException($"{targetMethod?.Name} is not supported by {nameof(FakeMongoDatabase)}.");
        }

        var resultType = targetMethod.ReturnType.GetGenericArguments()[0];

        if (_exceptionToThrow is { } exception)
        {
            var fromException = typeof(Task)
                .GetMethods()
                .Single(m =>
                    m.Name == nameof(Task.FromException) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1
                )
                .MakeGenericMethod(resultType);
            return fromException.Invoke(null, [exception]);
        }

        var fromResult = typeof(Task)
            .GetMethods()
            .Single(m => m.Name == nameof(Task.FromResult) && m.GetParameters().Length == 1)
            .MakeGenericMethod(resultType);
        var defaultValue = resultType == typeof(BsonDocument) ? new BsonDocument("ok", 1) : null;
        return fromResult.Invoke(null, [defaultValue]);
    }
}
