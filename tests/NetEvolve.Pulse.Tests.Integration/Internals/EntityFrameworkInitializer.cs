namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

public sealed class EntityFrameworkInitializer : IDatabaseInitializer
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
        _ = services.AddDbContext<TestDbContext>(options =>
        {
            var connectionString = databaseService.ConnectionString;

            // Disable EF Core's global internal service provider cache so that each test
            // gets a freshly-built model (with its own table name) instead of reusing a
            // cached model from a previous test that used the same connection string.
            _ = options.EnableServiceProviderCaching(false);

            _ = databaseService.DatabaseType switch
            {
                DatabaseType.InMemory => options.UseInMemoryDatabase(connectionString),
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
