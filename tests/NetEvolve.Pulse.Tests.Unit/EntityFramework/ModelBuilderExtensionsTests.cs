namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class ModelBuilderExtensionsTests
{
    [Test]
    public async Task ApplyPulseConfiguration_When_modelBuilder_is_null_throws_ArgumentNullException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(ApplyPulseConfiguration_When_modelBuilder_is_null_throws_ArgumentNullException))
            .Options;
        await using var context = new TestDbContext(options);

        _ = await Assert
            .That(() => ModelBuilderExtensions.ApplyPulseConfiguration(null!, context))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ApplyPulseConfiguration_When_context_is_null_throws_ArgumentNullException()
    {
        var modelBuilder = new ModelBuilder();

        _ = await Assert
            .That(() => modelBuilder.ApplyPulseConfiguration<TestDbContext>(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ApplyPulseConfiguration_When_context_is_not_IOutboxDbContext_returns_same_modelBuilder()
    {
        var options = new DbContextOptionsBuilder<PlainDbContext>()
            .UseInMemoryDatabase(
                nameof(ApplyPulseConfiguration_When_context_is_not_IOutboxDbContext_returns_same_modelBuilder)
            )
            .Options;
        await using var context = new PlainDbContext(options);
        var modelBuilder = new ModelBuilder();

        var result = modelBuilder.ApplyPulseConfiguration(context);

        _ = await Assert.That(result).IsSameReferenceAs(modelBuilder);
    }

    [Test]
    public async Task ApplyPulseConfiguration_When_context_implements_IOutboxDbContext_returns_same_modelBuilder()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(
                nameof(ApplyPulseConfiguration_When_context_implements_IOutboxDbContext_returns_same_modelBuilder)
            )
            .Options;
        await using var context = new TestDbContext(options);
        var modelBuilder = new ModelBuilder();

        var result = modelBuilder.ApplyPulseConfiguration(context);

        _ = await Assert.That(result).IsSameReferenceAs(modelBuilder);
    }

    private sealed class PlainDbContext : DbContext
    {
        public PlainDbContext(DbContextOptions<PlainDbContext> options)
            : base(options) { }
    }
}
