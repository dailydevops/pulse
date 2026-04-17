namespace NetEvolve.Pulse.Tests.Unit.FluentValidation.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using global::FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("FluentValidation")]
public sealed class FluentValidationRequestInterceptorTests
{
    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(provider);

            // Act & Assert
            _ = await Assert
                .That(() => interceptor.HandleAsync(new TestCommand("valid"), null!, cancellationToken)!)
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task HandleAsync_NoValidatorsRegistered_PassesThroughToHandler(CancellationToken cancellationToken)
    {
        // Arrange — no IValidator<TestCommand> registered
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(provider);
            var handlerCalled = false;

            // Act
            var result = await interceptor
                .HandleAsync(
                    new TestCommand("input"),
                    (_, _) =>
                    {
                        handlerCalled = true;
                        return Task.FromResult("ok");
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            // Assert
            using (Assert.Multiple())
            {
                _ = await Assert.That(handlerCalled).IsTrue();
                _ = await Assert.That(result).IsEqualTo("ok");
            }
        }
    }

    [Test]
    public async Task HandleAsync_ValidInput_PassesThroughToHandler(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysValidValidator>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            await using var scope = provider.CreateAsyncScope();
            var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(scope.ServiceProvider);
            var handlerCalled = false;

            // Act
            var result = await interceptor
                .HandleAsync(
                    new TestCommand("valid"),
                    (_, _) =>
                    {
                        handlerCalled = true;
                        return Task.FromResult("success");
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            // Assert
            using (Assert.Multiple())
            {
                _ = await Assert.That(handlerCalled).IsTrue();
                _ = await Assert.That(result).IsEqualTo("success");
            }
        }
    }

    [Test]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysInvalidValidator>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
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
                        cancellationToken
                    )!
                )
                .Throws<ValidationException>();

            _ = await Assert.That(handlerCalled).IsFalse();
        }
    }

    [Test]
    public async Task HandleAsync_MultipleValidators_AggregatesAllFailures(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysInvalidValidator>();
        _ = services.AddScoped<IValidator<TestCommand>, AnotherInvalidValidator>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            await using var scope = provider.CreateAsyncScope();
            var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(scope.ServiceProvider);

            // Act
            var exception = await Assert
                .That(() =>
                    interceptor.HandleAsync(
                        new TestCommand("invalid"),
                        (_, _) => Task.FromResult("should not reach"),
                        cancellationToken
                    )!
                )
                .Throws<ValidationException>();

            // Assert — both validators contributed failures
            _ = await Assert.That(exception!.Errors.Count()).IsGreaterThanOrEqualTo(2);
        }
    }

    [Test]
    public async Task HandleAsync_MultipleValidatorsOneInvalid_ThrowsValidationException(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysValidValidator>();
        _ = services.AddScoped<IValidator<TestCommand>, AlwaysInvalidValidator>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            await using var scope = provider.CreateAsyncScope();
            var interceptor = new FluentValidationRequestInterceptor<TestCommand, string>(scope.ServiceProvider);

            // Act & Assert
            _ = await Assert
                .That(() =>
                    interceptor.HandleAsync(
                        new TestCommand("input"),
                        (_, _) => Task.FromResult("should not reach"),
                        cancellationToken
                    )!
                )
                .Throws<ValidationException>();
        }
    }

    private sealed record TestCommand(string Value) : ICommand<string>
    {
        public string? CausationId { get; set; }
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
