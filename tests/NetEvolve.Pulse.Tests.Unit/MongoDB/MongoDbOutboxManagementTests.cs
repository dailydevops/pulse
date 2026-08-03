namespace NetEvolve.Pulse.Tests.Unit.MongoDB;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("MongoDB")]
public sealed class MongoDbOutboxManagementTests
{
    [Test]
    public async Task Constructor_WithNullMongoClient_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new MongoDbOutboxManagement(
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
                new MongoDbOutboxManagement(
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
                new MongoDbOutboxManagement(
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
                new MongoDbOutboxManagement(
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
                new MongoDbOutboxManagement(
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
                new MongoDbOutboxManagement(
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
                new MongoDbOutboxManagement(
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
        var management = new MongoDbOutboxManagement(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithZeroPageSize_ThrowsArgumentOutOfRangeException()
    {
        using var client = new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017");
        var management = new MongoDbOutboxManagement(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => _ = await management.GetDeadLetterMessagesAsync(pageSize: 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException()
    {
        using var client = new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017");
        var management = new MongoDbOutboxManagement(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => _ = await management.GetDeadLetterMessagesAsync(pageSize: -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePage_ThrowsArgumentOutOfRangeException()
    {
        using var client = new global::MongoDB.Driver.MongoClient("mongodb://localhost:27017");
        var management = new MongoDbOutboxManagement(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = "testdb" }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => _ = await management.GetDeadLetterMessagesAsync(pageSize: 50, page: -1))
            .Throws<ArgumentOutOfRangeException>();
    }
}
