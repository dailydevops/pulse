namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Interceptors")]
public sealed class DataAnnotationsEventInterceptorTests
{
    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        // Arrange
        var interceptor = new DataAnnotationsEventInterceptor<TestEvent>();
        var testEvent = new TestEvent();

        // Act & Assert
        _ = await Assert
            .That(() => interceptor.HandleAsync(testEvent, null!, cancellationToken))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_ValidEvent_PassesThroughToHandler(CancellationToken cancellationToken)
    {
        // Arrange
        var interceptor = new DataAnnotationsEventInterceptor<ValidatedEvent>();
        var testEvent = new ValidatedEvent { Name = "valid-name" };
        var handlerCalled = false;

        // Act
        await interceptor
            .HandleAsync(
                testEvent,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(handlerCalled).IsTrue();
    }

    [Test]
    public async Task HandleAsync_InvalidEvent_ThrowsValidationException(CancellationToken cancellationToken)
    {
        // Arrange
        var interceptor = new DataAnnotationsEventInterceptor<ValidatedEvent>();
        var testEvent = new ValidatedEvent { Name = null };
        var handlerCalled = false;

        // Act & Assert
        _ = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    testEvent,
                    (_, _) =>
                    {
                        handlerCalled = true;
                        return Task.CompletedTask;
                    },
                    cancellationToken
                )
            )
            .Throws<ValidationException>();

        _ = await Assert.That(handlerCalled).IsFalse();
    }

    [Test]
    public async Task HandleAsync_EventWithNoValidationAttributes_PassesThroughToHandler(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var interceptor = new DataAnnotationsEventInterceptor<TestEvent>();
        var testEvent = new TestEvent();
        var handlerCalled = false;

        // Act
        await interceptor
            .HandleAsync(
                testEvent,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(handlerCalled).IsTrue();
    }

    /// <summary>
    /// A plain event with no validation attributes.
    /// </summary>
    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    /// <summary>
    /// An event with a required validation attribute on <see cref="Name"/>.
    /// </summary>
    private sealed class ValidatedEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }

        [Required]
        public string? Name { get; set; }
    }
}
