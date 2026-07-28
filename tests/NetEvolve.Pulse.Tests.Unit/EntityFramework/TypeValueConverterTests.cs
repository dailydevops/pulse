namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Configurations;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class TypeValueConverterTests
{
    [Test]
    public async Task Constructor_Creates_instance()
    {
        var converter = new TypeValueConverter();

        _ = await Assert.That(converter).IsNotNull();
    }

    [Test]
    public async Task ConvertToProvider_With_valid_type_returns_assembly_qualified_name()
    {
        var converter = new TypeValueConverter();
        var toProvider = converter.ConvertToProvider;

        var result = toProvider(typeof(string)) as string;

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsNotNull();
            _ = await Assert.That(result).IsNotEmpty();
        }
    }

    [Test]
    public async Task ConvertFromProvider_With_valid_type_name_returns_type()
    {
        var converter = new TypeValueConverter();
        var fromProvider = converter.ConvertFromProvider;

        var result = fromProvider(typeof(string).AssemblyQualifiedName) as Type;

        _ = await Assert.That(result).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task ConvertFromProvider_With_invalid_type_name_throws_InvalidOperationException()
    {
        var converter = new TypeValueConverter();
        var fromProvider = converter.ConvertFromProvider;

        _ = await Assert
            .That(() => fromProvider("Invalid.Type.Name, InvalidAssembly"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ConvertFromProvider_Called_repeatedly_returns_same_type()
    {
        var converter = new TypeValueConverter();
        var fromProvider = converter.ConvertFromProvider;
        var typeName = typeof(TypeValueConverterTests).AssemblyQualifiedName;

        var first = fromProvider(typeName) as Type;
        var second = fromProvider(typeName) as Type;

        using (Assert.Multiple())
        {
            _ = await Assert.That(first).IsEqualTo(typeof(TypeValueConverterTests));
            _ = await Assert.That(second).IsSameReferenceAs(first);
        }
    }

    [Test]
    public async Task ConvertFromProvider_With_invalid_type_name_throws_on_every_attempt()
    {
        var converter = new TypeValueConverter();
        var fromProvider = converter.ConvertFromProvider;

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(() => fromProvider("Invalid.Repeated.Type, InvalidAssembly"))
                .Throws<InvalidOperationException>();
            _ = await Assert
                .That(() => fromProvider("Invalid.Repeated.Type, InvalidAssembly"))
                .Throws<InvalidOperationException>();
        }
    }
}
