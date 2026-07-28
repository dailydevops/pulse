namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkIdempotencyKeyRepositoryStoreTests
{
    [Test]
    public async Task StoreAsync_WithUnrelatedUniqueConstraintViolation_ThrowsDbUpdateException(
        CancellationToken cancellationToken
    )
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestIdempotencyWithUsersDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new TestIdempotencyWithUsersDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                _ = await context
                    .Users.AddAsync(new TestUser { Email = "duplicate@example.com" }, cancellationToken)
                    .ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // A handler queues an unrelated entity that violates its own unique index;
                // the pending change is flushed by the idempotency store's SaveChangesAsync.
                _ = await context
                    .Users.AddAsync(new TestUser { Email = "duplicate@example.com" }, cancellationToken)
                    .ConfigureAwait(false);

                var repository = new EntityFrameworkIdempotencyKeyRepository<TestIdempotencyWithUsersDbContext>(
                    context
                );

                _ = await Assert
                    .That(async () =>
                        await repository
                            .StoreAsync("store-key", DateTimeOffset.UtcNow, cancellationToken)
                            .ConfigureAwait(false)
                    )
                    .Throws<DbUpdateException>();
            }
        }
    }

    [Test]
    public async Task StoreAsync_WithDuplicateIdempotencyKey_DoesNotThrow(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestIdempotencyWithUsersDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new TestIdempotencyWithUsersDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var repository = new EntityFrameworkIdempotencyKeyRepository<TestIdempotencyWithUsersDbContext>(
                    context
                );

                await repository.StoreAsync("same-key", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

                // Clearing the tracker forces the second store to hit the database
                // unique constraint instead of the local change tracker check.
                context.ChangeTracker.Clear();

                await repository.StoreAsync("same-key", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

                var count = await context.IdempotencyKeys.CountAsync(cancellationToken).ConfigureAwait(false);
                _ = await Assert.That(count).IsEqualTo(1);
            }
        }
    }

    internal sealed class TestIdempotencyWithUsersDbContext : DbContext, IIdempotencyStoreDbContext
    {
        public TestIdempotencyWithUsersDbContext(DbContextOptions<TestIdempotencyWithUsersDbContext> options)
            : base(options) { }

        public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

        public DbSet<TestUser> Users => Set<TestUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            _ = modelBuilder.ApplyPulseConfiguration(this);
            _ = modelBuilder.Entity<TestUser>().HasIndex(u => u.Email).IsUnique();
        }
    }

    internal sealed class TestUser
    {
        public int Id { get; set; }

        public string Email { get; set; } = string.Empty;
    }
}
