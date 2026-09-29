namespace NetEvolve.Pulse.Tests.Integration.Schema;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;
using TUnit.Core;

/// <summary>
/// Checks the DDL that the EF Core providers generate for the idempotency key column.
/// Generating the create script needs no database connection, so these tests run without Docker.
/// </summary>
[TestGroup("EntityFramework")]
public sealed class EntityFrameworkIdempotencyKeyCreateScriptTests
{
    [Test]
    public async Task GenerateCreateScript_WithMySqlProvider_DeclaresBinaryKeyCollation(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new DbContextOptionsBuilder<EntityFrameworkIdempotencyInitializer.TestIdempotencyDbContext>()
            .UseMySQL("Server=localhost;Database=pulse;Uid=pulse;Pwd=pulse")
            .Options;

        var script = GenerateCreateScript(options);

        _ = await Assert.That(script).Contains("`IdempotencyKey` varchar(500) COLLATE utf8mb4_bin NOT NULL");
    }

    [Test]
    public async Task GenerateCreateScript_WithSqlServerProvider_DeclaresBinaryKeyCollation(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new DbContextOptionsBuilder<EntityFrameworkIdempotencyInitializer.TestIdempotencyDbContext>()
            .UseSqlServer("Server=localhost;Database=pulse;Integrated Security=true")
            .Options;

        var script = GenerateCreateScript(options);

        _ = await Assert
            .That(script)
            .Contains("[IdempotencyKey] nvarchar(450) COLLATE Latin1_General_100_BIN2 NOT NULL");
    }

    private static string GenerateCreateScript(
        DbContextOptions<EntityFrameworkIdempotencyInitializer.TestIdempotencyDbContext> options
    )
    {
        using var context = new EntityFrameworkIdempotencyInitializer.TestIdempotencyDbContext(options);
        return context.Database.GenerateCreateScript();
    }
}
