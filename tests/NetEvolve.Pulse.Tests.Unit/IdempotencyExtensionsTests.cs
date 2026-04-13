namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Idempotency")]
public sealed class IdempotencyExtensionsTests
{
    [Test]
    public async Task AddIdempotency_NullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => IdempotencyExtensions.AddIdempotency(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task AddIdempotency_RegistersRequestInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddIdempotency();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(IdempotencyCommandInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddIdempotency_CalledMultipleTimes_DoesNotDuplicateInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddIdempotency();
        _ = configurator.AddIdempotency();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(IdempotencyCommandInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddIdempotency_ReturnsSameConfigurator()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddIdempotency();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);
    }

    [Test]
    public async Task AddIdempotency_WithoutStoreRegistered_InterceptorResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        _ = services.AddPulse(configurator => configurator.AddIdempotency());

        var provider = services.BuildServiceProvider();

        var interceptors = provider.GetServices<IRequestInterceptor<TestCommand, string>>().ToList();

        _ = await Assert.That(interceptors).IsNotEmpty();
    }

    private sealed record TestCommand : IIdempotentCommand<string>
    {
        public string? CorrelationId { get; set; }
        public string IdempotencyKey { get; init; } = "test-key";
    }
}
