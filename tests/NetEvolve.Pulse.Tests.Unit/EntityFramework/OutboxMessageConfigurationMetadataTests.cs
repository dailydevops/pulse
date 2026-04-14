namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class OutboxMessageConfigurationMetadataTests
{
    [Test]
    public async Task Create_WithSqlServerProvider_AppliesSqlServerFiltersAndColumnTypes()
    {
        var entityType = GetConfiguredEntityType("Microsoft.EntityFrameworkCore.SqlServer");

        var pendingIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_CreatedAt");
        var retryIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_NextRetryAt");
        var completedIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_ProcessedAt");

        using (Assert.Multiple())
        {
            _ = await Assert.That(pendingIndex.GetFilter()).IsEqualTo("[Status] IN (0, 3)");
            _ = await Assert.That(retryIndex.GetFilter()).IsEqualTo("[Status] = 3 AND [NextRetryAt] IS NOT NULL");
            _ = await Assert.That(completedIndex.GetFilter()).IsEqualTo("[Status] = 2");

            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.Id))!.GetColumnType())
                .IsEqualTo("uniqueidentifier");
            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.CreatedAt))!.GetColumnType())
                .IsEqualTo("datetimeoffset");
            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.Status))!.GetColumnType())
                .IsEqualTo("int");
        }
    }

    [Test]
    public async Task Create_WithPostgreSqlProvider_AppliesPostgreSqlFiltersAndColumnTypes()
    {
        var entityType = GetConfiguredEntityType("Npgsql.EntityFrameworkCore.PostgreSQL");

        var pendingIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_CreatedAt");
        var retryIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_NextRetryAt");
        var completedIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_ProcessedAt");

        using (Assert.Multiple())
        {
            _ = await Assert.That(pendingIndex.GetFilter()).IsEqualTo("\"Status\" IN (0, 3)");
            _ = await Assert.That(retryIndex.GetFilter()).IsEqualTo("\"Status\" = 3 AND \"NextRetryAt\" IS NOT NULL");
            _ = await Assert.That(completedIndex.GetFilter()).IsEqualTo("\"Status\" = 2");

            _ = await Assert.That(entityType.FindProperty(nameof(OutboxMessage.Id))!.GetColumnType()).IsEqualTo("uuid");
            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.CreatedAt))!.GetColumnType())
                .IsEqualTo("timestamp with time zone");
            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.Status))!.GetColumnType())
                .IsEqualTo("integer");
        }
    }

    [Test]
    public async Task Create_WithSqliteProvider_AppliesSqliteFiltersAndColumnTypes()
    {
        var entityType = GetConfiguredEntityType("Microsoft.EntityFrameworkCore.Sqlite");

        var pendingIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_CreatedAt");
        var retryIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_NextRetryAt");
        var completedIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_ProcessedAt");

        using (Assert.Multiple())
        {
            _ = await Assert.That(pendingIndex.GetFilter()).IsEqualTo("\"Status\" IN (0, 3)");
            _ = await Assert.That(retryIndex.GetFilter()).IsEqualTo("\"Status\" = 3 AND \"NextRetryAt\" IS NOT NULL");
            _ = await Assert.That(completedIndex.GetFilter()).IsEqualTo("\"Status\" = 2");

            _ = await Assert.That(entityType.FindProperty(nameof(OutboxMessage.Id))!.GetColumnType()).IsEqualTo("TEXT");
            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.RetryCount))!.GetColumnType())
                .IsEqualTo("INTEGER");
            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.Status))!.GetColumnType())
                .IsEqualTo("INTEGER");
        }
    }

    [Test]
    public async Task Create_WithMySqlProvider_AppliesMySqlColumnTypesAndNoFilteredIndexes()
    {
        var entityType = GetConfiguredEntityType("Pomelo.EntityFrameworkCore.MySql");

        var pendingIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_CreatedAt");
        var retryIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_NextRetryAt");
        var completedIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_ProcessedAt");

        using (Assert.Multiple())
        {
            _ = await Assert.That(pendingIndex.GetFilter()).IsNull();
            _ = await Assert.That(retryIndex.GetFilter()).IsNull();
            _ = await Assert.That(completedIndex.GetFilter()).IsNull();

            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.Id))!.GetColumnType())
                .IsEqualTo("binary(16)");
            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.CreatedAt))!.GetColumnType())
                .IsEqualTo("bigint");
            _ = await Assert
                .That(entityType.FindProperty(nameof(OutboxMessage.Payload))!.GetColumnType())
                .IsEqualTo("longtext");
        }
    }

    [Test]
    public async Task Create_WithInMemoryProvider_UsesBaseDefaultsWithoutColumnTypeOverrides()
    {
        var entityType = GetConfiguredEntityType("Microsoft.EntityFrameworkCore.InMemory");

        var pendingIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_CreatedAt");
        var retryIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_NextRetryAt");
        var completedIndex = GetIndex(entityType, "IX_pulse_OutboxMessage_Status_ProcessedAt");

        using (Assert.Multiple())
        {
            _ = await Assert.That(pendingIndex.GetFilter()).IsNull();
            _ = await Assert.That(retryIndex.GetFilter()).IsNull();
            _ = await Assert.That(completedIndex.GetFilter()).IsNull();

            _ = await Assert.That(entityType.FindProperty(nameof(OutboxMessage.Id))!.GetColumnType()).IsNull();
            _ = await Assert.That(entityType.FindProperty(nameof(OutboxMessage.Status))!.GetColumnType()).IsNull();
        }
    }

    [Test]
    public async Task Create_WithWhitespaceSchema_UsesDefaultSchemaAndConfiguredTableName()
    {
        var entityType = GetConfiguredEntityType(
            "Microsoft.EntityFrameworkCore.SqlServer",
            new OutboxOptions { Schema = "   ", TableName = "CustomOutbox" }
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(entityType.GetSchema()).IsEqualTo(OutboxMessageSchema.DefaultSchema);
            _ = await Assert.That(entityType.GetTableName()).IsEqualTo("CustomOutbox");
        }
    }

    [Test]
    public async Task Create_WithSchemaContainingWhitespace_StoresTrimmedSchema()
    {
        var entityType = GetConfiguredEntityType(
            "Microsoft.EntityFrameworkCore.SqlServer",
            new OutboxOptions { Schema = " custom ", TableName = "OutboxMessages" }
        );

        _ = await Assert.That(entityType.GetSchema()).IsEqualTo("custom");
    }

    [Test]
    public async Task Create_WithSqlServerProvider_AppliesOutboxBaseDefaultValues()
    {
        var entityType = GetConfiguredEntityType("Microsoft.EntityFrameworkCore.SqlServer");

        var retryCountDefault = entityType.FindProperty(nameof(OutboxMessage.RetryCount))!.GetDefaultValue();
        var statusDefault = entityType.FindProperty(nameof(OutboxMessage.Status))!.GetDefaultValue();

        using (Assert.Multiple())
        {
            _ = await Assert.That(retryCountDefault).IsEqualTo(0);
            _ = await Assert.That(statusDefault).IsEqualTo(OutboxMessageStatus.Pending);
        }
    }

    private static IMutableEntityType GetConfiguredEntityType(string providerName, OutboxOptions? options = null)
    {
        var modelBuilder = new ModelBuilder();
        var resolvedOptions = Options.Create(options ?? new OutboxOptions());

        IEntityTypeConfiguration<OutboxMessage> configuration = providerName switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => new PostgreSqlOutboxMessageConfiguration(resolvedOptions),
            "Microsoft.EntityFrameworkCore.Sqlite" => new SqliteOutboxMessageConfiguration(resolvedOptions),
            "Microsoft.EntityFrameworkCore.SqlServer" => new SqlServerOutboxMessageConfiguration(resolvedOptions),
            "Pomelo.EntityFrameworkCore.MySql" or "MySql.EntityFrameworkCore" => new MySqlOutboxMessageConfiguration(
                resolvedOptions
            ),
            "Microsoft.EntityFrameworkCore.InMemory" => new InMemoryOutboxMessageConfiguration(resolvedOptions),
            _ => throw new NotSupportedException($"Unsupported EF Core provider: {providerName}"),
        };

        _ = modelBuilder.ApplyConfiguration(configuration);
        return modelBuilder.Model.FindEntityType(typeof(OutboxMessage))!;
    }

    private static IMutableIndex GetIndex(IMutableEntityType entityType, string indexName) =>
        entityType
            .GetIndexes()
            .Single(index => string.Equals(index.GetDatabaseName(), indexName, StringComparison.Ordinal));
}
