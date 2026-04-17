namespace NetEvolve.Pulse.Tests.Unit.DataAnnotations.Interceptors;

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

[TestGroup("DataAnnotations")]
public sealed class DataAnnotationsEventInterceptorTests
{
    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsEventInterceptor<TestEvent>();
        var testEvent = new TestEvent();

        _ = await Assert
            .That(() => interceptor.HandleAsync(testEvent, null!, cancellationToken))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoValidationAttributes_PassesThroughToHandler(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsEventInterceptor<TestEvent>();
        var testEvent = new TestEvent();
        var handlerCalled = false;

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

        _ = await Assert.That(handlerCalled).IsTrue();
    }

    [Test]
    public async Task HandleAsync_ValidInput_PassesThroughToHandler(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsEventInterceptor<ValidatedEvent>();
        var handlerCalled = false;

        await interceptor
            .HandleAsync(
                new ValidatedEvent { Name = "valid-name" },
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(handlerCalled).IsTrue();
    }

    [Test]
    public async Task HandleAsync_RequiredPropertyMissing_ThrowsValidationException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsEventInterceptor<ValidatedEvent>();
        var handlerCalled = false;

        _ = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    new ValidatedEvent { Name = null! },
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
    public async Task HandleAsync_MultipleViolations_ThrowsValidationExceptionWithAllErrors(
        CancellationToken cancellationToken
    )
    {
        var interceptor = new DataAnnotationsEventInterceptor<MultiConstraintEvent>();

        var exception = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    new MultiConstraintEvent { Name = null!, Age = -1 },
                    (_, _) => Task.CompletedTask,
                    cancellationToken
                )
            )
            .Throws<ValidationException>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsNotNull();
            _ = await Assert.That(exception!.ValidationResult.MemberNames.Count()).IsGreaterThanOrEqualTo(2);
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class ValidatedEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class MultiConstraintEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }

        [Required]
        public string Name { get; init; } = string.Empty;

        [Range(0, 150)]
        public int Age { get; init; }
    }
}
