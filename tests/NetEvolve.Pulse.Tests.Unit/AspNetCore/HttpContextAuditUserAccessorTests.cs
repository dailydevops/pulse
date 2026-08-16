namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("AspNetCore")]
public sealed class HttpContextAuditUserAccessorTests
{
    [Test]
    public async Task Constructor_NullHttpContextAccessor_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new HttpContextAuditUserAccessor(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task GetCurrentUser_AuthenticatedUser_ReturnsIdentityName()
    {
        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim(ClaimTypes.Name, "jane.doe"));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = new FakeHttpContextAccessor(context);
        var sut = new HttpContextAuditUserAccessor(accessor);

        var result = sut.GetCurrentUser();

        _ = await Assert.That(result).IsEqualTo("jane.doe");
    }

    [Test]
    public async Task GetCurrentUser_UnauthenticatedUser_ReturnsNull()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var accessor = new FakeHttpContextAccessor(context);
        var sut = new HttpContextAuditUserAccessor(accessor);

        var result = sut.GetCurrentUser();

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCurrentUser_NoAmbientHttpContext_ReturnsNull()
    {
        var accessor = new FakeHttpContextAccessor(null);
        var sut = new HttpContextAuditUserAccessor(accessor);

        var result = sut.GetCurrentUser();

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task AddHttpContextAuditUserAccessor_NullBuilder_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => AuditExtensions.AddHttpContextAuditUserAccessor(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddHttpContextAuditUserAccessor_RegistersHttpContextAccessor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        _ = builder.AddHttpContextAuditUserAccessor();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHttpContextAccessor));

        _ = await Assert.That(descriptor).IsNotNull();
    }

    [Test]
    public async Task AddHttpContextAuditUserAccessor_ReplacesPreviouslyRegisteredAuditUserAccessor()
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton<IAuditUserAccessor, PreExistingAuditUserAccessor>();
        var builder = new MediatorBuilder(services);

        _ = builder.AddHttpContextAuditUserAccessor();

        var descriptors = services.Where(d => d.ServiceType == typeof(IAuditUserAccessor)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors).HasSingleItem();
            _ = await Assert.That(descriptors[0].ImplementationType).IsEqualTo(typeof(HttpContextAuditUserAccessor));
        }
    }

    [Test]
    public async Task AddHttpContextAuditUserAccessor_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = new MediatorBuilder(services);

        var result = builder.AddHttpContextAuditUserAccessor();

        _ = await Assert.That(result).IsSameReferenceAs(builder);
    }

    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public FakeHttpContextAccessor(HttpContext? httpContext) => HttpContext = httpContext;

        public HttpContext? HttpContext { get; set; }
    }

    private sealed class PreExistingAuditUserAccessor : IAuditUserAccessor
    {
        public string? GetCurrentUser() => "pre-existing";
    }
}
