namespace NetEvolve.Pulse.FluentValidation.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class FluentValidationMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task AddFluentValidation_NullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => FluentValidationMediatorConfiguratorExtensions.AddFluentValidation(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddFluentValidation_RegistersRequestInterceptor()
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
    public async Task AddFluentValidation_CalledMultipleTimes_DoesNotDuplicateRequestInterceptor()
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
    public async Task AddFluentValidation_ReturnsSameConfigurator()
    {
        // Arrange
        var services = new ServiceCollection();
        IMediatorConfigurator? result = null;
        _ = services.AddPulse(configurator => result = configurator.AddFluentValidation());

        // Assert
        _ = await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task AddFluentValidation_RegistersInterceptorWithScopedLifetime()
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
