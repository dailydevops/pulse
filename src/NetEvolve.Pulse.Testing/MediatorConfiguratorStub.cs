namespace NetEvolve.Pulse.Testing;

using System;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Caching;

/// <summary>
/// A minimal stub implementation of <see cref="IMediatorConfigurator"/> for argument-validation tests.
/// All configurator methods throw <see cref="NotImplementedException"/>; only <see cref="Services"/>
/// is backed by a real <see cref="ServiceCollection"/>.
/// </summary>
public sealed class MediatorConfiguratorStub : IMediatorConfigurator
{
    /// <inheritdoc />
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <inheritdoc />
    public IMediatorConfigurator AddActivityAndMetrics() => throw new NotImplementedException();

    /// <inheritdoc />
    public IMediatorConfigurator AddQueryCaching(Action<QueryCachingOptions>? configure = null) =>
        throw new NotImplementedException();

    /// <inheritdoc />
    public IMediatorConfigurator UseDefaultEventDispatcher<TDispatcher>(
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();

    /// <inheritdoc />
    public IMediatorConfigurator UseDefaultEventDispatcher<TDispatcher>(
        Func<IServiceProvider, TDispatcher> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();

    /// <inheritdoc />
    public IMediatorConfigurator UseEventDispatcherFor<TEvent, TDispatcher>(
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TEvent : IEvent
        where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();

    /// <inheritdoc />
    public IMediatorConfigurator UseEventDispatcherFor<TEvent, TDispatcher>(
        Func<IServiceProvider, TDispatcher> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TEvent : IEvent
        where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();
}
