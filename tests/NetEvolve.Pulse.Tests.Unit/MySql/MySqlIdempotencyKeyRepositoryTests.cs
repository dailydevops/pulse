namespace NetEvolve.Pulse.Tests.Unit.MySql;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

[TestGroup("MySql")]
public sealed class MySqlIdempotencyKeyRepositoryTests
{
    private const string ValidConnectionString = "Server=localhost;Database=Test;Uid=root;Pwd=secret;";

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new MySqlIdempotencyKeyRepository(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new MySqlIdempotencyKeyRepository(Options.Create(new IdempotencyKeyOptions { ConnectionString = null }))
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MySqlIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = string.Empty })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MySqlIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = "   " })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions { ConnectionString = ValidConnectionString };

        var repository = new MySqlIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomTableName_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions
        {
            ConnectionString = ValidConnectionString,
            TableName = "CustomIdempotencyKey",
        };

        var repository = new MySqlIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task ExistsAsync_WithNullOrWhitespaceKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var repository = new MySqlIdempotencyKeyRepository(
            Options.Create(new IdempotencyKeyOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () =>
                await repository.ExistsAsync(null!, cancellationToken: cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task StoreAsync_WithNullOrWhitespaceKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var repository = new MySqlIdempotencyKeyRepository(
            Options.Create(new IdempotencyKeyOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () =>
                await repository.StoreAsync(null!, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentException>();
    }
}
