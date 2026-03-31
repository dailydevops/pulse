namespace NetEvolve.Pulse.Tests.Unit.FluentValidation.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using global::FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class FluentValidationRequestInterceptorTests
{
    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
        var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(provider);

        // Act & Assert
        _ = await Assert
            .That(() => interceptor.HandleAsync(new TestCommand("valid"), null!, CancellationToken.None)!)
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoValidatorsRegistered_PassesThroughToHandler()
    {
        // Arrange — no IValidator<TestCommand> registered
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
        var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(provider);
        var handlerCalled = false;

        // Act
        var result = await interceptor.HandleAsync(
            new TestCommand("input"),
            (_, _) =>
            {
                handlerCalled = true;
                return Task.FromResult("ok");
            },
            CancellationToken.None
        );

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(result).IsEqualTo("ok");
        }
    }

    [Test]
    public async Task HandleAsync_ValidInput_PassesThroughToHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysValidValidator>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(scope.ServiceProvider);
        var handlerCalled = false;

        // Act
        var result = await interceptor.HandleAsync(
            new TestCommand("valid"),
            (_, _) =>
            {
                handlerCalled = true;
                return Task.FromResult("success");
            },
            CancellationToken.None
        );

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(result).IsEqualTo("success");
        }
    }

    [Test]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysInvalidValidator>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(scope.ServiceProvider);
        var handlerCalled = false;

        // Act & Assert
        _ = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    new TestCommand("invalid"),
                    (_, _) =>
                    {
                        handlerCalled = true;
                        return Task.FromResult("should not reach");
                    },
                    CancellationToken.None
                )!
            )
            .Throws<ValidationException>();

        _ = await Assert.That(handlerCalled).IsFalse();
    }

    [Test]
    public async Task HandleAsync_MultipleValidators_AggregatesAllFailures()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysInvalidValidator>();
        _ = services.AddScoped<IValidator<TestCommand>, AnotherInvalidValidator>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(scope.ServiceProvider);

        // Act
        var exception = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    new TestCommand("invalid"),
                    (_, _) => Task.FromResult("should not reach"),
                    CancellationToken.None
                )!
            )
            .Throws<ValidationException>();

        // Assert — both validators contributed failures
        _ = await Assert.That(exception!.Errors.Count()).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task HandleAsync_MultipleValidatorsOneInvalid_ThrowsValidationException()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysValidValidator>();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysInvalidValidator>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(scope.ServiceProvider);

        // Act & Assert
        _ = await Assert
            .That(() =>
                interceptor.HandleAsync(
                    new TestCommand("input"),
                    (_, _) => Task.FromResult("should not reach"),
                    CancellationToken.None
                )!
            )
            .Throws<ValidationException>();
    }

    private sealed record TestCommand(string Value) : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class AlwaysValidValidator : AbstractValidator<TestCommand>
    {
        [SuppressMessage(
            "Major Code Smell",
            "S1144:Unused private types or members should be removed",
            Justification = "Validator is used by FluentValidation interceptor."
        )]
        public AlwaysValidValidator() => RuleFor(x => x.Value).NotEmpty();
    }

    private sealed class AlwaysInvalidValidator : AbstractValidator<TestCommand>
    {
        [SuppressMessage(
            "Major Code Smell",
            "S1144:Unused private types or members should be removed",
            Justification = "Validator is used by FluentValidation interceptor."
        )]
        public AlwaysInvalidValidator() =>
            RuleFor(x => x.Value).Must(_ => false).WithMessage("AlwaysInvalid: validation failed.");
    }

    private sealed class AnotherInvalidValidator : AbstractValidator<TestCommand>
    {
        [SuppressMessage(
            "Major Code Smell",
            "S1144:Unused private types or members should be removed",
            Justification = "Validator is used by FluentValidation interceptor."
        )]
        public AnotherInvalidValidator() =>
            RuleFor(x => x.Value).Must(_ => false).WithMessage("AnotherInvalid: another validation failed.");
    }
}
