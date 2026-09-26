namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Outbox")]
public sealed class OutboxEventTypeResolverTests
{
    private const string UnresolvableTypeName =
        "NetEvolve.Pulse.DoesNotExist.RemovedEvent, NetEvolve.Pulse.NoSuchAssembly";

    [Test]
    public async Task Resolve_WithKnownTypeName_ReturnsExpectedType()
    {
        var resolved = OutboxEventTypeResolver.Resolve(typeof(OutboxEventTypeResolverTests).AssemblyQualifiedName!);

        using (Assert.Multiple())
        {
            _ = await Assert.That(resolved).IsEqualTo(typeof(OutboxEventTypeResolverTests));
            _ = await Assert.That(OutboxEventTypeResolver.IsUnresolvable(resolved)).IsFalse();
        }
    }

    [Test]
    public async Task Resolve_CalledTwiceWithSameName_ReturnsCachedSameInstance()
    {
        var typeName = typeof(OutboxEventTypeResolverTests).AssemblyQualifiedName!;

        var first = OutboxEventTypeResolver.Resolve(typeName);
        var second = OutboxEventTypeResolver.Resolve(typeName);

        _ = await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async Task Resolve_WithUnresolvableTypeName_ReturnsPlaceholderWithStoredName()
    {
        var resolved = OutboxEventTypeResolver.Resolve(UnresolvableTypeName);

        using (Assert.Multiple())
        {
            _ = await Assert.That(OutboxEventTypeResolver.IsUnresolvable(resolved)).IsTrue();
            _ = await Assert.That(resolved.ToOutboxEventTypeName()).IsEqualTo(UnresolvableTypeName);
            _ = await Assert.That(resolved.FullName).IsEqualTo("NetEvolve.Pulse.DoesNotExist.RemovedEvent");
            _ = await Assert.That(resolved.Name).IsEqualTo("RemovedEvent");
            _ = await Assert.That(resolved.Namespace).IsEqualTo("NetEvolve.Pulse.DoesNotExist");
        }
    }

    [Test]
    public async Task Resolve_WithUnresolvableGenericTypeName_KeepsGenericArgumentsInFullName()
    {
        const string typeName =
            "Removed.Envelope`1[[System.String, System.Private.CoreLib, Version=8.0.0.0]], Removed.Assembly";

        var resolved = OutboxEventTypeResolver.Resolve(typeName);

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(resolved.FullName)
                .IsEqualTo("Removed.Envelope`1[[System.String, System.Private.CoreLib, Version=8.0.0.0]]");
            _ = await Assert.That(resolved.Namespace).IsEqualTo("Removed");
            _ = await Assert.That(resolved.ToOutboxEventTypeName()).IsEqualTo(typeName);
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("Foo[[")]
    [Arguments("Foo]], Bar")]
    [Arguments("Foo, ")]
    [Arguments("Foo, Bar, Version=not-a-version")]
    [Arguments("Foo, Bar, Culture=xx-invalid-culture")]
    [Arguments("Foo, Bar, PublicKeyToken=zz")]
    [Arguments("Foo, Bar/Baz:Qux")]
    [Arguments("System.Collections.Generic.List`1[[Foo, Bar]]")]
    public async Task Resolve_WithCorruptTypeName_ReturnsPlaceholderInsteadOfThrowing(string typeName)
    {
        var resolved = OutboxEventTypeResolver.Resolve(typeName);

        using (Assert.Multiple())
        {
            _ = await Assert.That(OutboxEventTypeResolver.IsUnresolvable(resolved)).IsTrue();
            _ = await Assert.That(resolved.ToOutboxEventTypeName()).IsEqualTo(typeName);
        }
    }

    [Test]
    public async Task Resolve_WithUnresolvableTypeName_PlaceholdersAreEqualByStoredName()
    {
        var first = OutboxEventTypeResolver.Resolve(UnresolvableTypeName);
        var second = OutboxEventTypeResolver.Resolve(UnresolvableTypeName);
        var other = OutboxEventTypeResolver.Resolve("Other.RemovedEvent, Other");

        using (Assert.Multiple())
        {
            _ = await Assert.That(first.Equals(second)).IsTrue();
            _ = await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
            _ = await Assert.That(first.Equals(other)).IsFalse();
            _ = await Assert.That(first.Equals(typeof(object))).IsFalse();
        }
    }

    [Test]
    public async Task DeadLetterUnresolvableAsync_AllResolved_ReturnsSameListWithoutDeadLettering()
    {
        var repository = Mock.Of<IOutboxRepository>();
        OutboxMessage[] messages = [CreateMessage(typeof(string)), CreateMessage(typeof(int))];

        var result = await repository.Object.DeadLetterUnresolvableAsync(messages).ConfigureAwait(false);

        _ = await Assert.That(result).IsSameReferenceAs(messages);
        repository
            .MarkAsDeadLetterAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .WasCalled(Times.Never);
    }

    [Test]
    public async Task DeadLetterUnresolvableAsync_WithUnresolvable_DeadLettersItAndKeepsOthersInOrder()
    {
        var repository = Mock.Of<IOutboxRepository>();
        var first = CreateMessage(typeof(string));
        var unresolvable = CreateMessage(OutboxEventTypeResolver.Resolve(UnresolvableTypeName));
        var last = CreateMessage(typeof(int));

        var result = await repository
            .Object.DeadLetterUnresolvableAsync([first, unresolvable, last])
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEquivalentTo([first, last]);
        repository
            .MarkAsDeadLetterAsync(
                unresolvable.Id,
                Arg.Is<string>(error =>
                    error != null && error.Contains(UnresolvableTypeName, StringComparison.Ordinal)
                ),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
        repository
            .MarkAsDeadLetterAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task DeadLetterUnresolvableAsync_WithNullRepository_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(async () => _ = await OutboxEventTypeResolver.DeadLetterUnresolvableAsync(null!, []))
            .Throws<ArgumentNullException>();

    private static OutboxMessage CreateMessage(Type eventType) =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = "{}",
        };
}
