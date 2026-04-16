namespace NetEvolve.Pulse.Tests.Unit.DataAnnotations.Interceptors;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("DataAnnotations")]
public sealed class DataAnnotationsStreamQueryInterceptorTests
{
    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsStreamQueryInterceptor<TestStreamQuery, string>();

        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(new TestStreamQuery("valid"), null!, cancellationToken)
                        .ConfigureAwait(false)
                )
                {
                    // consume
                }
            })
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoValidationAttributes_PassesThroughToHandler(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsStreamQueryInterceptor<NoAttributesStreamQuery, string>();
        var handlerCalled = false;

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(
                    new NoAttributesStreamQuery(),
                    (_, ct) =>
                    {
                        handlerCalled = true;
                        return YieldItemsAsync(["ok"], ct);
                    },
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(items).IsEquivalentTo(["ok"]);
        }
    }

    [Test]
    public async Task HandleAsync_ValidInput_PassesThroughToHandler(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsStreamQueryInterceptor<TestStreamQuery, string>();
        var handlerCalled = false;

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(
                    new TestStreamQuery("valid-name"),
                    (_, ct) =>
                    {
                        handlerCalled = true;
                        return YieldItemsAsync(["a", "b", "c"], ct);
                    },
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(items).IsEquivalentTo(["a", "b", "c"]);
        }
    }

    [Test]
    public async Task HandleAsync_RequiredPropertyMissing_ThrowsValidationException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsStreamQueryInterceptor<TestStreamQuery, string>();
        var handlerCalled = false;

        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(
                            new TestStreamQuery(null!),
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

    [Test]
    public async Task HandleAsync_RangeViolation_ThrowsValidationException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsStreamQueryInterceptor<RangeStreamQuery, string>();
        var handlerCalled = false;

        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(
                            new RangeStreamQuery { Age = 200 },
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

    [Test]
    public async Task HandleAsync_MaxLengthViolation_ThrowsValidationException(CancellationToken cancellationToken)
    {
        var interceptor = new DataAnnotationsStreamQueryInterceptor<MaxLengthStreamQuery, string>();
        var handlerCalled = false;

        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(
                            new MaxLengthStreamQuery { Name = new string('x', 101) },
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

    [Test]
    public async Task HandleAsync_MultipleViolations_ThrowsValidationExceptionWithAllErrors(
        CancellationToken cancellationToken
    )
    {
        var interceptor = new DataAnnotationsStreamQueryInterceptor<MultiConstraintStreamQuery, string>();

        var exception = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(
                            new MultiConstraintStreamQuery { Name = null!, Age = -1 },
                            (_, ct) => YieldItemsAsync(["should not reach"], ct),
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                )
                {
                    // consume — should throw before yielding
                }
            })
            .Throws<ValidationException>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsNotNull();
            _ = await Assert.That(exception!.ValidationResult.MemberNames.Count()).IsGreaterThanOrEqualTo(2);
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

    private sealed record TestStreamQuery(string Name) : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }

        [Required]
        public string Name { get; init; } = Name;
    }

    private sealed record NoAttributesStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }
        public string Value { get; init; } = string.Empty;
    }

    private sealed record RangeStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }

        [Range(0, 150)]
        public int Age { get; init; }
    }

    private sealed record MaxLengthStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }

        [MaxLength(100)]
        public string Name { get; init; } = string.Empty;
    }

    private sealed record MultiConstraintStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }

        [Required]
        public string Name { get; init; } = string.Empty;

        [Range(0, 150)]
        public int Age { get; init; }
    }
}
