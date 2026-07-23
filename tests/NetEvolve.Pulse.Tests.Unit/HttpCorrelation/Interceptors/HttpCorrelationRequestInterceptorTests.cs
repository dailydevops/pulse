namespace NetEvolve.Pulse.Tests.Unit.HttpCorrelation.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Http.Correlation.Abstractions;
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
[TestGroup("HttpCorrelation")]
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
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator("test-id");
        var provider = services.BuildServiceProvider();

        // Act
        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);
        var request = new TestCommand();

        // Act & Assert
        _ = await Assert
            .That(async () => await interceptor.HandleAsync(request, null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoAccessorRegistered_PassesThroughWithoutModification(
        CancellationToken cancellationToken
    )
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
                },
                cancellationToken
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
    public async Task HandleAsync_AccessorHasCorrelationId_RequestAlreadyHasCorrelationId_DoesNotOverwrite(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        const string existingId = "existing-id";
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator("http-id");
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);
        var request = new TestCommand { CorrelationId = existingId };

        // Act
        _ = await interceptor
            .HandleAsync(request, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(request.CorrelationId).IsEqualTo(existingId);
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_RequestHasNoCorrelationId_SetsCorrelationId(
        CancellationToken cancellationToken
    )
    {
        // Arrange — verifies the core invariant: when accessor exposes a correlation id and the
        // request has none, the interceptor propagates the accessor value onto the request before
        // the handler is invoked.
        const string httpId = "http-correlation-id";
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(httpId);
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();

        // HttpCorrelationAccessor caches the correlation ID in a private field; set it via reflection
        // to simulate an incoming HTTP request that has already populated the correlation ID.
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpCorrelationAccessor>();
        var field = accessor.GetType().GetField("_correlationId", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(accessor, httpId);

        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(scope.ServiceProvider);
        var request = new TestCommand { CorrelationId = null };
        string? observedCorrelationId = null;

        // Act
        _ = await interceptor
            .HandleAsync(
                request,
                (req, _) =>
                {
                    observedCorrelationId = req.CorrelationId;
                    return Task.FromResult("response");
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(request.CorrelationId).IsEqualTo(httpId);
            _ = await Assert.That(observedCorrelationId).IsEqualTo(httpId);
        }
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_RequestHasEmptyCorrelationId_SetsCorrelationId(
        CancellationToken cancellationToken
    )
    {
        // Arrange — string.Empty is treated the same as null per IsNullOrEmpty contract.
        const string httpId = "http-correlation-id";
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(httpId);
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpCorrelationAccessor>();
        var field = accessor.GetType().GetField("_correlationId", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(accessor, httpId);

        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(scope.ServiceProvider);
        var request = new TestCommand { CorrelationId = string.Empty };

        // Act
        _ = await interceptor
            .HandleAsync(request, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(request.CorrelationId).IsEqualTo(httpId);
    }

    [Test]
    public async Task HandleAsync_AccessorCorrelationIdIsEmpty_DoesNotModifyRequest(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(string.Empty);
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationRequestInterceptor<TestCommand, string>(provider);
        var request = new TestCommand { CorrelationId = null };

        // Act
        _ = await interceptor
            .HandleAsync(request, (_, _) => Task.FromResult("response"), cancellationToken)
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(request.CorrelationId).IsNull();
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
