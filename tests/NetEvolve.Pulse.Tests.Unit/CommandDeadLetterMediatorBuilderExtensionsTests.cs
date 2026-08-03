namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("CommandDeadLetter")]
public sealed class CommandDeadLetterMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddCommandDeadLetter_NullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => CommandDeadLetterMediatorBuilderExtensions.AddCommandDeadLetter(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddCommandDeadLetter_RegistersRequestInterceptorAsSingleton()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddCommandDeadLetter();

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(CommandDeadLetterInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddCommandDeadLetter_CalledMultipleTimes_DoesNotDuplicateInterceptor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddCommandDeadLetter();
        _ = builder.AddCommandDeadLetter();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(CommandDeadLetterInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddCommandDeadLetter_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddCommandDeadLetter();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(builder);
            _ = await Assert.That(result).IsTypeOf<IMediatorBuilder>();
        }
    }
}
