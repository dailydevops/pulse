namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class IdempotencyKeyConfigurationMetadataTests
{
    [Test]
    public async Task Configure_WithSqlServerConfiguration_UsesBinaryCollationWithinIndexKeyLimit()
    {
        var property = GetKeyProperty(new SqlServerIdempotencyKeyConfiguration());

        using (Assert.Multiple())
        {
            // NVARCHAR stores 2 bytes per character, and a clustered index key is limited to 900 bytes.
            _ = await Assert.That(property.GetColumnType()).IsEqualTo("nvarchar(450)");
            _ = await Assert.That(property.GetCollation()).IsEqualTo("Latin1_General_100_BIN2");
        }
    }

    [Test]
    public async Task Configure_WithMySqlConfiguration_UsesBinaryCollation()
    {
        var property = GetKeyProperty(new MySqlIdempotencyKeyConfiguration());

        _ = await Assert.That(property.GetCollation()).IsEqualTo("utf8mb4_bin");
    }

    [Test]
    public async Task Configure_WithInMemoryConfiguration_UsesCentralMaxLength()
    {
        var property = GetKeyProperty(new InMemoryIdempotencyKeyConfiguration());

        _ = await Assert.That(property.GetMaxLength()).IsEqualTo(IdempotencyKeySchema.MaxLengths.IdempotencyKey);
    }

    [Test]
    public async Task Configure_WithPostgreSqlConfiguration_KeepsModelMaxLengthOfExistingMigrations()
    {
        var property = GetKeyProperty(new PostgreSqlIdempotencyKeyConfiguration());

        _ = await Assert.That(property.GetMaxLength()).IsEqualTo(500);
    }

    [Test]
    public async Task Configure_WithSqliteConfiguration_KeepsModelMaxLengthOfExistingMigrations()
    {
        var property = GetKeyProperty(new SqliteIdempotencyKeyConfiguration());

        _ = await Assert.That(property.GetMaxLength()).IsEqualTo(500);
    }

    [Test]
    public async Task Configure_WithMySqlConfiguration_KeepsModelMaxLengthOfExistingMigrations()
    {
        var property = GetKeyProperty(new MySqlIdempotencyKeyConfiguration());

        _ = await Assert.That(property.GetMaxLength()).IsEqualTo(500);
    }

    private static IMutableProperty GetKeyProperty(IEntityTypeConfiguration<IdempotencyKey> configuration)
    {
        var modelBuilder = new ModelBuilder();
        _ = modelBuilder.ApplyConfiguration(configuration);
        return modelBuilder.Model.FindEntityType(typeof(IdempotencyKey))!.FindProperty(nameof(IdempotencyKey.Key))!;
    }
}
