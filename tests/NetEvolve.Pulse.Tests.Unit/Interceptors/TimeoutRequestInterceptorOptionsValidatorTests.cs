namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

[TestGroup("Interceptors")]
public class TimeoutRequestInterceptorOptionsValidatorTests
{
    private static readonly TimeoutRequestInterceptorOptionsValidator _validator = new();

    [Test]
    public async Task Validate_WithNullGlobalTimeout_Succeeds()
    {
        var options = new TimeoutRequestInterceptorOptions { GlobalTimeout = null };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithPositiveGlobalTimeout_Succeeds()
    {
        var options = new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromSeconds(30) };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithZeroGlobalTimeout_Fails()
    {
        var options = new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.Zero };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithNegativeGlobalTimeout_Fails()
    {
        var options = new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromSeconds(-1) };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_DefaultOptions_Succeeds()
    {
        var options = new TimeoutRequestInterceptorOptions();

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }
}
