namespace NetEvolve.Pulse.Tests.Unit;

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

public class HandlerRegistrationExtensionsTests
{
    [Test]
    public void AddCommandHandler_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(() =>
            configurator!.AddCommandHandler<TestCommand, string, TestCommandHandler>()
        );
    }

    [Test]
    public async Task AddCommandHandler_RegistersHandlerWithScopedLifetime()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config => config.AddCommandHandler<TestCommand, string, TestCommandHandler>());

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(ICommandHandler<TestCommand, string>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(TestCommandHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddCommandHandler_WithExplicitLifetime_RegistersHandlerWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config =>
            config.AddCommandHandler<TestCommand, string, TestCommandHandler>(ServiceLifetime.Singleton)
        );

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(ICommandHandler<TestCommand, string>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(TestCommandHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddCommandHandler_ReturnsConfigurator()
    {
        var services = new ServiceCollection();
        IMediatorConfigurator? capturedConfig = null;
        IMediatorConfigurator? result = null;

        _ = services.AddPulse(config =>
        {
            capturedConfig = config;
            result = config.AddCommandHandler<TestCommand, string, TestCommandHandler>();
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedConfig).IsNotNull();
            _ = await Assert.That(result).IsSameReferenceAs(capturedConfig);
        }
    }

    [Test]
    public void AddCommandHandler_VoidCommand_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(() =>
            configurator!.AddCommandHandler<TestVoidCommand, TestVoidCommandHandler>()
        );
    }

    [Test]
    public async Task AddCommandHandler_VoidCommand_RegistersHandlerWithScopedLifetime()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config => config.AddCommandHandler<TestVoidCommand, TestVoidCommandHandler>());

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(ICommandHandler<TestVoidCommand, Void>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(TestVoidCommandHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddCommandHandler_VoidCommand_WithExplicitLifetime_RegistersHandlerWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config =>
            config.AddCommandHandler<TestVoidCommand, TestVoidCommandHandler>(ServiceLifetime.Transient)
        );

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(ICommandHandler<TestVoidCommand, Void>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(TestVoidCommandHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Transient);
        }
    }

    [Test]
    public async Task AddCommandHandler_VoidCommand_ReturnsConfigurator()
    {
        var services = new ServiceCollection();
        IMediatorConfigurator? capturedConfig = null;
        IMediatorConfigurator? result = null;

        _ = services.AddPulse(config =>
        {
            capturedConfig = config;
            result = config.AddCommandHandler<TestVoidCommand, TestVoidCommandHandler>();
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedConfig).IsNotNull();
            _ = await Assert.That(result).IsSameReferenceAs(capturedConfig);
        }
    }

    [Test]
    public void AddQueryHandler_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(() =>
            configurator!.AddQueryHandler<TestQuery, string, TestQueryHandler>()
        );
    }

    [Test]
    public async Task AddQueryHandler_RegistersHandlerWithScopedLifetime()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config => config.AddQueryHandler<TestQuery, string, TestQueryHandler>());

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IQueryHandler<TestQuery, string>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(TestQueryHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddQueryHandler_WithTransientLifetime_RegistersHandlerWithTransientLifetime()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config =>
            config.AddQueryHandler<TestQuery, string, TestQueryHandler>(ServiceLifetime.Transient)
        );

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IQueryHandler<TestQuery, string>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(TestQueryHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Transient);
        }
    }

    [Test]
    public async Task AddQueryHandler_ReturnsConfigurator()
    {
        var services = new ServiceCollection();
        IMediatorConfigurator? capturedConfig = null;
        IMediatorConfigurator? result = null;

        _ = services.AddPulse(config =>
        {
            capturedConfig = config;
            result = config.AddQueryHandler<TestQuery, string, TestQueryHandler>();
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedConfig).IsNotNull();
            _ = await Assert.That(result).IsSameReferenceAs(capturedConfig);
        }
    }

    [Test]
    public void AddEventHandler_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(() => configurator!.AddEventHandler<TestEvent, TestEventHandler>());
    }

    [Test]
    public async Task AddEventHandler_RegistersHandlerWithScopedLifetime()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config => config.AddEventHandler<TestEvent, TestEventHandler>());

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IEventHandler<TestEvent>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(TestEventHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddEventHandler_WithSingletonLifetime_RegistersHandlerWithSingletonLifetime()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config => config.AddEventHandler<TestEvent, TestEventHandler>(ServiceLifetime.Singleton));

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IEventHandler<TestEvent>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(TestEventHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddEventHandler_AllowsMultipleHandlersForSameEvent()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config =>
            config.AddEventHandler<TestEvent, TestEventHandler>().AddEventHandler<TestEvent, AnotherTestEventHandler>()
        );

        var descriptors = services.Where(x => x.ServiceType == typeof(IEventHandler<TestEvent>)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors.Count).IsEqualTo(2);
            _ = await Assert.That(descriptors[0].ImplementationType).IsEqualTo(typeof(TestEventHandler));
            _ = await Assert.That(descriptors[1].ImplementationType).IsEqualTo(typeof(AnotherTestEventHandler));
        }
    }

    [Test]
    public async Task AddEventHandler_ReturnsConfigurator()
    {
        var services = new ServiceCollection();
        IMediatorConfigurator? capturedConfig = null;
        IMediatorConfigurator? result = null;

        _ = services.AddPulse(config =>
        {
            capturedConfig = config;
            result = config.AddEventHandler<TestEvent, TestEventHandler>();
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedConfig).IsNotNull();
            _ = await Assert.That(result).IsSameReferenceAs(capturedConfig);
        }
    }

    [Test]
    public async Task HandlerRegistrationExtensions_SupportMethodChaining()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config =>
            config
                .AddCommandHandler<TestCommand, string, TestCommandHandler>()
                .AddQueryHandler<TestQuery, string, TestQueryHandler>()
                .AddEventHandler<TestEvent, TestEventHandler>()
        );

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(ICommandHandler<TestCommand, string>)))
                .IsTrue();
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(IQueryHandler<TestQuery, string>)))
                .IsTrue();
            _ = await Assert.That(services.Any(x => x.ServiceType == typeof(IEventHandler<TestEvent>))).IsTrue();
        }
    }

    // Test helper types
    private sealed partial record TestCommand(string Value) : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed partial class TestCommandHandler : ICommandHandler<TestCommand, string>
    {
        public Task<string> HandleAsync(TestCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(command.Value);
    }

    private sealed partial record TestVoidCommand(string Value) : ICommand<Void>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed partial class TestVoidCommandHandler : ICommandHandler<TestVoidCommand, Void>
    {
        public Task<Void> HandleAsync(TestVoidCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(Void.Completed);
    }

    private sealed partial record TestQuery(string Value) : IQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed partial class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public Task<string> HandleAsync(TestQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Value);
    }

    private sealed partial record TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed partial class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed partial class AnotherTestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
