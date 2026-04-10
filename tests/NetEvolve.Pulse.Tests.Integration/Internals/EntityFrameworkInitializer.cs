namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

public sealed class EntityFrameworkInitializer : IDatabaseInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IDatabaseServiceFixture databaseService) =>
        mediatorBuilder.AddEntityFrameworkOutbox<TestDbContext>();

    public async ValueTask<bool> CreateDatabaseAsync(IServiceProvider serviceProvider)
    {
        using (var scope = serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

            if (await context.Database.CanConnectAsync().ConfigureAwait(false))
            {
                _ = await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
            }
            return await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
        }
    }

    public void Initialize(IServiceCollection services, IDatabaseServiceFixture databaseService) =>
        _ = services.AddDbContext<TestDbContext>(options =>
        {
            _ = databaseService.DatabaseType switch
            {
                DatabaseType.InMemory => options.UseInMemoryDatabase(databaseService.ConnectionString),
                // Add a busy-timeout interceptor so that concurrent SaveChangesAsync calls from
                // parallel PublishAsync tasks wait and retry instead of failing with SQLITE_BUSY.
                DatabaseType.SQLite => options
                    .UseSqlite(databaseService.ConnectionString)
                    .AddInterceptors(new SQLiteBusyTimeoutInterceptor()),
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
