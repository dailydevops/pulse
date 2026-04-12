namespace NetEvolve.Pulse.Tests.Unit.FluentValidation;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("FluentValidation")]
public sealed class FluentValidationExtensionsTests
{
    [Test]
    public async Task AddFluentValidation_NullConfigurator_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    ) =>
        _ = await Assert
            .That(() => FluentValidationExtensions.AddFluentValidation(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddFluentValidation_RegistersRequestInterceptor(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddFluentValidation());

        var provider = services.BuildServiceProvider();

        // Assert
        var requestInterceptors = provider.GetServices<IRequestInterceptor<TestCommand, string>>().ToList();
        _ = await Assert.That(requestInterceptors).IsNotEmpty();
    }

    [Test]
    public async Task AddFluentValidation_CalledMultipleTimes_DoesNotDuplicateRequestInterceptor(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddFluentValidation().AddFluentValidation());

        // Assert — TryAddEnumerable should prevent duplicates for open-generic registrations
        var requestDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType?.GetGenericTypeDefinition() == typeof(FluentValidationRequestInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(requestDescriptors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddFluentValidation_ReturnsSameConfigurator(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        IMediatorBuilder? result = null;
        _ = services.AddPulse(configurator => result = configurator.AddFluentValidation());

        // Assert
        _ = await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task AddFluentValidation_RegistersInterceptorWithScopedLifetime(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddFluentValidation());

        // Assert
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(FluentValidationRequestInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }
}
