namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

[TestGroup("PostgreSql")]
public sealed class PostgreSqlIdempotencyKeyRepositoryTests
{
    private const string ValidConnectionString = "Host=localhost;Database=Test;Username=postgres;Password=secret;";

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new PostgreSqlIdempotencyKeyRepository(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = null })
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = string.Empty })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = "   " })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions { ConnectionString = ValidConnectionString };

        var repository = new PostgreSqlIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomSchema_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions { ConnectionString = ValidConnectionString, Schema = "custom" };

        var repository = new PostgreSqlIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullSchema_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions { ConnectionString = ValidConnectionString, Schema = null };

        var repository = new PostgreSqlIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }
}
