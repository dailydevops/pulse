namespace NetEvolve.Pulse.Tests.Unit.MongoDB;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("MongoDB")]
public sealed class MongoDbOutboxRepositoryTests
{
    [Test]
    public async Task Constructor_WithNullMongoClient_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new MongoDbOutboxRepository(
                    null!,
                    Options.Create(new MongoDbOutboxOptions { DatabaseName = "db" }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "MongoClient lifetime is intentionally controlled by the caller in production; in this test it is discarded after exception is thrown from the constructor."
    )]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new MongoDbOutboxRepository(
                    new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017"),
                    null!,
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "MongoClient lifetime is intentionally controlled by the caller in production; in this test it is discarded after exception is thrown from the constructor."
    )]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new MongoDbOutboxRepository(
                    new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017"),
                    Options.Create(new MongoDbOutboxOptions { DatabaseName = "db" }),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "MongoClient lifetime is intentionally controlled by the caller in production; in this test it is discarded after exception is thrown from the constructor."
    )]
    public async Task Constructor_WithEmptyDatabaseName_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MongoDbOutboxRepository(
                    new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017"),
                    Options.Create(new MongoDbOutboxOptions { DatabaseName = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "MongoClient lifetime is intentionally controlled by the caller in production; in this test it is discarded after exception is thrown from the constructor."
    )]
    public async Task Constructor_WithWhitespaceDatabaseName_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MongoDbOutboxRepository(
                    new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017"),
                    Options.Create(new MongoDbOutboxOptions { DatabaseName = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "MongoClient lifetime is intentionally controlled by the caller in production; in this test it is discarded after exception is thrown from the constructor."
    )]
    public async Task Constructor_WithEmptyCollectionName_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MongoDbOutboxRepository(
                    new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017"),
                    Options.Create(new MongoDbOutboxOptions { DatabaseName = "db", CollectionName = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "MongoClient lifetime is intentionally controlled by the caller in production; in this test it is discarded after exception is thrown from the constructor."
    )]
    public async Task Constructor_WithWhitespaceCollectionName_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MongoDbOutboxRepository(
                    new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017"),
                    Options.Create(new MongoDbOutboxOptions { DatabaseName = "db", CollectionName = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithValidOptions_CreatesInstance()
    {
        using var client = new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017");
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomCollectionName_CreatesInstance()
    {
        using var client = new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017");
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb", CollectionName = "custom_outbox" }),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        using var client = new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017");
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        _ = await Assert.That(() => repository.AddAsync(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task IsHealthyAsync_WithCanceledToken_ThrowsOperationCanceledException()
    {
        using var client = new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017");
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = await Assert.That(() => repository.IsHealthyAsync(cts.Token)).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task IsHealthyAsync_WithUnreachableServer_ReturnsFalse()
    {
        using var client = new global::MongoDB.Driver.MongoClient(
            "mongodb://localhost:1/?serverSelectionTimeoutMS=200&connectTimeoutMS=200&directConnection=true"
        );
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        var result = await repository.IsHealthyAsync(CancellationToken.None);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_WhenRunCommandThrowsMongoException_ReturnsFalse()
    {
        var database = FakeMongoDatabase.ThatThrows(new global::MongoDB.Driver.MongoException("connection lost"));
        using var client = FakeMongoClient.Create(database);
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        var result = await repository.IsHealthyAsync(CancellationToken.None);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_WhenRunCommandThrowsTimeoutException_ReturnsFalse()
    {
        var database = FakeMongoDatabase.ThatThrows(new TimeoutException("server selection timed out"));
        using var client = FakeMongoClient.Create(database);
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        var result = await repository.IsHealthyAsync(CancellationToken.None);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_WhenPingSucceeds_ReturnsTrue()
    {
        var database = FakeMongoDatabase.ThatSucceeds();
        using var client = FakeMongoClient.Create(database);
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        var result = await repository.IsHealthyAsync(CancellationToken.None);

        _ = await Assert.That(result).IsTrue();
    }
}
