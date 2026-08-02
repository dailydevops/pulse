namespace NetEvolve.Pulse.Tests.Unit.Idempotency;

using System;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Idempotency")]
public sealed class IdempotencyConflictExceptionTests
{
    [Test]
    public async Task Constructor_Parameterless_SetsDefaultMessageAndEmptyKey()
    {
        var exception = new IdempotencyConflictException();

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(exception.Message)
                .IsEqualTo("A command with the given idempotency key has already been processed.");
            _ = await Assert.That(exception.IdempotencyKey).IsEqualTo(string.Empty);
            _ = await Assert.That(exception.InnerException).IsNull();
        }
    }

    [Test]
    public async Task Constructor_WithIdempotencyKey_SetsMessageAndKey()
    {
        var exception = new IdempotencyConflictException("key-123");

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(exception.Message)
                .IsEqualTo("A command with idempotency key 'key-123' has already been processed.");
            _ = await Assert.That(exception.IdempotencyKey).IsEqualTo("key-123");
            _ = await Assert.That(exception.InnerException).IsNull();
        }
    }

    [Test]
    public async Task Constructor_WithIdempotencyKey_NullKey_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new IdempotencyConflictException((string)null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithKeyMessageAndInnerException_SetsAllProperties()
    {
        var innerException = new InvalidOperationException("inner");

        var exception = new IdempotencyConflictException("key-456", "custom message", innerException);

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception.Message).IsEqualTo("custom message");
            _ = await Assert.That(exception.IdempotencyKey).IsEqualTo("key-456");
            _ = await Assert.That(exception.InnerException).IsSameReferenceAs(innerException);
        }
    }

    [Test]
    public async Task Constructor_WithKeyMessageAndInnerException_NullKey_ThrowsArgumentNullException()
    {
        var innerException = new InvalidOperationException("inner");

        _ = await Assert
            .That(() => new IdempotencyConflictException(null!, "custom message", innerException))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithMessageAndInnerException_SetsMessageAndEmptyKey()
    {
        var innerException = new InvalidOperationException("inner");

        var exception = new IdempotencyConflictException("custom message", innerException);

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception.Message).IsEqualTo("custom message");
            _ = await Assert.That(exception.IdempotencyKey).IsEqualTo(string.Empty);
            _ = await Assert.That(exception.InnerException).IsSameReferenceAs(innerException);
        }
    }
}
