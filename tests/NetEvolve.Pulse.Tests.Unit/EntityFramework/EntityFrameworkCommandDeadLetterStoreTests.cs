namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkCommandDeadLetterStoreTests
{
    private static TestCommandDeadLetterDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestCommandDeadLetterDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new TestCommandDeadLetterDbContext(options);
    }

    private static EntityFrameworkCommandDeadLetterStore<TestCommandDeadLetterDbContext> CreateStore(
        TestCommandDeadLetterDbContext context
    ) => new(context);

    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new EntityFrameworkCommandDeadLetterStore<TestCommandDeadLetterDbContext>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidContext_CreatesInstance()
    {
        var context = CreateContext(nameof(Constructor_WithValidContext_CreatesInstance));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert.That(store).IsNotNull();
        }
    }

    [Test]
    public async Task StoreAsync_WithNullCommandType_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WithNullCommandType_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert
                .That(async () =>
                    await store
                        .StoreAsync(null!, "{}", new InvalidOperationException("boom"), cancellationToken)
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task StoreAsync_WithNullPayload_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WithNullPayload_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert
                .That(async () =>
                    await store
                        .StoreAsync("Some.Command", null!, new InvalidOperationException("boom"), cancellationToken)
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task StoreAsync_WithNullException_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WithNullException_ThrowsArgumentNullException));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert
                .That(async () =>
                    await store.StoreAsync("Some.Command", "{}", null!, cancellationToken).ConfigureAwait(false)
                )
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task StoreAsync_WithValidArguments_StoresNewEntryWithAllFieldsPopulated(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(nameof(StoreAsync_WithValidArguments_StoresNewEntryWithAllFieldsPopulated));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);
            var exception = new InvalidOperationException("Something went wrong");

            await store
                .StoreAsync("MyApp.Commands.CreateOrderCommand", "{\"orderId\":42}", exception, cancellationToken)
                .ConfigureAwait(false);

            var entry = await context.CommandDeadLetterEntries.SingleAsync(cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(entry.Id).IsNotEqualTo(Guid.Empty);
                _ = await Assert.That(entry.CommandType).IsEqualTo("MyApp.Commands.CreateOrderCommand");
                _ = await Assert.That(entry.Payload).IsEqualTo("{\"orderId\":42}");
                _ = await Assert.That(entry.ExceptionType).IsEqualTo(exception.GetType().AssemblyQualifiedName);
                _ = await Assert.That(entry.ExceptionMessage).IsEqualTo("Something went wrong");
                _ = await Assert.That(entry.AttemptCount).IsEqualTo(1);
                _ = await Assert.That(entry.Status).IsEqualTo(CommandDeadLetterStatus.New);
            }
        }
    }

    [Test]
    public async Task StoreAsync_CalledTwice_StoresTwoDistinctEntries(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_CalledTwice_StoresTwoDistinctEntries));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);
            var exception = new InvalidOperationException("boom");

            await store.StoreAsync("Some.Command", "{}", exception, cancellationToken).ConfigureAwait(false);
            await store.StoreAsync("Some.Command", "{}", exception, cancellationToken).ConfigureAwait(false);

            var count = await context.CommandDeadLetterEntries.CountAsync(cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(count).IsEqualTo(2);
        }
    }

    [Test]
    public async Task IsDuplicateKeyException_WithInMemoryArgumentException_ReturnsTrue()
    {
        var ex = new ArgumentException("An item with the same key has already been added. Key: some-id");

        var result = EntityFrameworkCommandDeadLetterStore<TestCommandDeadLetterDbContext>.IsDuplicateKeyException(ex);

        _ = await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsDuplicateKeyException_WithUnrelatedArgumentException_ReturnsFalse()
    {
        var ex = new ArgumentException("Value does not fall within the expected range.");

        var result = EntityFrameworkCommandDeadLetterStore<TestCommandDeadLetterDbContext>.IsDuplicateKeyException(ex);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsDuplicateKeyException_WithDbUpdateExceptionContainingSqlServer2627_ReturnsTrue()
    {
        var inner = new InvalidOperationException(
            "Violation of PRIMARY KEY constraint 'PK_pulse_CommandDeadLetter'. Cannot insert duplicate key in object 'pulse.CommandDeadLetter'. The duplicate key value is (id). The statement has been terminated."
        );
        var ex = new DbUpdateException("An error occurred", inner);

        var result = EntityFrameworkCommandDeadLetterStore<TestCommandDeadLetterDbContext>.IsDuplicateKeyException(ex);

        _ = await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsDuplicateKeyException_WithDbUpdateExceptionContainingPostgres23505_ReturnsTrue()
    {
        var inner = new InvalidOperationException("23505: duplicate key value violates unique constraint");
        var ex = new DbUpdateException("An error occurred", inner);

        var result = EntityFrameworkCommandDeadLetterStore<TestCommandDeadLetterDbContext>.IsDuplicateKeyException(ex);

        _ = await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsDuplicateKeyException_WithUnrelatedExceptionType_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Something went wrong");

        var result = EntityFrameworkCommandDeadLetterStore<TestCommandDeadLetterDbContext>.IsDuplicateKeyException(ex);

        _ = await Assert.That(result).IsFalse();
    }
}
