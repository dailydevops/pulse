namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class OutboxMessageConfigurationFactoryTests
{
    [Test]
    public async Task Create_WithNullDbContext_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => OutboxMessageConfigurationFactory.Create(context: null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Create_WithNullProviderName_ThrowsNotSupportedException() =>
        _ = await Assert
            .That(() => OutboxMessageConfigurationFactory.Create(providerName: null))
            .Throws<NotSupportedException>();

    [Test]
    public async Task Create_WithUnknownProviderName_ThrowsNotSupportedException() =>
        _ = await Assert
            .That(() => OutboxMessageConfigurationFactory.Create("Unknown.Provider"))
            .Throws<NotSupportedException>();

    [Test]
    public async Task Create_WithInMemoryDbContext_ReturnsConfiguration()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Create_WithInMemoryDbContext_ReturnsConfiguration))
            .Options;
        await using var context = new TestDbContext(options);

        var result = OutboxMessageConfigurationFactory.Create(context);

        _ = await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Create_WithSqlServerProviderName_ReturnsSqlServerConfiguration()
    {
        var result = OutboxMessageConfigurationFactory.Create("Microsoft.EntityFrameworkCore.SqlServer");

        _ = await Assert.That(result).IsTypeOf<SqlServerOutboxMessageConfiguration>();
    }

    [Test]
    public async Task Create_WithPostgreSqlProviderName_ReturnsPostgreSqlConfiguration()
    {
        var result = OutboxMessageConfigurationFactory.Create("Npgsql.EntityFrameworkCore.PostgreSQL");

        _ = await Assert.That(result).IsTypeOf<PostgreSqlOutboxMessageConfiguration>();
    }

    [Test]
    public async Task Create_WithSqliteProviderName_ReturnsSqliteConfiguration()
    {
        var result = OutboxMessageConfigurationFactory.Create("Microsoft.EntityFrameworkCore.Sqlite");

        _ = await Assert.That(result).IsTypeOf<SqliteOutboxMessageConfiguration>();
    }

    [Test]
    public async Task Create_WithPomeloMySqlProviderName_ReturnsMySqlConfiguration()
    {
        var result = OutboxMessageConfigurationFactory.Create("Pomelo.EntityFrameworkCore.MySql");

        _ = await Assert.That(result).IsTypeOf<MySqlOutboxMessageConfiguration>();
    }

    [Test]
    public async Task Create_WithOracleMySqlProviderName_ReturnsMySqlConfiguration()
    {
        var result = OutboxMessageConfigurationFactory.Create("MySql.EntityFrameworkCore");

        _ = await Assert.That(result).IsTypeOf<MySqlOutboxMessageConfiguration>();
    }

    [Test]
    public async Task Create_WithInMemoryProviderName_ReturnsInMemoryConfiguration()
    {
        var result = OutboxMessageConfigurationFactory.Create("Microsoft.EntityFrameworkCore.InMemory");

        _ = await Assert.That(result).IsTypeOf<InMemoryOutboxMessageConfiguration>();
    }

    [Test]
    public async Task Create_WithDbContext_WithOptions_ReturnsConfigurationForInMemory()
    {
        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Create_WithDbContext_WithOptions_ReturnsConfigurationForInMemory))
            .Options;
        await using var context = new TestDbContext(dbOptions);
        var outboxOptions = Options.Create(new OutboxOptions { Schema = "custom" });

        var result = OutboxMessageConfigurationFactory.Create(context, outboxOptions);

        _ = await Assert.That(result).IsNotNull();
    }
}
