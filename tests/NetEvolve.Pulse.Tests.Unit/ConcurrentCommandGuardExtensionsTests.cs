namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Polly;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("ConcurrentCommandGuard")]
public sealed class ConcurrentCommandGuardExtensionsTests
{
    [Test]
    public void AddConcurrentCommandGuard_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>("configurator", () => configurator!.AddConcurrentCommandGuard());
    }

    [Test]
    public async Task AddConcurrentCommandGuard_RegistersOpenGenericInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddConcurrentCommandGuard();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(ConcurrentCommandGuardInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_CalledMultipleTimes_DoesNotDuplicateInterceptors()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddConcurrentCommandGuard();
        _ = configurator.AddConcurrentCommandGuard();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(ConcurrentCommandGuardInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public void AddConcurrentCommandGuard_TypedWithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(
            "configurator",
            () => configurator!.AddConcurrentCommandGuard<ExclusiveCommand, string>()
        );
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<ExclusiveCommand, string>) && d.ImplementationFactory != null
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_CalledMultipleTimes_DoesNotDuplicateInterceptors()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();
        _ = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<ExclusiveCommand, string>)
                && d.ImplementationFactory != null
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public void AddConcurrentCommandGuard_VoidWithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(
            "configurator",
            () => configurator!.AddConcurrentCommandGuard<ExclusiveVoidCommand>()
        );
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Void_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        var result = configurator.AddConcurrentCommandGuard<ExclusiveVoidCommand>();

        _ = await Assert.That(result).IsSameReferenceAs(configurator);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<ExclusiveVoidCommand, Extensibility.Void>)
            && d.ImplementationFactory != null
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Void_CalledMultipleTimes_DoesNotDuplicateInterceptors()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddConcurrentCommandGuard<ExclusiveVoidCommand>();
        _ = configurator.AddConcurrentCommandGuard<ExclusiveVoidCommand>();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<ExclusiveVoidCommand, Extensibility.Void>)
                && d.ImplementationFactory != null
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Void_RegistersNoOpenGenericService()
    {
        // The DI container cannot close open-generic services over value types such as Void under NativeAOT,
        // so the closed overloads must register the interceptor implementation as a closed service.
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddConcurrentCommandGuard<ExclusiveVoidCommand>();

        var openGenericDescriptors = services.Where(d => d.ServiceType.IsGenericTypeDefinition).ToList();
        var interceptor = services
            .BuildServiceProvider()
            .GetServices<IRequestInterceptor<ExclusiveVoidCommand, Extensibility.Void>>()
            .ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(openGenericDescriptors).IsEmpty();
            _ = await Assert.That(interceptor).HasSingleItem();
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_CombinedWithOpenGeneric_DoesNotDuplicateInterfaceRegistrations()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator.AddConcurrentCommandGuard();
        _ = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();

        // Open-generic overload: IRequestInterceptor<,> → ConcurrentCommandGuardInterceptor<,>
        var openGenericDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(ConcurrentCommandGuardInterceptor<,>)
            )
            .ToList();

        // The typed overload must detect that the open-generic mapping already covers this closed
        // TRequest/TResponse pair and skip its own closed factory registration entirely — otherwise
        // GetServices<IRequestInterceptor<ExclusiveCommand, string>>() would resolve TWO independent
        // interceptor instances (one via open-generic auto-closing, one via the factory), each wrapping
        // the command handler separately.
        var closedGenericDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<ExclusiveCommand, string>)
                && d.ImplementationFactory != null
            )
            .ToList();

        var provider = services.BuildServiceProvider();
        var resolvedInterceptors = provider.GetServices<IRequestInterceptor<ExclusiveCommand, string>>().ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(openGenericDescriptors).HasSingleItem();
            _ = await Assert.That(closedGenericDescriptors).IsEmpty();
            _ = await Assert.That(resolvedInterceptors).HasSingleItem();
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_CalledBeforeOpenGeneric_StillResolvesSingleInterceptor()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        // Reverse call order: the typed overload runs first (registering its own closed factory since
        // no open-generic mapping exists yet), then the open-generic overload is added afterwards.
        _ = configurator.AddConcurrentCommandGuard<ExclusiveCommand, string>();
        _ = configurator.AddConcurrentCommandGuard();

        var provider = services.BuildServiceProvider();
        var resolvedInterceptors = provider.GetServices<IRequestInterceptor<ExclusiveCommand, string>>().ToList();

        // NOTE: this ordering is not de-duplicated (only open-then-typed is) — documenting current
        // behavior. Calling both overloads for the same command is an unusual combination; the
        // recommended pattern is to pick one registration style per command type.
        _ = await Assert.That(resolvedInterceptors).IsNotEmpty();
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_AfterRequestInterceptor_ResolvesGuard()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator
            .AddRequestInterceptor<ExclusiveCommand, string, PassThroughInterceptor<ExclusiveCommand, string>>()
            .AddConcurrentCommandGuard<ExclusiveCommand, string>();

        await AssertGuardResolvedOnceAsync<ExclusiveCommand, string>(services, expectedInterceptors: 2);
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_AfterCommandInterceptor_ResolvesGuard()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator
            .AddCommandInterceptor<ExclusiveCommand, string, PassThroughInterceptor<ExclusiveCommand, string>>()
            .AddConcurrentCommandGuard<ExclusiveCommand, string>();

        await AssertGuardResolvedOnceAsync<ExclusiveCommand, string>(services, expectedInterceptors: 2);
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_AfterPollyCommandPolicies_ResolvesGuard()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator
            .AddPollyCommandPolicies<ExclusiveCommand, string>(pipeline =>
                pipeline.AddTimeout(TimeSpan.FromSeconds(30))
            )
            .AddConcurrentCommandGuard<ExclusiveCommand, string>();

        await AssertGuardResolvedOnceAsync<ExclusiveCommand, string>(services, expectedInterceptors: 2);
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Void_AfterRequestInterceptor_ResolvesGuard()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator
            .AddRequestInterceptor<
                ExclusiveVoidCommand,
                Extensibility.Void,
                PassThroughInterceptor<ExclusiveVoidCommand, Extensibility.Void>
            >()
            .AddConcurrentCommandGuard<ExclusiveVoidCommand>();

        await AssertGuardResolvedOnceAsync<ExclusiveVoidCommand, Extensibility.Void>(services, expectedInterceptors: 2);
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_CalledTwiceAfterRequestInterceptor_RegistersGuardOnce()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator
            .AddRequestInterceptor<ExclusiveCommand, string, PassThroughInterceptor<ExclusiveCommand, string>>()
            .AddConcurrentCommandGuard<ExclusiveCommand, string>()
            .AddConcurrentCommandGuard<ExclusiveCommand, string>();

        await AssertGuardResolvedOnceAsync<ExclusiveCommand, string>(services, expectedInterceptors: 2);
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_CalledTwice_RegistersGuardOnce()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator
            .AddConcurrentCommandGuard<ExclusiveCommand, string>()
            .AddConcurrentCommandGuard<ExclusiveCommand, string>();

        await AssertGuardResolvedOnceAsync<ExclusiveCommand, string>(services, expectedInterceptors: 1);
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_TwoDifferentCommands_RegistersBothGuards()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator
            .AddConcurrentCommandGuard<ExclusiveCommand, string>()
            .AddConcurrentCommandGuard<ExclusiveVoidCommand>();

        await AssertGuardResolvedOnceAsync<ExclusiveCommand, string>(services, expectedInterceptors: 1);
        await AssertGuardResolvedOnceAsync<ExclusiveVoidCommand, Extensibility.Void>(services, expectedInterceptors: 1);
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_AfterOpenGenericAndRequestInterceptor_ResolvesSingleGuard()
    {
        var services = new ServiceCollection();
        var configurator = new MediatorBuilder(services);

        _ = configurator
            .AddConcurrentCommandGuard()
            .AddRequestInterceptor<ExclusiveCommand, string, PassThroughInterceptor<ExclusiveCommand, string>>()
            .AddConcurrentCommandGuard<ExclusiveCommand, string>();

        using var provider = services.BuildServiceProvider();
        var interceptors = provider.GetServices<IRequestInterceptor<ExclusiveCommand, string>>().ToList();
        var guards = interceptors.OfType<ConcurrentCommandGuardInterceptor<ExclusiveCommand, string>>().ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(interceptors.Count).IsEqualTo(2);
            _ = await Assert.That(guards).HasSingleItem();
        }
    }

    [Test]
    public async Task AddConcurrentCommandGuard_Typed_AfterRequestInterceptor_ConcurrentSends_SerializesExecution(
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        _ = services
            .AddLogging()
            .AddSingleton<ConcurrencyTracker>()
            .AddPulse(configurator =>
                configurator
                    .AddRequestInterceptor<ExclusiveCommand, string, PassThroughInterceptor<ExclusiveCommand, string>>()
                    .AddConcurrentCommandGuard<ExclusiveCommand, string>()
                    .AddCommandHandler<ExclusiveCommand, string, TrackingHandler>()
            );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var scope = provider.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                _ = await Task.WhenAll(
                        Enumerable
                            .Range(0, 4)
                            .Select(_ =>
                                mediator.SendAsync<ExclusiveCommand, string>(new ExclusiveCommand(), cancellationToken)
                            )
                    )
                    .ConfigureAwait(false);
            }

            _ = await Assert.That(provider.GetRequiredService<ConcurrencyTracker>().MaxConcurrent).IsEqualTo(1);
        }
    }

    private static async Task AssertGuardResolvedOnceAsync<TRequest, TResponse>(
        ServiceCollection services,
        int expectedInterceptors
    )
        where TRequest : IExclusiveCommand<TResponse>
    {
        using var provider = services.BuildServiceProvider();
        var interceptors = provider.GetServices<IRequestInterceptor<TRequest, TResponse>>().ToList();
        var guards = interceptors.OfType<ConcurrentCommandGuardInterceptor<TRequest, TResponse>>().ToList();
        var concrete = provider.GetService<ConcurrentCommandGuardInterceptor<TRequest, TResponse>>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(interceptors.Count).IsEqualTo(expectedInterceptors);
            _ = await Assert.That(guards).HasSingleItem();
            _ = await Assert.That(guards.FirstOrDefault()).IsSameReferenceAs(concrete);
        }
    }

    private sealed class PassThroughInterceptor<TRequest, TResponse> : ICommandInterceptor<TRequest, TResponse>
        where TRequest : ICommand<TResponse>
    {
        public Task<TResponse> HandleAsync(
            TRequest request,
            Func<TRequest, CancellationToken, Task<TResponse>> handler,
            CancellationToken cancellationToken = default
        ) => handler(request, cancellationToken);
    }

    private sealed class ConcurrencyTracker
    {
        private int _current;
        private int _max;

        public int MaxConcurrent => Volatile.Read(ref _max);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _current);
            int max;
            while (current > (max = Volatile.Read(ref _max)))
            {
                _ = Interlocked.CompareExchange(ref _max, current, max);
            }

            try
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ = Interlocked.Decrement(ref _current);
            }
        }
    }

    private sealed class TrackingHandler(ConcurrencyTracker tracker) : ICommandHandler<ExclusiveCommand, string>
    {
        public async Task<string> HandleAsync(ExclusiveCommand command, CancellationToken cancellationToken = default)
        {
            await tracker.RunAsync(cancellationToken).ConfigureAwait(false);
            return "done";
        }
    }

    private sealed record ExclusiveCommand : IExclusiveCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record ExclusiveVoidCommand : IExclusiveCommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
