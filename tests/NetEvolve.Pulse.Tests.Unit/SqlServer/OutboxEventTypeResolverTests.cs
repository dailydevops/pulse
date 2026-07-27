namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("SqlServer")]
public sealed class OutboxEventTypeResolverTests
{
    [Test]
    public async Task Resolve_WithKnownTypeName_ReturnsExpectedType()
    {
        var typeName = typeof(OutboxEventTypeResolverTests).AssemblyQualifiedName!;

        var resolved = OutboxEventTypeResolver.Resolve(typeName);

        _ = await Assert.That(resolved).IsEqualTo(typeof(OutboxEventTypeResolverTests));
    }

    [Test]
    public async Task Resolve_CalledTwiceWithSameName_ReturnsCachedSameInstance()
    {
        var typeName = typeof(OutboxEventTypeResolverTests).AssemblyQualifiedName!;

        var first = OutboxEventTypeResolver.Resolve(typeName);
        var second = OutboxEventTypeResolver.Resolve(typeName);

        _ = await Assert.That(second).IsSameReferenceAs(first!);
    }

    [Test]
    public async Task Resolve_WithUnresolvableTypeName_ReturnsNull()
    {
        var resolved = OutboxEventTypeResolver.Resolve("NetEvolve.Pulse.DoesNotExist, NetEvolve.Pulse.NoSuchAssembly");

        _ = await Assert.That(resolved).IsNull();
    }
}
