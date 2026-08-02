namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="OutboxExtensions.AddOutbox"/>.
/// Tests service registration including the open-generic <see cref="OutboxEventHandler{TEvent}"/>.
/// </summary>
[TestGroup("Outbox")]
public sealed class OutboxExtensionsTests
{
    [Test]
    public async Task AddOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => OutboxExtensions.AddOutbox(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task AddOutbox_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddOutbox();

        _ = await Assert.That(result).IsSameReferenceAs(builder);
    }

    [Test]
    public async Task AddOutbox_RegistersOutboxEventHandlerAsOpenGenericScoped()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddOutbox();

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventHandler<>) && d.ImplementationType == typeof(OutboxEventHandler<>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddOutbox_CalledMultipleTimes_DoesNotDuplicateOutboxEventHandler()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddOutbox();
        _ = builder.AddOutbox();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IEventHandler<>) && d.ImplementationType == typeof(OutboxEventHandler<>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddOutbox_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddOutbox();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseMessageTransportGeneric_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => OutboxExtensions.UseMessageTransport<FakeMessageTransport>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseMessageTransportGeneric_WithNoExistingRegistration_AddsTransport()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.UseMessageTransport<FakeMessageTransport>();

        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageTransport)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors).HasSingleItem();
            _ = await Assert.That(descriptors[0].ImplementationType).IsEqualTo(typeof(FakeMessageTransport));
            _ = await Assert.That(descriptors[0].Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseMessageTransportGeneric_WithExistingRegistration_ReplacesTransport()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);
        _ = builder.AddOutbox();

        _ = builder.UseMessageTransport<FakeMessageTransport>();

        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageTransport)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors).HasSingleItem();
            _ = await Assert.That(descriptors[0].ImplementationType).IsEqualTo(typeof(FakeMessageTransport));
        }
    }

    [Test]
    public async Task UseMessageTransportGeneric_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.UseMessageTransport<FakeMessageTransport>();

        _ = await Assert.That(result).IsSameReferenceAs(builder);
    }

    [Test]
    public async Task UseMessageTransportFactory_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => OutboxExtensions.UseMessageTransport(null!, _ => new FakeMessageTransport()))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseMessageTransportFactory_WithNullFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = await Assert
            .That(() => OutboxExtensions.UseMessageTransport(builder, null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task UseMessageTransportFactory_WithNoExistingRegistration_AddsTransport()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.UseMessageTransport(_ => new FakeMessageTransport());

        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageTransport)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors).HasSingleItem();
            _ = await Assert.That(descriptors[0].Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseMessageTransportFactory_WithExistingRegistration_ReplacesTransport()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);
        _ = builder.AddOutbox();

        _ = builder.UseMessageTransport(_ => new FakeMessageTransport());

        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageTransport)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors).HasSingleItem();
            _ = await Assert.That(descriptors[0].ImplementationFactory).IsNotNull();
        }
    }

    [Test]
    public async Task UseMessageTransportFactory_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.UseMessageTransport(_ => new FakeMessageTransport());

        _ = await Assert.That(result).IsSameReferenceAs(builder);
    }

    private sealed class FakeMessageTransport : IMessageTransport
    {
        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
