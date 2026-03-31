namespace NetEvolve.Pulse.Tests.Unit.HttpCorrelation.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Http.Correlation.AspNetCore;
using NetEvolve.Http.Correlation.TestGenerator;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[SuppressMessage(
    "IDisposableAnalyzers.Correctness",
    "CA2000:Dispose objects before losing scope",
    Justification = "ServiceProvider instances are short-lived within test methods"
)]
public sealed class HttpCorrelationRequestInterceptorTests
{
    [Test]
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new HttpCorrelationRequestInterceptor<TestCommand, string>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NoAccessorRegistered_DoesNotThrow()
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();

        // Act
        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithAccessorRegistered_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator("test-id");
        var provider = services.BuildServiceProvider();

        // Act
        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);
        var request = new TestCommand();

        // Act & Assert
        _ = await Assert
            .That(async () => await interceptor.HandleAsync(request, null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoAccessorRegistered_PassesThroughWithoutModification()
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);
        var request = new TestCommand { CorrelationId = null };
        var handlerCalled = false;

        // Act
        var result = await interceptor
            .HandleAsync(
                request,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.FromResult("response");
                }
            )
            .ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(request.CorrelationId).IsNull();
        }
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_RequestCorrelationIdIsNull_SetsCorrelationId()
    {
        // Arrange
        const string expectedId = "correlation-abc";
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator(expectedId);
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);
        var request = new TestCommand { CorrelationId = null };

        // Act
        _ = await interceptor.HandleAsync(request, (_, _) => Task.FromResult("response")).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(request.CorrelationId).IsEqualTo(expectedId);
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_RequestCorrelationIdIsEmpty_SetsCorrelationId()
    {
        // Arrange
        const string expectedId = "correlation-abc";
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator(expectedId);
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);
        var request = new TestCommand { CorrelationId = string.Empty };

        // Act
        _ = await interceptor.HandleAsync(request, (_, _) => Task.FromResult("response")).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(request.CorrelationId).IsEqualTo(expectedId);
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_RequestAlreadyHasCorrelationId_DoesNotOverwrite()
    {
        // Arrange
        const string existingId = "existing-id";
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator("http-id");
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);
        var request = new TestCommand { CorrelationId = existingId };

        // Act
        _ = await interceptor.HandleAsync(request, (_, _) => Task.FromResult("response")).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(request.CorrelationId).IsEqualTo(existingId);
    }

    [Test]
    public async Task HandleAsync_AccessorCorrelationIdIsEmpty_DoesNotModifyRequest()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator(string.Empty);
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);
        var request = new TestCommand { CorrelationId = null };

        // Act
        _ = await interceptor.HandleAsync(request, (_, _) => Task.FromResult("response")).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(request.CorrelationId).IsNull();
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }
}
