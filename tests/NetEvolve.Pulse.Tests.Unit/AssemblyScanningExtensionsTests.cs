namespace NetEvolve.Pulse.Tests.Unit;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

public class AssemblyScanningExtensionsTests
{
    [Test]
    public void AddHandlersFromAssembly_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = Assert.Throws<ArgumentNullException>(() => configurator!.AddHandlersFromAssembly(assembly));
    }

    [Test]
    public void AddHandlersFromAssembly_WithNullAssembly_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assembly? assembly = null;

        _ = Assert.Throws<ArgumentNullException>(() =>
            services.AddPulse(config => config.AddHandlersFromAssembly(assembly!))
        );
    }

    [Test]
    public async Task AddHandlersFromAssembly_RegistersCommandHandlers()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddHandlersFromAssembly(assembly));

        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(ICommandHandler<ScanTestCommand, string>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(ScanTestCommandHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddHandlersFromAssembly_RegistersQueryHandlers()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddHandlersFromAssembly(assembly));

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IQueryHandler<ScanTestQuery, string>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(ScanTestQueryHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddHandlersFromAssembly_RegistersEventHandlers()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddHandlersFromAssembly(assembly));

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IEventHandler<ScanTestEvent>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(ScanTestEventHandler));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddHandlersFromAssembly_WithCustomLifetime_RegistersHandlersWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddHandlersFromAssembly(assembly, ServiceLifetime.Singleton));

        var commandDescriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(ICommandHandler<ScanTestCommand, string>)
        );
        var queryDescriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IQueryHandler<ScanTestQuery, string>)
        );
        var eventDescriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IEventHandler<ScanTestEvent>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(commandDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
            _ = await Assert.That(queryDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
            _ = await Assert.That(eventDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddHandlersFromAssembly_DoesNotRegisterAbstractClasses()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddHandlersFromAssembly(assembly));

        var descriptor = services.FirstOrDefault(x => x.ImplementationType == typeof(AbstractCommandHandler));

        _ = await Assert.That(descriptor).IsNull();
    }

    [Test]
    public async Task AddHandlersFromAssembly_ReturnsConfigurator()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;
        IMediatorConfigurator? capturedConfig = null;
        IMediatorConfigurator? result = null;

        _ = services.AddPulse(config =>
        {
            capturedConfig = config;
            result = config.AddHandlersFromAssembly(assembly);
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedConfig).IsNotNull();
            _ = await Assert.That(result).IsSameReferenceAs(capturedConfig);
        }
    }

    [Test]
    public void AddHandlersFromAssemblyContaining_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(() =>
            configurator!.AddHandlersFromAssemblyContaining<ScanTestCommandHandler>()
        );
    }

    [Test]
    public async Task AddHandlersFromAssemblyContaining_RegistersHandlersFromMarkerTypeAssembly()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config => config.AddHandlersFromAssemblyContaining<ScanTestCommandHandler>());

        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(ICommandHandler<ScanTestCommand, string>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(ScanTestCommandHandler));
        }
    }

    [Test]
    public async Task AddHandlersFromAssemblyContaining_WithCustomLifetime_RegistersHandlersWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config =>
            config.AddHandlersFromAssemblyContaining<ScanTestCommandHandler>(ServiceLifetime.Transient)
        );

        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(ICommandHandler<ScanTestCommand, string>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Transient);
        }
    }

    [Test]
    public async Task AddHandlersFromAssemblyContaining_ReturnsConfigurator()
    {
        var services = new ServiceCollection();
        IMediatorConfigurator? capturedConfig = null;
        IMediatorConfigurator? result = null;

        _ = services.AddPulse(config =>
        {
            capturedConfig = config;
            result = config.AddHandlersFromAssemblyContaining<ScanTestCommandHandler>();
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedConfig).IsNotNull();
            _ = await Assert.That(result).IsSameReferenceAs(capturedConfig);
        }
    }

    [Test]
    public void AddHandlersFromAssemblies_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;
        var assemblies = new[] { typeof(AssemblyScanningExtensionsTests).Assembly };

        _ = Assert.Throws<ArgumentNullException>(() => configurator!.AddHandlersFromAssemblies(assemblies));
    }

    [Test]
    public void AddHandlersFromAssemblies_WithNullAssemblies_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assembly[]? assemblies = null;

        _ = Assert.Throws<ArgumentNullException>(() =>
            services.AddPulse(config => config.AddHandlersFromAssemblies(assemblies!))
        );
    }

    [Test]
    public async Task AddHandlersFromAssemblies_RegistersHandlersFromAllAssemblies()
    {
        var services = new ServiceCollection();
        var assemblies = new[]
        {
            typeof(AssemblyScanningExtensionsTests).Assembly,
            typeof(IMediator).Assembly, // NetEvolve.Pulse assembly
        };

        _ = services.AddPulse(config => config.AddHandlersFromAssemblies(assemblies));

        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(ICommandHandler<ScanTestCommand, string>)
        );

        _ = await Assert.That(descriptor).IsNotNull();
    }

    [Test]
    public async Task AddHandlersFromAssemblies_WithCustomLifetime_RegistersHandlersWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();
        var assemblies = new[] { typeof(AssemblyScanningExtensionsTests).Assembly };

        _ = services.AddPulse(config => config.AddHandlersFromAssemblies(assemblies, ServiceLifetime.Transient));

        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(ICommandHandler<ScanTestCommand, string>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Transient);
        }
    }

    [Test]
    public async Task AddHandlersFromAssemblies_ReturnsConfigurator()
    {
        var services = new ServiceCollection();
        var assemblies = new[] { typeof(AssemblyScanningExtensionsTests).Assembly };
        IMediatorConfigurator? capturedConfig = null;
        IMediatorConfigurator? result = null;

        _ = services.AddPulse(config =>
        {
            capturedConfig = config;
            result = config.AddHandlersFromAssemblies(assemblies);
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedConfig).IsNotNull();
            _ = await Assert.That(result).IsSameReferenceAs(capturedConfig);
        }
    }

    // Test helper types for assembly scanning
    // Must be public for assembly scanning tests
    private sealed partial record ScanTestCommand(string Value) : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed partial class ScanTestCommandHandler : ICommandHandler<ScanTestCommand, string>
    {
        public Task<string> HandleAsync(ScanTestCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(command.Value);
    }

    private sealed partial record ScanTestQuery(string Value) : IQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed partial class ScanTestQueryHandler : IQueryHandler<ScanTestQuery, string>
    {
        public Task<string> HandleAsync(ScanTestQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Value);
    }

    private sealed partial record ScanTestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed partial class ScanTestEventHandler : IEventHandler<ScanTestEvent>
    {
        public Task HandleAsync(ScanTestEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    [SuppressMessage(
        "Minor Code Smell",
        "S1694:An abstract class should have both abstract and concrete methods",
        Justification = "As designed."
    )]
    private abstract class AbstractCommandHandler : ICommandHandler<ScanTestCommand, string>
    {
        public abstract Task<string> HandleAsync(
            ScanTestCommand command,
            CancellationToken cancellationToken = default
        );
    }

    // Interceptor scanning tests
    [Test]
    public void AddInterceptorsFromAssembly_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = Assert.Throws<ArgumentNullException>(() => configurator!.AddInterceptorsFromAssembly(assembly));
    }

    [Test]
    public void AddInterceptorsFromAssembly_WithNullAssembly_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assembly? assembly = null;

        _ = Assert.Throws<ArgumentNullException>(() =>
            services.AddPulse(config => config.AddInterceptorsFromAssembly(assembly!))
        );
    }

    [Test]
    public async Task AddInterceptorsFromAssembly_RegistersRequestInterceptors()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddInterceptorsFromAssembly(assembly));

        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IRequestInterceptor<ScanTestCommand, string>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(ScanTestRequestInterceptor));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddInterceptorsFromAssembly_RegistersCommandInterceptors()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddInterceptorsFromAssembly(assembly));

        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(ICommandInterceptor<ScanTestCommand, string>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(ScanTestCommandInterceptor));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddInterceptorsFromAssembly_RegistersQueryInterceptors()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddInterceptorsFromAssembly(assembly));

        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IQueryInterceptor<ScanTestQuery, string>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(ScanTestQueryInterceptor));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddInterceptorsFromAssembly_RegistersEventInterceptors()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddInterceptorsFromAssembly(assembly));

        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IEventInterceptor<ScanTestEvent>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(ScanTestEventInterceptor));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddInterceptorsFromAssembly_WithCustomLifetime_RegistersInterceptorsWithSpecifiedLifetime()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;

        _ = services.AddPulse(config => config.AddInterceptorsFromAssembly(assembly, ServiceLifetime.Singleton));

        var requestDescriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IRequestInterceptor<ScanTestCommand, string>)
        );
        var commandDescriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(ICommandInterceptor<ScanTestCommand, string>)
        );
        var queryDescriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IQueryInterceptor<ScanTestQuery, string>)
        );
        var eventDescriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IEventInterceptor<ScanTestEvent>));

        using (Assert.Multiple())
        {
            _ = await Assert.That(requestDescriptor).IsNotNull();
            _ = await Assert.That(requestDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
            _ = await Assert.That(commandDescriptor).IsNotNull();
            _ = await Assert.That(commandDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
            _ = await Assert.That(queryDescriptor).IsNotNull();
            _ = await Assert.That(queryDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
            _ = await Assert.That(eventDescriptor).IsNotNull();
            _ = await Assert.That(eventDescriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddInterceptorsFromAssembly_ReturnsConfigurator()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AssemblyScanningExtensionsTests).Assembly;
        IMediatorConfigurator? capturedConfig = null;
        IMediatorConfigurator? result = null;

        _ = services.AddPulse(config =>
        {
            capturedConfig = config;
            result = config.AddInterceptorsFromAssembly(assembly);
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedConfig).IsNotNull();
            _ = await Assert.That(result).IsSameReferenceAs(capturedConfig);
        }
    }

    [Test]
    public void AddInterceptorsFromAssemblies_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;
        var assemblies = new[] { typeof(AssemblyScanningExtensionsTests).Assembly };

        _ = Assert.Throws<ArgumentNullException>(() => configurator!.AddInterceptorsFromAssemblies(assemblies));
    }

    [Test]
    public void AddInterceptorsFromAssemblies_WithNullAssemblies_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assembly[]? assemblies = null;

        _ = Assert.Throws<ArgumentNullException>(() =>
            services.AddPulse(config => config.AddInterceptorsFromAssemblies(assemblies!))
        );
    }

    [Test]
    public async Task AddInterceptorsFromAssemblies_RegistersInterceptorsFromAllAssemblies()
    {
        var services = new ServiceCollection();
        var assemblies = new[] { typeof(AssemblyScanningExtensionsTests).Assembly };

        _ = services.AddPulse(config => config.AddInterceptorsFromAssemblies(assemblies));

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(IRequestInterceptor<ScanTestCommand, string>)))
                .IsTrue();
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(ICommandInterceptor<ScanTestCommand, string>)))
                .IsTrue();
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(IQueryInterceptor<ScanTestQuery, string>)))
                .IsTrue();
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(IEventInterceptor<ScanTestEvent>)))
                .IsTrue();
        }
    }

    [Test]
    public async Task AddInterceptorsFromAssemblies_ReturnsConfigurator()
    {
        var services = new ServiceCollection();
        var assemblies = new[] { typeof(AssemblyScanningExtensionsTests).Assembly };
        IMediatorConfigurator? capturedConfig = null;
        IMediatorConfigurator? result = null;

        _ = services.AddPulse(config =>
        {
            capturedConfig = config;
            result = config.AddInterceptorsFromAssemblies(assemblies);
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedConfig).IsNotNull();
            _ = await Assert.That(result).IsSameReferenceAs(capturedConfig);
        }
    }

    [Test]
    public async Task AddInterceptorsFromAssemblyContaining_RegistersInterceptors()
    {
        var services = new ServiceCollection();

        _ = services.AddPulse(config => config.AddInterceptorsFromAssemblyContaining<ScanTestCommandInterceptor>());

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(IRequestInterceptor<ScanTestCommand, string>)))
                .IsTrue();
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(ICommandInterceptor<ScanTestCommand, string>)))
                .IsTrue();
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(IQueryInterceptor<ScanTestQuery, string>)))
                .IsTrue();
            _ = await Assert
                .That(services.Any(x => x.ServiceType == typeof(IEventInterceptor<ScanTestEvent>)))
                .IsTrue();
        }
    }

    [Test]
    public void AddInterceptorsFromAssemblyContaining_WithNullConfigurator_ThrowsArgumentNullException()
    {
        IMediatorConfigurator? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(() =>
            configurator!.AddInterceptorsFromAssemblyContaining<ScanTestCommandInterceptor>()
        );
    }

    // Test helper interceptor types for assembly scanning
    private sealed partial class ScanTestRequestInterceptor : IRequestInterceptor<ScanTestCommand, string>
    {
        public Task<string> HandleAsync(
            ScanTestCommand request,
            Func<ScanTestCommand, Task<string>> handler,
            CancellationToken cancellationToken = default
        ) => handler(request);
    }

    private sealed partial class ScanTestCommandInterceptor : ICommandInterceptor<ScanTestCommand, string>
    {
        public Task<string> HandleAsync(
            ScanTestCommand request,
            Func<ScanTestCommand, Task<string>> handler,
            CancellationToken cancellationToken = default
        ) => handler(request);
    }

    private sealed partial class ScanTestQueryInterceptor : IQueryInterceptor<ScanTestQuery, string>
    {
        public Task<string> HandleAsync(
            ScanTestQuery request,
            Func<ScanTestQuery, Task<string>> handler,
            CancellationToken cancellationToken = default
        ) => handler(request);
    }

    private sealed partial class ScanTestEventInterceptor : IEventInterceptor<ScanTestEvent>
    {
        public Task HandleAsync(
            ScanTestEvent message,
            Func<ScanTestEvent, Task> handler,
            CancellationToken cancellationToken = default
        ) => handler(message);
    }
}
