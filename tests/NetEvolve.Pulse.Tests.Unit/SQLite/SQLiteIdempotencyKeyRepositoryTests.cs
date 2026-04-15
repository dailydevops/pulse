namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

[TestGroup("SQLite")]
public sealed class SQLiteIdempotencyKeyRepositoryTests
{
    private const string ValidConnectionString = "Data Source=:memory:";

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new SQLiteIdempotencyKeyRepository(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SQLiteIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = null })
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = string.Empty })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = "   " })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions { ConnectionString = ValidConnectionString };

        var repository = new SQLiteIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomTableName_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions
        {
            ConnectionString = ValidConnectionString,
            TableName = "CustomIdempotencyKeys",
        };

        var repository = new SQLiteIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithWalModeDisabled_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions { ConnectionString = ValidConnectionString, EnableWalMode = false };

        var repository = new SQLiteIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }
}
