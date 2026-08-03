namespace NetEvolve.Pulse.Tests.Unit;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Caching;
using TUnit.Core;

[TestGroup("QueryCaching")]
public sealed class QueryCachingOptionsValidatorTests
{
    private static readonly QueryCachingOptionsValidator _validator = new();

    [Test]
    public async Task Validate_WithNullDefaultExpiry_Succeeds()
    {
        var options = new QueryCachingOptions { DefaultExpiry = null };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithPositiveDefaultExpiry_Succeeds()
    {
        var options = new QueryCachingOptions { DefaultExpiry = TimeSpan.FromMinutes(10) };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithZeroDefaultExpiry_Fails()
    {
        var options = new QueryCachingOptions { DefaultExpiry = TimeSpan.Zero };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithNegativeDefaultExpiry_Fails()
    {
        var options = new QueryCachingOptions { DefaultExpiry = TimeSpan.FromMinutes(-1) };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_NullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => _validator.Validate(null, null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Validate_DefaultOptions_Succeeds()
    {
        var options = new QueryCachingOptions();

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }
}
