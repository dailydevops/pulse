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
    public async Task Constructor_Creates_instance(CancellationToken cancellationToken)
    {
        var converter = new TypeValueConverter();

        _ = await Assert.That(converter).IsNotNull();
    }

    [Test]
    public async Task ConvertToProvider_With_valid_type_returns_assembly_qualified_name(
        CancellationToken cancellationToken
    )
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
    public async Task ConvertFromProvider_With_valid_type_name_returns_type(CancellationToken cancellationToken)
    {
        var converter = new TypeValueConverter();
        var fromProvider = converter.ConvertFromProvider;

        var result = fromProvider(typeof(string).AssemblyQualifiedName) as Type;

        _ = await Assert.That(result).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task ConvertFromProvider_With_invalid_type_name_throws_InvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        var converter = new TypeValueConverter();
        var fromProvider = converter.ConvertFromProvider;

        _ = await Assert
            .That(() => fromProvider("Invalid.Type.Name, InvalidAssembly"))
            .Throws<InvalidOperationException>();
    }
}
