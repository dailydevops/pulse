namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxRepositoryTests
{
    // The well-known Cosmos DB emulator account key — safe to embed in tests.
    private const string EmulatorAccountKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMcQMSXY0UQKhRAEA==";
    private const string EmulatorConnectionString =
        $"AccountEndpoint=https://localhost:8081/;AccountKey={EmulatorAccountKey};";

    [Test]
    public async Task Constructor_WithNullCosmosClient_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new CosmosDbOutboxRepository(
                    null!,
                    Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        _ = await Assert
            .That(() => new CosmosDbOutboxRepository(client, null!, TimeProvider.System))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        _ = await Assert
            .That(() =>
                new CosmosDbOutboxRepository(
                    client,
                    Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
                    null!
                )
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithEmptyDatabaseName_ThrowsArgumentException()
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        _ = await Assert
            .That(() =>
                new CosmosDbOutboxRepository(
                    client,
                    Options.Create(new CosmosDbOutboxOptions { DatabaseName = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_WithWhitespaceDatabaseName_ThrowsArgumentException()
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        _ = await Assert
            .That(() =>
                new CosmosDbOutboxRepository(
                    client,
                    Options.Create(new CosmosDbOutboxOptions { DatabaseName = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_WithEmptyContainerName_ThrowsArgumentException()
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        _ = await Assert
            .That(() =>
                new CosmosDbOutboxRepository(
                    client,
                    Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb", ContainerName = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_WithValidOptions_CreatesInstance()
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        var repository = new CosmosDbOutboxRepository(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithTtlEnabled_CreatesInstance()
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        var repository = new CosmosDbOutboxRepository(
            client,
            Options.Create(
                new CosmosDbOutboxOptions
                {
                    DatabaseName = "TestDb",
                    ContainerName = "outbox",
                    EnableTimeToLive = true,
                    TtlSeconds = 3600,
                }
            ),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        var repository = new CosmosDbOutboxRepository(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await repository.AddAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }
}
