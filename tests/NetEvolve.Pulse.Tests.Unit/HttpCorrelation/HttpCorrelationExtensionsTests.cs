namespace NetEvolve.Pulse.Tests.Unit.HttpCorrelation;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("HttpCorrelation")]
public sealed class HttpCorrelationExtensionsTests
{
    [Test]
    public async Task AddHttpCorrelationEnrichment_NullConfigurator_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    ) =>
        _ = await Assert
            .That(() => HttpCorrelationExtensions.AddHttpCorrelationEnrichment(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddHttpCorrelationEnrichment_RegistersRequestInterceptor(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        var provider = services.BuildServiceProvider();

        // Assert
        var requestInterceptors = provider.GetServices<IRequestInterceptor<TestCommand, string>>().ToList();
        _ = await Assert.That(requestInterceptors).IsNotEmpty();
    }

    [Test]
    public async Task AddHttpCorrelationEnrichment_RegistersEventInterceptor(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        var provider = services.BuildServiceProvider();

        // Assert
        var eventInterceptors = provider.GetServices<IEventInterceptor<TestEvent>>().ToList();
        _ = await Assert.That(eventInterceptors).IsNotEmpty();
    }

    [Test]
    public async Task AddHttpCorrelationEnrichment_CalledMultipleTimes_DoesNotDuplicateRequestInterceptor(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator.AddHttpCorrelationEnrichment().AddHttpCorrelationEnrichment()
        );

        // Assert — TryAddEnumerable should prevent duplicates for open-generic registrations
        var requestDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType?.GetGenericTypeDefinition()
                    == typeof(Pulse.Interceptors.HttpCorrelationRequestInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(requestDescriptors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddHttpCorrelationEnrichment_CalledMultipleTimes_DoesNotDuplicateEventInterceptor(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator.AddHttpCorrelationEnrichment().AddHttpCorrelationEnrichment()
        );

        // Assert — TryAddEnumerable should prevent duplicates for open-generic registrations
        var eventDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IEventInterceptor<>)
                && d.ImplementationType?.GetGenericTypeDefinition()
                    == typeof(Pulse.Interceptors.HttpCorrelationEventInterceptor<>)
            )
            .ToList();

        _ = await Assert.That(eventDescriptors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddHttpCorrelationEnrichment_ReturnsSameConfigurator(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        IMediatorBuilder? result = null;
        _ = services.AddPulse(configurator => result = configurator.AddHttpCorrelationEnrichment());

        // Assert
        _ = await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task AddHttpCorrelationEnrichment_WithoutAccessorRegistered_InterceptorResolvesSuccessfully(
        CancellationToken cancellationToken
    )
    {
        // Arrange — IHttpCorrelationAccessor is intentionally NOT registered
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        _ = services.AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        var provider = services.BuildServiceProvider();

        // Act — resolving the interceptor should NOT throw even without IHttpCorrelationAccessor
        var interceptors = provider.GetServices<IRequestInterceptor<TestCommand, string>>().ToList();

        // Assert
        _ = await Assert.That(interceptors).IsNotEmpty();
    }

    [Test]
    public async Task AddHttpCorrelationEnrichment_RegistersStreamQueryInterceptor(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator => configurator.AddHttpCorrelationEnrichment());

        var provider = services.BuildServiceProvider();

        // Assert
        var streamQueryInterceptors = provider.GetServices<IStreamQueryInterceptor<TestStreamQuery, string>>().ToList();
        _ = await Assert.That(streamQueryInterceptors).IsNotEmpty();
    }

    [Test]
    public async Task AddHttpCorrelationEnrichment_CalledMultipleTimes_DoesNotDuplicateStreamQueryInterceptor(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddPulse(configurator =>
            configurator.AddHttpCorrelationEnrichment().AddHttpCorrelationEnrichment()
        );

        // Assert — TryAddEnumerable should prevent duplicates for open-generic registrations
        var streamQueryDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IStreamQueryInterceptor<,>)
                && d.ImplementationType?.GetGenericTypeDefinition()
                    == typeof(Pulse.Interceptors.HttpCorrelationStreamQueryInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(streamQueryDescriptors.Count).IsEqualTo(1);
    }

    private sealed record TestStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
