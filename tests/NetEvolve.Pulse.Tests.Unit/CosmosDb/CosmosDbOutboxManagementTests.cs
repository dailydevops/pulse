namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxManagementTests
{
    private const string EmulatorAccountKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMcQMSXY0UQKhRAEA==";
    private const string EmulatorConnectionString =
        $"AccountEndpoint=https://localhost:8081/;AccountKey={EmulatorAccountKey};";

    [Test]
    public async Task Constructor_WithNullCosmosClient_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new CosmosDbOutboxManagement(
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
            .That(() => new CosmosDbOutboxManagement(client, null!, TimeProvider.System))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        _ = await Assert
            .That(() =>
                new CosmosDbOutboxManagement(
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
                new CosmosDbOutboxManagement(
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
                new CosmosDbOutboxManagement(
                    client,
                    Options.Create(new CosmosDbOutboxOptions { DatabaseName = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_WithValidOptions_CreatesInstance()
    {
        using var client = new CosmosClient(EmulatorConnectionString);

        var management = new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        _ = await Assert.That(management).IsNotNull();
    }
}
