namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

[TestGroup("SqlServer")]
public sealed class SqlServerIdempotencyKeyRepositoryTests
{
    private const string ValidConnectionString = "Server=.;Database=Test;Integrated Security=true;";

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new SqlServerIdempotencyKeyRepository(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SqlServerIdempotencyKeyRepository(
                    Options.Create(new SqlServerIdempotencyKeyOptions { ConnectionString = null })
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerIdempotencyKeyRepository(
                    Options.Create(new SqlServerIdempotencyKeyOptions { ConnectionString = string.Empty })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerIdempotencyKeyRepository(
                    Options.Create(new SqlServerIdempotencyKeyOptions { ConnectionString = "   " })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new SqlServerIdempotencyKeyOptions { ConnectionString = ValidConnectionString };

        var repository = new SqlServerIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomSchema_CreatesInstance()
    {
        var options = new SqlServerIdempotencyKeyOptions
        {
            ConnectionString = ValidConnectionString,
            Schema = "custom",
        };

        var repository = new SqlServerIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullSchema_CreatesInstance()
    {
        var options = new SqlServerIdempotencyKeyOptions { ConnectionString = ValidConnectionString, Schema = null };

        var repository = new SqlServerIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }
}
