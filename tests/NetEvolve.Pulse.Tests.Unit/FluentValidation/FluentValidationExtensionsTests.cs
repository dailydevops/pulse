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
    public async Task AddFluentValidation_NullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => FluentValidationExtensions.AddFluentValidation(null!))
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
        IMediatorBuilder? result = null;
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

    [Test]
    public async Task AddFluentValidation_RegistersStreamQueryInterceptor()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddFluentValidation());

        var provider = services.BuildServiceProvider();

        // Assert
        var streamInterceptors = provider.GetServices<IStreamQueryInterceptor<TestStreamQuery, string>>().ToList();
        _ = await Assert.That(streamInterceptors).IsNotEmpty();
    }

    [Test]
    public async Task AddFluentValidation_CalledMultipleTimes_DoesNotDuplicateStreamQueryInterceptor()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddFluentValidation().AddFluentValidation());

        // Assert — TryAddEnumerable should prevent duplicates for open-generic registrations
        var streamDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IStreamQueryInterceptor<,>)
                && d.ImplementationType?.GetGenericTypeDefinition() == typeof(FluentValidationStreamQueryInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(streamDescriptors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddFluentValidation_RegistersStreamQueryInterceptorWithScopedLifetime()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddFluentValidation());

        // Assert
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IStreamQueryInterceptor<,>)
            && d.ImplementationType == typeof(FluentValidationStreamQueryInterceptor<,>)
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

    private sealed record TestStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }
    }
}
