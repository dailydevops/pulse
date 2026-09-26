namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

[TestGroup("NativeAot")]
public sealed class NativeAotInterceptorExtensionsTests
{
    private const string Key = NativeAotInterceptorExtensions.ServiceKey;

    [Test]
    public void AddNativeAotCommandInterceptors_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;

        _ = Assert.Throws<ArgumentNullException>(
            "services",
            () => services!.AddNativeAotCommandInterceptors<ValueCommand, int>()
        );
    }

    [Test]
    public async Task AddNativeAotCommandInterceptors_WithDynamicCodeSupported_RegistersNothing()
    {
        var services = new ServiceCollection();
        _ = new MediatorBuilder(services).AddLogging();
        var count = services.Count;

        var result = services
            .AddNativeAotCommandInterceptors<ValueCommand, int>()
            .AddNativeAotExclusiveCommandInterceptors<ExclusiveValueCommand, int>()
            .AddNativeAotQueryInterceptors<ValueQuery, int>()
            .AddNativeAotStreamQueryInterceptors<RangeQuery, int>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(services);
            _ = await Assert.That(services.Count).IsEqualTo(count);
        }
    }

    [Test]
    public async Task AddCommandInterceptorsCore_WithBuiltInInterceptors_RegistersClosedKeyedInterceptorsInOrder()
    {
        var services = new ServiceCollection();
        _ = new MediatorBuilder(services).AddActivityAndMetrics().AddLogging();

        NativeAotInterceptorExtensions.AddCommandInterceptorsCore<ValueCommand, int>(services);

        var descriptors = KeyedDescriptors<IRequestInterceptor<ValueCommand, int>>(services);

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(descriptors.Select(d => d.KeyedImplementationType!))
                .IsEquivalentTo(
                    [
                        typeof(ActivityAndMetricsRequestInterceptor<ValueCommand, int>),
                        typeof(LoggingRequestInterceptor<ValueCommand, int>),
                    ],
                    TUnit.Assertions.Enums.CollectionOrdering.Matching
                );
            _ = await Assert.That(descriptors.All(d => d.Lifetime == ServiceLifetime.Singleton)).IsTrue();
        }
    }

    [Test]
    public async Task AddCommandInterceptorsCore_WithVoidResponse_ResolvesClosedKeyedInterceptors()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ = services.AddPulse(config => config.AddLogging());

        NativeAotInterceptorExtensions.AddCommandInterceptorsCore<VoidCommand, Extensibility.Void>(services);

        await using var provider = services.BuildServiceProvider();
        var interceptors = provider
            .GetKeyedServices<IRequestInterceptor<VoidCommand, Extensibility.Void>>(Key)
            .ToList();

        _ = await Assert.That(interceptors).HasSingleItem();
        _ = await Assert.That(interceptors[0]).IsTypeOf<LoggingRequestInterceptor<VoidCommand, Extensibility.Void>>();
    }

    [Test]
    public async Task AddCommandInterceptorsCore_WithUnknownOpenGenericInterceptor_RegistersNothing()
    {
        var services = new ServiceCollection();
        _ = new MediatorBuilder(services).AddLogging();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(OpenRecordingInterceptor<,>))
        );
        var count = services.Count;

        NativeAotInterceptorExtensions.AddCommandInterceptorsCore<ValueCommand, int>(services);

        _ = await Assert.That(services.Count).IsEqualTo(count);
    }

    [Test]
    public async Task AddCommandInterceptorsCore_CalledTwice_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        _ = new MediatorBuilder(services).AddLogging();

        NativeAotInterceptorExtensions.AddCommandInterceptorsCore<ValueCommand, int>(services);
        NativeAotInterceptorExtensions.AddCommandInterceptorsCore<ValueCommand, int>(services);

        _ = await Assert.That(KeyedDescriptors<IRequestInterceptor<ValueCommand, int>>(services)).HasSingleItem();
    }

    [Test]
    public async Task AddCommandInterceptorsCore_WithClosedInterceptors_CopiesThemInRegistrationOrder()
    {
        var instance = new ClosedRecordingInterceptor("instance");
        var services = new ServiceCollection();
        _ = services
            .AddSingleton<IRequestInterceptor<ValueCommand, int>>(instance)
            .AddSingleton<IRequestInterceptor<ValueCommand, int>>(_ => new ClosedRecordingInterceptor("factory"))
            .AddSingleton<IRequestInterceptor<ValueCommand, int>, DefaultRecordingInterceptor>();
        _ = new MediatorBuilder(services).AddLogging();
        _ = services
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        NativeAotInterceptorExtensions.AddCommandInterceptorsCore<ValueCommand, int>(services);

        await using var provider = services.BuildServiceProvider();
        var interceptors = provider.GetKeyedServices<IRequestInterceptor<ValueCommand, int>>(Key).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(interceptors.Count).IsEqualTo(4);
            _ = await Assert.That(interceptors[0]).IsSameReferenceAs(instance);
            _ = await Assert.That(((ClosedRecordingInterceptor)interceptors[1]).Name).IsEqualTo("factory");
            _ = await Assert.That(interceptors[2]).IsTypeOf<DefaultRecordingInterceptor>();
            _ = await Assert.That(interceptors[3]).IsTypeOf<LoggingRequestInterceptor<ValueCommand, int>>();
        }
    }

    [Test]
    public async Task AddCommandInterceptorsCore_WithQueryCachingAndConcurrentCommandGuard_SkipsThem()
    {
        var services = new ServiceCollection();
        _ = new MediatorBuilder(services).AddQueryCaching().AddConcurrentCommandGuard().AddLogging();

        NativeAotInterceptorExtensions.AddCommandInterceptorsCore<ValueCommand, int>(services);

        var descriptors = KeyedDescriptors<IRequestInterceptor<ValueCommand, int>>(services);

        _ = await Assert.That(descriptors).HasSingleItem();
        _ = await Assert
            .That(descriptors[0].KeyedImplementationType)
            .IsEqualTo(typeof(LoggingRequestInterceptor<ValueCommand, int>));
    }

    [Test]
    public async Task AddExclusiveCommandInterceptorsCore_WithConcurrentCommandGuard_RegistersClosedGuard()
    {
        var services = new ServiceCollection();
        _ = new MediatorBuilder(services).AddConcurrentCommandGuard().AddQueryCaching();

        NativeAotInterceptorExtensions.AddExclusiveCommandInterceptorsCore<ExclusiveValueCommand, int>(services);

        var descriptors = KeyedDescriptors<IRequestInterceptor<ExclusiveValueCommand, int>>(services);

        _ = await Assert.That(descriptors).HasSingleItem();
        _ = await Assert
            .That(descriptors[0].KeyedImplementationType)
            .IsEqualTo(typeof(ConcurrentCommandGuardInterceptor<ExclusiveValueCommand, int>));
    }

    [Test]
    public async Task AddQueryInterceptorsCore_WithQueryCachingAndConcurrentCommandGuard_RegistersClosedQueryCaching()
    {
        var services = new ServiceCollection();
        _ = new MediatorBuilder(services).AddConcurrentCommandGuard().AddQueryCaching();

        NativeAotInterceptorExtensions.AddQueryInterceptorsCore<ValueQuery, int>(services);

        var descriptors = KeyedDescriptors<IRequestInterceptor<ValueQuery, int>>(services);

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors).HasSingleItem();
            _ = await Assert
                .That(descriptors[0].KeyedImplementationType)
                .IsEqualTo(typeof(DistributedCacheQueryInterceptor<ValueQuery, int>));
            _ = await Assert.That(descriptors[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddStreamQueryInterceptorsCore_WithBuiltInInterceptors_RegistersClosedKeyedInterceptors()
    {
        var services = new ServiceCollection();
        _ = new MediatorBuilder(services).AddLogging().AddActivityAndMetrics();

        NativeAotInterceptorExtensions.AddStreamQueryInterceptorsCore<RangeQuery, int>(services);

        var descriptors = KeyedDescriptors<IStreamQueryInterceptor<RangeQuery, int>>(services);

        _ = await Assert
            .That(descriptors.Select(d => d.KeyedImplementationType!))
            .IsEquivalentTo(
                [
                    typeof(LoggingStreamQueryInterceptor<RangeQuery, int>),
                    typeof(ActivityAndMetricsStreamQueryInterceptor<RangeQuery, int>),
                ],
                TUnit.Assertions.Enums.CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task SendAsync_WithValueTypeResponseAndKeyedInterceptors_UsesKeyedInterceptors()
    {
        var recorder = new Recorder();
        var services = CreateServices(recorder);
        _ = services.AddSingleton<IRequestInterceptor<ValueCommand, int>>(
            new ClosedRecordingInterceptor("keyed", recorder)
        );
        NativeAotInterceptorExtensions.AddCommandInterceptorsCore<ValueCommand, int>(services);
        _ = services.AddSingleton<IRequestInterceptor<ValueCommand, int>>(
            new ClosedRecordingInterceptor("unkeyed", recorder)
        );

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync<ValueCommand, int>(new ValueCommand(), CancellationToken.None);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo(42);
            _ = await Assert.That(recorder.Names).IsEquivalentTo(["keyed"]);
        }
    }

    [Test]
    public async Task SendAsync_WithValueTypeResponseAndNoKeyedInterceptors_UsesUnkeyedInterceptors()
    {
        var recorder = new Recorder();
        var services = CreateServices(recorder);
        _ = services.AddSingleton<IRequestInterceptor<ValueCommand, int>>(
            new ClosedRecordingInterceptor("unkeyed", recorder)
        );

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        _ = await scope
            .ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync<ValueCommand, int>(new ValueCommand(), CancellationToken.None);

        _ = await Assert.That(recorder.Names).IsEquivalentTo(["unkeyed"]);
    }

    [Test]
    public async Task SendAsync_WithReferenceTypeResponse_IgnoresKeyedInterceptors()
    {
        var recorder = new Recorder();
        var services = CreateServices(recorder);
        _ = services.AddSingleton<IRequestInterceptor<ReferenceCommand, string>>(
            new ReferenceRecordingInterceptor("keyed", recorder)
        );
        NativeAotInterceptorExtensions.AddCommandInterceptorsCore<ReferenceCommand, string>(services);
        _ = services.AddSingleton<IRequestInterceptor<ReferenceCommand, string>>(
            new ReferenceRecordingInterceptor("unkeyed", recorder)
        );

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        _ = await scope
            .ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync<ReferenceCommand, string>(new ReferenceCommand(), CancellationToken.None);

        _ = await Assert
            .That(recorder.Names)
            .IsEquivalentTo(["keyed", "unkeyed"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task StreamQueryAsync_WithValueTypeItemsAndKeyedInterceptors_UsesKeyedInterceptors()
    {
        var recorder = new Recorder();
        var services = CreateServices(recorder);
        _ = services.AddSingleton<IStreamQueryInterceptor<RangeQuery, int>>(
            new StreamRecordingInterceptor("keyed", recorder)
        );
        NativeAotInterceptorExtensions.AddStreamQueryInterceptorsCore<RangeQuery, int>(services);
        _ = services.AddSingleton<IStreamQueryInterceptor<RangeQuery, int>>(
            new StreamRecordingInterceptor("unkeyed", recorder)
        );

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var items = new List<int>();
        await foreach (
            var item in scope
                .ServiceProvider.GetRequiredService<IMediator>()
                .StreamQueryAsync<RangeQuery, int>(new RangeQuery(), CancellationToken.None)
        )
        {
            items.Add(item);
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(items).IsEquivalentTo([1, 2]);
            _ = await Assert.That(recorder.Names).IsEquivalentTo(["keyed"]);
        }
    }

    private static List<ServiceDescriptor> KeyedDescriptors<TService>(IServiceCollection services) =>
        [.. services.Where(d => d.IsKeyedService && Equals(d.ServiceKey, Key) && d.ServiceType == typeof(TService))];

    private static ServiceCollection CreateServices(Recorder recorder)
    {
        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton(recorder)
            .AddPulse()
            .AddScoped<ICommandHandler<ValueCommand, int>, ValueCommandHandler>()
            .AddScoped<ICommandHandler<ReferenceCommand, string>, ReferenceCommandHandler>()
            .AddScoped<IStreamQueryHandler<RangeQuery, int>, RangeQueryHandler>();
        return services;
    }

    private sealed class Recorder
    {
        public ConcurrentQueue<string> Names { get; } = new();
    }

    private sealed record ValueCommand : ICommand<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record VoidCommand : ICommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record ReferenceCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record ExclusiveValueCommand : IExclusiveCommand<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record ValueQuery : IQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record RangeQuery : IStreamQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class ValueCommandHandler : ICommandHandler<ValueCommand, int>
    {
        public Task<int> HandleAsync(ValueCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(42);
    }

    private sealed class ReferenceCommandHandler : ICommandHandler<ReferenceCommand, string>
    {
        public Task<string> HandleAsync(ReferenceCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult("done");
    }

    private sealed class RangeQueryHandler : IStreamQueryHandler<RangeQuery, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            RangeQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            yield return 1;
            yield return 2;
        }
    }

    private sealed class ClosedRecordingInterceptor(string name, Recorder? recorder = null)
        : IRequestInterceptor<ValueCommand, int>
    {
        public string Name { get; } = name;

        public Task<int> HandleAsync(
            ValueCommand request,
            Func<ValueCommand, CancellationToken, Task<int>> handler,
            CancellationToken cancellationToken = default
        )
        {
            recorder?.Names.Enqueue(Name);
            return handler(request, cancellationToken);
        }
    }

    private sealed class ReferenceRecordingInterceptor(string name, Recorder recorder)
        : IRequestInterceptor<ReferenceCommand, string>
    {
        public Task<string> HandleAsync(
            ReferenceCommand request,
            Func<ReferenceCommand, CancellationToken, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            recorder.Names.Enqueue(name);
            return handler(request, cancellationToken);
        }
    }

    private sealed class StreamRecordingInterceptor(string name, Recorder recorder)
        : IStreamQueryInterceptor<RangeQuery, int>
    {
        public IAsyncEnumerable<int> HandleAsync(
            RangeQuery request,
            Func<RangeQuery, CancellationToken, IAsyncEnumerable<int>> handler,
            CancellationToken cancellationToken = default
        )
        {
            recorder.Names.Enqueue(name);
            return handler(request, cancellationToken);
        }
    }

    private sealed class DefaultRecordingInterceptor : IRequestInterceptor<ValueCommand, int>
    {
        public Task<int> HandleAsync(
            ValueCommand request,
            Func<ValueCommand, CancellationToken, Task<int>> handler,
            CancellationToken cancellationToken = default
        ) => handler(request, cancellationToken);
    }

    private sealed class OpenRecordingInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> HandleAsync(
            TRequest request,
            Func<TRequest, CancellationToken, Task<TResponse>> handler,
            CancellationToken cancellationToken = default
        ) => handler(request, cancellationToken);
    }
}
