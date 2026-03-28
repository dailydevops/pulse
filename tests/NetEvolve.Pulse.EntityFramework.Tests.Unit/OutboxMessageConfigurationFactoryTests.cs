namespace NetEvolve.Pulse.EntityFramework.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

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
    public async Task Create_WithInMemoryDbContext_ThrowsNotSupportedException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Create_WithInMemoryDbContext_ThrowsNotSupportedException))
            .Options;
        await using var context = new TestDbContext(options);

        _ = await Assert.That(() => OutboxMessageConfigurationFactory.Create(context)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task Create_WithDbContext_WithOptions_ThrowsNotSupportedExceptionForInMemory()
    {
        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Create_WithDbContext_WithOptions_ThrowsNotSupportedExceptionForInMemory))
            .Options;
        await using var context = new TestDbContext(dbOptions);
        var outboxOptions = Options.Create(new OutboxOptions { Schema = "custom" });

        _ = await Assert
            .That(() => OutboxMessageConfigurationFactory.Create(context, outboxOptions))
            .Throws<NotSupportedException>();
    }
}
