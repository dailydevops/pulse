namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Idempotency;

public sealed class EntityFrameworkIdempotencyInitializer : IDatabaseInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture databaseService) =>
        mediatorBuilder.AddEntityFrameworkIdempotencyStore<TestIdempotencyDbContext>();

    private static readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using (var scope = serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestIdempotencyDbContext>();
            var databaseCreator = context.GetService<IDatabaseCreator>();

            cancellationToken.ThrowIfCancellationRequested();

            if (databaseCreator is IRelationalDatabaseCreator relationalDatabaseCreator)
            {
                if (!await relationalDatabaseCreator.CanConnectAsync(cancellationToken).ConfigureAwait(false))
                {
                    await relationalDatabaseCreator.CreateAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                if (await databaseCreator.CanConnectAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await _gate.WaitAsync(cancellationToken);

                try
                {
                    _ = await databaseCreator.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                finally
                {
                    _ = _gate.Release();
                }
            }

            if (databaseCreator is IRelationalDatabaseCreator relationalTableCreator)
            {
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    await relationalTableCreator.CreateTablesAsync(cancellationToken);
                }
                finally
                {
                    _ = _gate.Release();
                }
            }
        }
    }

    public void Initialize(IServiceCollection services, IServiceFixture databaseService) =>
        _ = services.AddDbContextFactory<TestIdempotencyDbContext>(options =>
        {
            var connectionString = databaseService.ConnectionString;

            // Register a custom model-cache key factory that includes the per-test table name.
            _ = options.ReplaceService<IModelCacheKeyFactory, TestTableModelCacheKeyFactory>();

            _ = databaseService.ServiceType switch
            {
                ServiceType.InMemory => options.UseInMemoryDatabase(connectionString),
                ServiceType.MySql => options.UseMySQL(connectionString),
                ServiceType.PostgreSQL => options.UseNpgsql(connectionString),
                // Add a busy-timeout interceptor so that concurrent SaveChangesAsync calls from
                // parallel tests wait and retry instead of failing with SQLITE_BUSY.
                ServiceType.SQLite => options
                    .UseSqlite(connectionString)
                    .AddInterceptors(new SQLiteBusyTimeoutInterceptor()),
                ServiceType.SqlServer => options.UseSqlServer(
                    connectionString,
                    sqlOptions => sqlOptions.EnableRetryOnFailure(maxRetryCount: 5)
                ),
                _ => throw new NotSupportedException($"Database type {databaseService.ServiceType} is not supported."),
            };
        });

    /// <summary>
    /// Sets <c>PRAGMA busy_timeout</c> on every SQLite connection when it is opened so that
    /// concurrent write operations wait and retry rather than immediately failing with SQLITE_BUSY.
    /// </summary>
    private sealed class SQLiteBusyTimeoutInterceptor : DbConnectionInterceptor
    {
        private const string Pragmas = "PRAGMA busy_timeout = 60000; PRAGMA journal_mode = WAL;";

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = Pragmas;
            _ = command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Pragmas;
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Custom EF Core model-cache key factory that incorporates <see cref="IdempotencyKeyOptions.TableName"/>
    /// into the cache key to prevent model sharing between tests that use different table names.
    /// </summary>
    private sealed class TestTableModelCacheKeyFactory : IModelCacheKeyFactory
    {
        /// <inheritdoc />
        public object Create(DbContext context, bool designTime)
        {
            string tableName;
            try
            {
                tableName = context.GetService<IOptions<IdempotencyKeyOptions>>()?.Value?.TableName ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                tableName = string.Empty;
            }

            return (context.GetType(), tableName, designTime);
        }
    }

    internal sealed class TestIdempotencyDbContext : DbContext, IIdempotencyStoreDbContext
    {
        public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

        public TestIdempotencyDbContext(DbContextOptions<TestIdempotencyDbContext> configuration)
            : base(configuration) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            _ = modelBuilder.ApplyPulseConfiguration(this);
        }
    }
}
