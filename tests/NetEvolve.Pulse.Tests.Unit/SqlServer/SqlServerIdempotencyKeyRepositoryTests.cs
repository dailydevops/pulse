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
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = null })
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = string.Empty })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerIdempotencyKeyRepository(
                    Options.Create(new IdempotencyKeyOptions { ConnectionString = "   " })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions { ConnectionString = ValidConnectionString };

        var repository = new SqlServerIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomSchema_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions { ConnectionString = ValidConnectionString, Schema = "custom" };

        var repository = new SqlServerIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullSchema_CreatesInstance()
    {
        var options = new IdempotencyKeyOptions { ConnectionString = ValidConnectionString, Schema = null };

        var repository = new SqlServerIdempotencyKeyRepository(Options.Create(options));

        _ = await Assert.That(repository).IsNotNull();
    }

    // Defense-in-depth: pin that an attacker-controlled Schema value cannot reach the SQL
    // builder. The constructor must fail fast when Schema contains characters that would
    // break out of the [bracketed] identifier (e.g. ']' followed by injected SQL).
    [Test]
    public async Task Constructor_WithMaliciousSchema_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerIdempotencyKeyRepository(
                    Options.Create(
                        new IdempotencyKeyOptions
                        {
                            ConnectionString = ValidConnectionString,
                            Schema = "pulse].[evil] -- ",
                        }
                    )
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task ExistsAsync_WithNullIdempotencyKey_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new SqlServerIdempotencyKeyRepository(
            Options.Create(new IdempotencyKeyOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () =>
                await repository.ExistsAsync(null!, cancellationToken: cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ExistsAsync_WithEmptyIdempotencyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new SqlServerIdempotencyKeyRepository(
            Options.Create(new IdempotencyKeyOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () =>
                await repository.ExistsAsync(string.Empty, cancellationToken: cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ExistsAsync_WithWhitespaceIdempotencyKey_ThrowsArgumentException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new SqlServerIdempotencyKeyRepository(
            Options.Create(new IdempotencyKeyOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () =>
                await repository.ExistsAsync("   ", cancellationToken: cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task StoreAsync_WithNullIdempotencyKey_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new SqlServerIdempotencyKeyRepository(
            Options.Create(new IdempotencyKeyOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () =>
                await repository.StoreAsync(null!, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithEmptyIdempotencyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new SqlServerIdempotencyKeyRepository(
            Options.Create(new IdempotencyKeyOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () =>
                await repository
                    .StoreAsync(string.Empty, DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task StoreAsync_WithWhitespaceIdempotencyKey_ThrowsArgumentException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new SqlServerIdempotencyKeyRepository(
            Options.Create(new IdempotencyKeyOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () =>
                await repository.StoreAsync("   ", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentException>();
    }
}
