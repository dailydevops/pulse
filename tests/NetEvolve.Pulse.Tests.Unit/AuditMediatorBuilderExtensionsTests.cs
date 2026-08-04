namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Audit")]
public sealed class AuditMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddAudit_NullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => AuditMediatorBuilderExtensions.AddAudit(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task AddAudit_RegistersRequestInterceptorAsScoped()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddAudit();

        _ = await Assert.That(result).IsSameReferenceAs(builder);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<,>)
            && d.ImplementationType == typeof(AuditRequestInterceptor<,>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddAudit_RegistersNullAuditUserAccessorAsSingleton()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddAudit();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditUserAccessor));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(NullAuditUserAccessor));
            _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddAudit_PreviouslyRegisteredAuditUserAccessor_IsNotOverwritten()
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton<IAuditUserAccessor, CustomAuditUserAccessor>();
        var builder = new MediatorBuilder(services);

        _ = builder.AddAudit();

        var descriptors = services.Where(d => d.ServiceType == typeof(IAuditUserAccessor)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors).HasSingleItem();
            _ = await Assert.That(descriptors[0].ImplementationType).IsEqualTo(typeof(CustomAuditUserAccessor));
        }
    }

    [Test]
    public async Task AddAudit_CalledMultipleTimes_DoesNotDuplicateInterceptor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddAudit();
        _ = builder.AddAudit();

        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IRequestInterceptor<,>)
                && d.ImplementationType == typeof(AuditRequestInterceptor<,>)
            )
            .ToList();

        _ = await Assert.That(descriptors).HasSingleItem();
    }

    [Test]
    public async Task AddAudit_ConfigureDelegate_IsApplied()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddAudit(options =>
        {
            options.CapturePayload = true;
            options.AuditQueries = true;
        });

        var provider = services.BuildServiceProvider();
        var configured = provider.GetRequiredService<IOptions<AuditOptions>>().Value;

        using (Assert.Multiple())
        {
            _ = await Assert.That(configured.CapturePayload).IsTrue();
            _ = await Assert.That(configured.AuditQueries).IsTrue();
        }
    }

    [Test]
    public async Task AddAudit_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddAudit();

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsSameReferenceAs(builder);
            _ = await Assert.That(result).IsTypeOf<IMediatorBuilder>();
        }
    }

    private sealed class CustomAuditUserAccessor : IAuditUserAccessor
    {
        public string? GetCurrentUser() => "custom-user";
    }
}
