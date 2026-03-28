namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

public class LoggingInterceptorOptionsValidatorTests
{
    private static readonly LoggingInterceptorOptionsValidator _validator = new();

    [Test]
    public async Task Validate_WithNullThreshold_Succeeds()
    {
        var options = new LoggingInterceptorOptions { SlowRequestThreshold = null };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithZeroThreshold_Succeeds()
    {
        var options = new LoggingInterceptorOptions { SlowRequestThreshold = TimeSpan.Zero };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithPositiveThreshold_Succeeds()
    {
        var options = new LoggingInterceptorOptions { SlowRequestThreshold = TimeSpan.FromMilliseconds(500) };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithNegativeThreshold_Fails()
    {
        var options = new LoggingInterceptorOptions { SlowRequestThreshold = TimeSpan.FromMilliseconds(-1) };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_DefaultOptions_Succeeds()
    {
        var options = new LoggingInterceptorOptions();

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }
}
