namespace NetEvolve.Pulse.Tests.Unit.Internals;

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

public class MediatorBuilderTests
{
    [Test]
    public async Task Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;

        _ = Assert.Throws<ArgumentNullException>(
            "services",
            () => new MediatorBuilder(services!).AddActivityAndMetrics()
        );
    }

    [Test]
    public async Task Constructor_WithValidServices_CreatesInstance()
    {
        var services = new ServiceCollection();

        var configurator = new MediatorBuilder(services);

        using (Assert.Multiple())
        {
            _ = await Assert.That(configurator).IsNotNull();
            _ = await Assert.That(configurator).IsTypeOf<MediatorBuilder>();
        }
    }

    [Test]
    public async Task Services_ReturnsProvidedServiceCollection()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.Services;

        _ = await Assert.That(result).IsSameReferenceAs(services);
    }
}
