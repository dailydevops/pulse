namespace NetEvolve.Pulse.Tests.Unit.FluentValidation.Interceptors;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
public sealed class FluentValidationStreamQueryInterceptorTests
{
    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new FluentValidationStreamQueryInterceptor<TestStreamQuery, string>(provider);

            // Act & Assert — ArgumentNullException is thrown immediately (before enumeration begins)
            _ = await Assert
                .That(() => interceptor.HandleAsync(new TestStreamQuery("valid"), null!, cancellationToken))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task HandleAsync_NoValidatorsRegistered_PassesThroughAllItems(CancellationToken cancellationToken)
    {
        // Arrange — no IValidator<TestStreamQuery> registered
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new FluentValidationStreamQueryInterceptor<TestStreamQuery, string>(provider);
            var expected = new[] { "a", "b", "c" };

            // Act
            var items = new List<string>();
            await foreach (
                var item in interceptor
                    .HandleAsync(
                        new TestStreamQuery("input"),
                        (_, ct) => YieldItemsAsync(expected, ct),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                items.Add(item);
            }

            // Assert
            _ = await Assert.That(items).IsEquivalentTo(expected);
        }
    }

    [Test]
    public async Task HandleAsync_ValidInput_YieldsAllItems(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestStreamQuery>, AlwaysValidValidator>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            await using var scope = provider.CreateAsyncScope();
            var interceptor = new FluentValidationStreamQueryInterceptor<TestStreamQuery, string>(
                scope.ServiceProvider
            );
            var expected = new[] { "x", "y" };

            // Act
            var items = new List<string>();
            await foreach (
                var item in interceptor
                    .HandleAsync(
                        new TestStreamQuery("valid"),
                        (_, ct) => YieldItemsAsync(expected, ct),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                items.Add(item);
            }

            // Assert
            _ = await Assert.That(items).IsEquivalentTo(expected);
        }
    }

    [Test]
    public async Task HandleAsync_InvalidInput_ThrowsValidationExceptionBeforeYieldingItems(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestStreamQuery>, AlwaysInvalidValidator>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            await using var scope = provider.CreateAsyncScope();
            var interceptor = new FluentValidationStreamQueryInterceptor<TestStreamQuery, string>(
                scope.ServiceProvider
            );
            var handlerCalled = false;

            // Act & Assert
            _ = await Assert
                .That(async () =>
                {
                    await foreach (
                        var _ in interceptor
                            .HandleAsync(
                                new TestStreamQuery("invalid"),
                                (_, ct) =>
                                {
                                    handlerCalled = true;
                                    return YieldItemsAsync(["should not reach"], ct);
                                },
                                cancellationToken
                            )
                            .ConfigureAwait(false)
                    )
                    {
                        // consume — should throw before yielding
                    }
                })
                .Throws<ValidationException>();

            _ = await Assert.That(handlerCalled).IsFalse();
        }
    }

    [Test]
    public async Task HandleAsync_MultipleValidators_AggregatesAllFailures(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddScoped<IValidator<TestStreamQuery>, AlwaysInvalidValidator>();
        _ = services.AddScoped<IValidator<TestStreamQuery>, AnotherInvalidValidator>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            await using var scope = provider.CreateAsyncScope();
            var interceptor = new FluentValidationStreamQueryInterceptor<TestStreamQuery, string>(
                scope.ServiceProvider
            );

            // Act
            var exception = await Assert
                .That(async () =>
                {
                    await foreach (
                        var _ in interceptor
                            .HandleAsync(
                                new TestStreamQuery("invalid"),
                                (_, ct) => YieldItemsAsync(["should not reach"], ct),
                                cancellationToken
                            )
                            .ConfigureAwait(false)
                    )
                    {
                        // consume
                    }
                })
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
        _ = services.AddScoped<IValidator<TestStreamQuery>, AlwaysValidValidator>();
        _ = services.AddScoped<IValidator<TestStreamQuery>, AlwaysInvalidValidator>();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            await using var scope = provider.CreateAsyncScope();
            var interceptor = new FluentValidationStreamQueryInterceptor<TestStreamQuery, string>(
                scope.ServiceProvider
            );

            // Act & Assert
            _ = await Assert
                .That(async () =>
                {
                    await foreach (
                        var _ in interceptor
                            .HandleAsync(
                                new TestStreamQuery("input"),
                                (_, ct) => YieldItemsAsync(["should not reach"], ct),
                                cancellationToken
                            )
                            .ConfigureAwait(false)
                    )
                    {
                        // consume
                    }
                })
                .Throws<ValidationException>();
        }
    }

    private static async IAsyncEnumerable<string> YieldItemsAsync(
        IEnumerable<string> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return item;
        }
    }

    private sealed record TestStreamQuery(string Value) : IStreamQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class AlwaysValidValidator : AbstractValidator<TestStreamQuery>
    {
        [SuppressMessage(
            "Major Code Smell",
            "S1144:Unused private types or members should be removed",
            Justification = "Validator is used by FluentValidation interceptor."
        )]
        public AlwaysValidValidator() => RuleFor(x => x.Value).NotEmpty();
    }

    private sealed class AlwaysInvalidValidator : AbstractValidator<TestStreamQuery>
    {
        [SuppressMessage(
            "Major Code Smell",
            "S1144:Unused private types or members should be removed",
            Justification = "Validator is used by FluentValidation interceptor."
        )]
        public AlwaysInvalidValidator() =>
            RuleFor(x => x.Value).Must(_ => false).WithMessage("AlwaysInvalid: validation failed.");
    }

    private sealed class AnotherInvalidValidator : AbstractValidator<TestStreamQuery>
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
