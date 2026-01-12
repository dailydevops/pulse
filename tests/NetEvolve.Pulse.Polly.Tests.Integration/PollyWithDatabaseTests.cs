namespace NetEvolve.Pulse.Polly.Tests.Integration;

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using global::Polly;
using global::Polly.Retry;
using global::Polly.Timeout;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using Testcontainers.MsSql;
using TUnit.Core;
using TUnit.Core.Interfaces;

/// <summary>
/// Integration tests that verify Polly resilience policies work correctly
/// when combined with real database operations using Testcontainers.
/// </summary>
[ClassDataSource<SqlServerContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PollyWithDatabaseTests(SqlServerContainerFixture fixture)
{
    private readonly SqlServerContainerFixture _fixture = fixture;

    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        return services;
    }

    [Test]
    public async Task RetryPolicy_WithDatabaseCommand_RetriesOnTransientFailure()
    {
        // Arrange
        var dbName = $"PollyDbRetry_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(dbName).ConfigureAwait(false);

        try
        {
            var connectionString = _fixture.GetConnectionString(dbName);
            var services = CreateServiceCollection();
            var handler = new DatabaseCommandHandler(connectionString);

            _ = services
                .AddScoped<ICommandHandler<CreateRecordCommand, int>>(_ => handler)
                .AddPulse(configurator =>
                    configurator.AddPollyRequestPolicies<CreateRecordCommand, int>(pipeline =>
                        pipeline.AddRetry(
                            new RetryStrategyOptions<int>
                            {
                                MaxRetryAttempts = 3,
                                Delay = TimeSpan.FromMilliseconds(50),
                                ShouldHandle = new PredicateBuilder<int>()
                                    .Handle<SqlException>()
                                    .Handle<InvalidOperationException>(),
                            }
                        )
                    )
                );

            await using var provider = services.BuildServiceProvider();
            var mediator = provider.GetRequiredService<IMediator>();

            // Act
            var command = new CreateRecordCommand(dbName, "TestData");
            var result = await mediator.SendAsync<CreateRecordCommand, int>(command).ConfigureAwait(false);

            // Assert
            _ = await Assert.That(result).IsEqualTo(1);
            _ = await Assert.That(handler.ExecutionCount).IsEqualTo(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName).ConfigureAwait(false);
        }
    }

    [Test]
    [Skip(
        "Polly timeout requires propagating ResilienceContext.CancellationToken to handlers. Architecture change needed."
    )]
    public async Task TimeoutPolicy_WithSlowDatabaseQuery_ThrowsTimeoutException()
    {
        // Arrange
        var dbName = $"PollyDbTimeout_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(dbName).ConfigureAwait(false);

        try
        {
            var connectionString = _fixture.GetConnectionString(dbName);
            var services = CreateServiceCollection();
            var handler = new SlowDatabaseQueryHandler(connectionString, TimeSpan.FromSeconds(5));

            _ = services
                .AddScoped<ICommandHandler<SlowQueryCommand, int>>(_ => handler)
                .AddPulse(configurator =>
                    configurator.AddPollyRequestPolicies<SlowQueryCommand, int>(pipeline =>
                        pipeline.AddTimeout(TimeSpan.FromMilliseconds(200))
                    )
                );

            await using var provider = services.BuildServiceProvider();
            var mediator = provider.GetRequiredService<IMediator>();

            // Act & Assert
            var command = new SlowQueryCommand(dbName);
            _ = await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
                mediator.SendAsync<SlowQueryCommand, int>(command)
            );
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PollyPolicy_WithOutboxRepository_RetriesOnConnectionFailure()
    {
        // Arrange
        var dbName = $"PollyOutbox_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(dbName).ConfigureAwait(false);
        await _fixture.InitializeOutboxSchemaAsync(dbName).ConfigureAwait(false);

        try
        {
            var connectionString = _fixture.GetConnectionString(dbName);
            var services = CreateServiceCollection();
            var handler = new OutboxStoreHandler(connectionString);

            _ = services
                .AddScoped<ICommandHandler<StoreOutboxCommand, bool>>(_ => handler)
                .AddPulse(configurator =>
                    configurator.AddPollyRequestPolicies<StoreOutboxCommand, bool>(pipeline =>
                        pipeline.AddRetry(
                            new RetryStrategyOptions<bool>
                            {
                                MaxRetryAttempts = 3,
                                Delay = TimeSpan.FromMilliseconds(100),
                            }
                        )
                    )
                );

            await using var provider = services.BuildServiceProvider();
            var mediator = provider.GetRequiredService<IMediator>();

            // Act
            var command = new StoreOutboxCommand(dbName, "TestEvent", """{"key":"value"}""");
            var result = await mediator.SendAsync<StoreOutboxCommand, bool>(command).ConfigureAwait(false);

            // Assert
            _ = await Assert.That(result).IsTrue();
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task CombinedPolicies_WithDatabaseOperation_AppliesAllPolicies()
    {
        // Arrange
        var dbName = $"PollyDbCombined_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(dbName).ConfigureAwait(false);

        try
        {
            var connectionString = _fixture.GetConnectionString(dbName);
            var services = CreateServiceCollection();
            var handler = new FailingDatabaseHandler(connectionString, failCount: 2);

            _ = services
                .AddScoped<ICommandHandler<DatabaseOperationCommand, string>>(_ => handler)
                .AddPulse(configurator =>
                    configurator.AddPollyRequestPolicies<DatabaseOperationCommand, string>(pipeline =>
                        pipeline
                            .AddTimeout(TimeSpan.FromSeconds(30))
                            .AddRetry(
                                new RetryStrategyOptions<string>
                                {
                                    MaxRetryAttempts = 5,
                                    Delay = TimeSpan.FromMilliseconds(50),
                                }
                            )
                    )
                );

            await using var provider = services.BuildServiceProvider();
            var mediator = provider.GetRequiredService<IMediator>();

            // Act
            var command = new DatabaseOperationCommand(dbName, "Test");
            var result = await mediator.SendAsync<DatabaseOperationCommand, string>(command).ConfigureAwait(false);

            // Assert
            _ = await Assert.That(result).IsEqualTo("Success");
            _ = await Assert.That(handler.AttemptCount).IsEqualTo(3); // 2 failures + 1 success
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName).ConfigureAwait(false);
        }
    }

    #region Test Commands and Handlers

    private sealed class CreateRecordCommand : ICommand<int>
    {
        public CreateRecordCommand(string dbName, string data)
        {
            DbName = dbName;
            Data = data;
        }

        public string DbName { get; }
        public string Data { get; }
        public string? CorrelationId { get; set; }
    }

    private sealed class SlowQueryCommand : ICommand<int>
    {
        public SlowQueryCommand(string dbName) => DbName = dbName;

        public string DbName { get; }
        public string? CorrelationId { get; set; }
    }

    private sealed class StoreOutboxCommand : ICommand<bool>
    {
        public StoreOutboxCommand(string dbName, string eventType, string payload)
        {
            DbName = dbName;
            EventType = eventType;
            Payload = payload;
        }

        public string DbName { get; }
        public string EventType { get; }
        public string Payload { get; }
        public string? CorrelationId { get; set; }
    }

    private sealed class DatabaseOperationCommand : ICommand<string>
    {
        public DatabaseOperationCommand(string dbName, string operation)
        {
            DbName = dbName;
            Operation = operation;
        }

        public string DbName { get; }
        public string Operation { get; }
        public string? CorrelationId { get; set; }
    }

    private sealed class DatabaseCommandHandler : ICommandHandler<CreateRecordCommand, int>
    {
        private readonly string _connectionString;

        public DatabaseCommandHandler(string connectionString) => _connectionString = connectionString;

        public int ExecutionCount { get; private set; }

        public async Task<int> HandleAsync(CreateRecordCommand request, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Simple operation that should succeed
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
    }

    private sealed class SlowDatabaseQueryHandler : ICommandHandler<SlowQueryCommand, int>
    {
        private readonly string _connectionString;
        private readonly TimeSpan _delay;

        public SlowDatabaseQueryHandler(string connectionString, TimeSpan delay)
        {
            _connectionString = connectionString;
            _delay = delay;
        }

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities - Delay value is from trusted internal source
        public async Task<int> HandleAsync(SlowQueryCommand request, CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Simulate a slow query using WAITFOR DELAY
            var delayString = _delay.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            await using var command = connection.CreateCommand();
            command.CommandText = $"WAITFOR DELAY '{delayString}'; SELECT 1;";
            command.CommandTimeout = (int)_delay.TotalSeconds + 10;

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
#pragma warning restore CA2100
    }

    private sealed class OutboxStoreHandler : ICommandHandler<StoreOutboxCommand, bool>
    {
        private readonly string _connectionString;

        public OutboxStoreHandler(string connectionString) => _connectionString = connectionString;

        public async Task<bool> HandleAsync(StoreOutboxCommand request, CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO OutboxMessage (Id, EventType, Payload, CreatedAt, State)
                VALUES (@Id, @EventType, @Payload, @CreatedAt, 0)
                """;

            _ = command.Parameters.AddWithValue("@Id", Guid.NewGuid());
            _ = command.Parameters.AddWithValue("@EventType", request.EventType);
            _ = command.Parameters.AddWithValue("@Payload", request.Payload);
            _ = command.Parameters.AddWithValue("@CreatedAt", DateTimeOffset.UtcNow);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rowsAffected > 0;
        }
    }

    private sealed class FailingDatabaseHandler : ICommandHandler<DatabaseOperationCommand, string>
    {
        private readonly string _connectionString;
        private readonly int _failCount;
        private int _attemptCount;

        public FailingDatabaseHandler(string connectionString, int failCount)
        {
            _connectionString = connectionString;
            _failCount = failCount;
        }

        public int AttemptCount => _attemptCount;

        public async Task<string> HandleAsync(
            DatabaseOperationCommand request,
            CancellationToken cancellationToken = default
        )
        {
            var attempt = Interlocked.Increment(ref _attemptCount);

            if (attempt <= _failCount)
            {
                throw new InvalidOperationException($"Simulated database failure on attempt {attempt}");
            }

            // Actual database operation on success
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 'Success'";
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return result?.ToString() ?? "Unknown";
        }
    }

    #endregion
}

/// <summary>
/// Fixture that manages a SQL Server container for database integration tests.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    /// <summary>
    /// Initializes the SQL Server container.
    /// </summary>
    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    /// <summary>
    /// Gets a connection string for the specified database.
    /// </summary>
    /// <param name="database">The database name.</param>
    /// <returns>The connection string.</returns>
    public string GetConnectionString(string database)
    {
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates a new database.
    /// </summary>
    /// <param name="database">The database name.</param>
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities - Database name is generated internally
    public async Task CreateDatabaseAsync(string database)
    {
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{database}]";
        _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
#pragma warning restore CA2100

    /// <summary>
    /// Drops a database.
    /// </summary>
    /// <param name="database">The database name.</param>
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities - Database name is generated internally
    public async Task DropDatabaseAsync(string database)
    {
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF EXISTS (SELECT name FROM sys.databases WHERE name = N'{database}')
            BEGIN
                ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{database}];
            END
            """;
        _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
#pragma warning restore CA2100

    /// <summary>
    /// Initializes the outbox schema in the specified database.
    /// </summary>
    /// <param name="database">The database name.</param>
    public async Task InitializeOutboxSchemaAsync(string database)
    {
        await using var connection = new SqlConnection(GetConnectionString(database));
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE OutboxMessage (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                EventType NVARCHAR(512) NOT NULL,
                Payload NVARCHAR(MAX) NOT NULL,
                CreatedAt DATETIMEOFFSET NOT NULL,
                ProcessedAt DATETIMEOFFSET NULL,
                RetryCount INT NOT NULL DEFAULT 0,
                State INT NOT NULL DEFAULT 0,
                LastError NVARCHAR(MAX) NULL
            )
            """;
        _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the SQL Server container.
    /// </summary>
    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
