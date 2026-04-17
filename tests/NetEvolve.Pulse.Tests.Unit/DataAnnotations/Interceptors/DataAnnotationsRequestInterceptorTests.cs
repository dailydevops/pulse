namespace NetEvolve.Pulse.Tests.Unit.DataAnnotations.Interceptors;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("DataAnnotations")]
public sealed class DataAnnotationsRequestInterceptorTests
{
    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsRequestInterceptor<TestCommand, string>();

        _ = await Assert
            .That(() => interceptor.HandleAsync(new TestCommand("valid"), null!, cancellationToken)!)
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoValidationAttributes_PassesThroughToHandler(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsRequestInterceptor<NoAttributesCommand, string>();
        var handlerCalled = false;

        var result = await interceptor
            .HandleAsync(
                new NoAttributesCommand(),
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.FromResult("ok");
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(result).IsEqualTo("ok");
        }
    }

    [Test]
    public async Task HandleAsync_ValidInput_PassesThroughToHandler(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsRequestInterceptor<TestCommand, string>();
        var handlerCalled = false;

        var result = await interceptor
            .HandleAsync(
                new TestCommand("valid-name"),
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.FromResult("success");
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(result).IsEqualTo("success");
        }
    }

    [Test]
    public async Task HandleAsync_RequiredPropertyMissing_ThrowsValidationException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsRequestInterceptor<TestCommand, string>();
        var handlerCalled = false;

        _ = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    new TestCommand(null!),
                    (_, _) =>
                    {
                        handlerCalled = true;
                        return Task.FromResult("should not reach");
                    },
                    cancellationToken
                )!
            )
            .Throws<ValidationException>();

        _ = await Assert.That(handlerCalled).IsFalse();
    }

    [Test]
    public async Task HandleAsync_RangeViolation_ThrowsValidationException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsRequestInterceptor<RangeCommand, string>();
        var handlerCalled = false;

        _ = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    new RangeCommand { Age = 200 },
                    (_, _) =>
                    {
                        handlerCalled = true;
                        return Task.FromResult("should not reach");
                    },
                    cancellationToken
                )!
            )
            .Throws<ValidationException>();

        _ = await Assert.That(handlerCalled).IsFalse();
    }

    [Test]
    public async Task HandleAsync_MaxLengthViolation_ThrowsValidationException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsRequestInterceptor<MaxLengthCommand, string>();
        var handlerCalled = false;

        _ = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    new MaxLengthCommand { Name = new string('x', 101) },
                    (_, _) =>
                    {
                        handlerCalled = true;
                        return Task.FromResult("should not reach");
                    },
                    cancellationToken
                )!
            )
            .Throws<ValidationException>();

        _ = await Assert.That(handlerCalled).IsFalse();
    }

    [Test]
    public async Task HandleAsync_MultipleViolations_ThrowsValidationExceptionWithAllErrors(
        CancellationToken cancellationToken
    )
    {
        var interceptor = new DataAnnotationsRequestInterceptor<MultiConstraintCommand, string>();

        var exception = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    new MultiConstraintCommand { Name = null!, Age = -1 },
                    (_, _) => Task.FromResult("should not reach"),
                    cancellationToken
                )!
            )
            .Throws<ValidationException>();

        // Assert — all violations are captured in the composite ValidationResult
        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsNotNull();
            _ = await Assert.That(exception!.ValidationResult.MemberNames.Count()).IsGreaterThanOrEqualTo(2);
        }
    }

    private sealed record TestCommand(string Name) : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        [Required]
        public string Name { get; init; } = Name;
    }

    private sealed record NoAttributesCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Value { get; init; } = string.Empty;
    }

    private sealed record RangeCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        [Range(0, 150)]
        public int Age { get; init; }
    }

    private sealed record MaxLengthCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        [MaxLength(100)]
        public string Name { get; init; } = string.Empty;
    }

    private sealed record MultiConstraintCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        [Required]
        public string Name { get; init; } = string.Empty;

        [Range(0, 150)]
        public int Age { get; init; }
    }
}
