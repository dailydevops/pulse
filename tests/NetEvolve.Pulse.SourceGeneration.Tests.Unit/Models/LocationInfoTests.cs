namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit.Models;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.SourceGeneration.Models;
using TUnit.Core;

[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.Models")]
public class LocationInfoTests
{
    [Test]
    public async Task CreateFromThenCapturesFilePathAndSpansOfNode(CancellationToken cancellationToken = default)
    {
        const string source = """
            public class MyClass
            {
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, path: "TestFile.cs", cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        var locationInfo = LocationInfo.CreateFrom(classDeclaration);
        var expectedLocation = classDeclaration.GetLocation();

        _ = await Assert.That(locationInfo.FilePath).IsEqualTo("TestFile.cs");
        _ = await Assert.That(locationInfo.TextSpan).IsEqualTo(expectedLocation.SourceSpan);
        _ = await Assert.That(locationInfo.LineSpan).IsEqualTo(expectedLocation.GetLineSpan().Span);
    }

    [Test]
    public async Task ToLocationThenReconstructsEquivalentLocation(CancellationToken cancellationToken = default)
    {
        const string source = """
            public class MyClass
            {
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, path: "TestFile.cs", cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var originalLocation = classDeclaration.GetLocation();

        var locationInfo = LocationInfo.CreateFrom(classDeclaration);
        var reconstructed = locationInfo.ToLocation();

        _ = await Assert.That(reconstructed.SourceSpan).IsEqualTo(originalLocation.SourceSpan);
        _ = await Assert.That(reconstructed.GetLineSpan().Path).IsEqualTo(originalLocation.GetLineSpan().Path);
        _ = await Assert
            .That(reconstructed.GetLineSpan().StartLinePosition)
            .IsEqualTo(originalLocation.GetLineSpan().StartLinePosition);
    }

    [Test]
    public async Task EqualsThenTrueForSameFilePathAndSpans()
    {
        var span = new TextSpan(10, 5);
        var lineSpan = new LinePositionSpan(new LinePosition(1, 0), new LinePosition(1, 5));
        var left = new LocationInfo("File.cs", span, lineSpan);
        var right = new LocationInfo("File.cs", span, lineSpan);

        _ = await Assert.That(left.Equals(right)).IsTrue();
        _ = await Assert.That(left.Equals((object)right)).IsTrue();
        _ = await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task EqualsThenFalseWhenFilePathDiffers()
    {
        var span = new TextSpan(10, 5);
        var lineSpan = new LinePositionSpan(new LinePosition(1, 0), new LinePosition(1, 5));
        var left = new LocationInfo("FileA.cs", span, lineSpan);
        var right = new LocationInfo("FileB.cs", span, lineSpan);

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsThenFalseWhenTextSpanDiffers()
    {
        var lineSpan = new LinePositionSpan(new LinePosition(1, 0), new LinePosition(1, 5));
        var left = new LocationInfo("File.cs", new TextSpan(10, 5), lineSpan);
        var right = new LocationInfo("File.cs", new TextSpan(20, 5), lineSpan);

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsThenFalseWhenLineSpanDiffers()
    {
        var span = new TextSpan(10, 5);
        var left = new LocationInfo(
            "File.cs",
            span,
            new LinePositionSpan(new LinePosition(1, 0), new LinePosition(1, 5))
        );
        var right = new LocationInfo(
            "File.cs",
            span,
            new LinePositionSpan(new LinePosition(2, 0), new LinePosition(2, 5))
        );

        _ = await Assert.That(left.Equals(right)).IsFalse();
    }

    [Test]
    public async Task EqualsObjectThenFalseWhenOtherIsNotLocationInfo()
    {
        var locationInfo = new LocationInfo(
            "File.cs",
            new TextSpan(0, 1),
            new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1))
        );

        _ = await Assert.That(locationInfo.Equals("not a LocationInfo")).IsFalse();
    }
}
