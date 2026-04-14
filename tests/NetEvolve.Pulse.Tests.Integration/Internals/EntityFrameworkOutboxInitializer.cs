namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

public sealed class EntityFrameworkOutboxInitializer : IDatabaseInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IDatabaseServiceFixture databaseService) =>
        mediatorBuilder.AddEntityFrameworkOutbox<TestDbContext>();

    private static readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using (var scope = serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
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

    public void Initialize(IServiceCollection services, IDatabaseServiceFixture databaseService) =>
        _ = services.AddDbContextFactory<TestDbContext>(options =>
        {
            var connectionString = databaseService.ConnectionString;

            // Register a custom model-cache key factory that includes the per-test table name.
            // Multiple tests share the same connection string (same container), so EF Core would
            // otherwise cache the first test's model (with its table name) and reuse it for all
            // subsequent tests — causing "Table '...' already exists" errors on CreateTablesAsync.
            // This factory makes the cache key unique per (DbContext type, TableName), so each
            // test gets its own model while still sharing the internal EF Core service provider
            // (critical for correct type-mapping initialisation on providers like Oracle MySQL).
            _ = options.ReplaceService<IModelCacheKeyFactory, TestTableModelCacheKeyFactory>();

            _ = databaseService.DatabaseType switch
            {
                DatabaseType.InMemory => options.UseInMemoryDatabase(connectionString),
                DatabaseType.MySql => options.UseMySQL(connectionString),
                DatabaseType.PostgreSQL => options.UseNpgsql(connectionString),
                // Add a busy-timeout interceptor so that concurrent SaveChangesAsync calls from
                // parallel PublishAsync tasks wait and retry instead of failing with SQLITE_BUSY.
                DatabaseType.SQLite => options
                    .UseSqlite(connectionString)
                    .AddInterceptors(new SQLiteBusyTimeoutInterceptor()),
                DatabaseType.SqlServer => options.UseSqlServer(
                    connectionString,
                    sqlOptions => sqlOptions.EnableRetryOnFailure(maxRetryCount: 5)
                ),
                _ => throw new NotSupportedException($"Database type {databaseService.DatabaseType} is not supported."),
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
    /// Custom EF Core model-cache key factory that incorporates <see cref="OutboxOptions.TableName"/>
    /// into the cache key.  Without this, all tests that share the same database connection string
    /// receive the same cached EF Core model (keyed only by <see cref="DbContext"/> type), so the
    /// second test picks up the first test's table name and <c>CreateTablesAsync</c> fails with
    /// "Table '&lt;first-test-name&gt;' already exists".
    /// </summary>
    private sealed class TestTableModelCacheKeyFactory : IModelCacheKeyFactory
    {
        /// <inheritdoc />
        public object Create(DbContext context, bool designTime)
        {
            string tableName;
            try
            {
                tableName = context.GetService<IOptions<OutboxOptions>>()?.Value?.TableName ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                tableName = string.Empty;
            }

            return (context.GetType(), tableName, designTime);
        }
    }

    private sealed class TestDbContext : DbContext, IOutboxDbContext
    {
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        public TestDbContext(DbContextOptions<TestDbContext> configuration)
            : base(configuration) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            _ = modelBuilder.ApplyPulseConfiguration(this);
        }
    }
}
