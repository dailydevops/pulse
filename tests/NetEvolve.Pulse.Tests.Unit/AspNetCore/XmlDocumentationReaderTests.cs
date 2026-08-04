namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.IO;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.AspNetCore.Internals;
using TUnit.Core;

[TestGroup("AspNetCore")]
public sealed class XmlDocumentationReaderTests
{
    [Test]
    public async Task LoadDocumentation_WithExistingFileAndKnownMember_ReturnsSummary()
    {
        var path = CreateTempXmlDocumentationFile("T:Some.Namespace.SomeType", "Test summary.");

        try
        {
            var members = XmlDocumentationReader.LoadDocumentation(path);

            _ = await Assert.That(members).IsNotNull();
            _ = await Assert.That(members!["T:Some.Namespace.SomeType"]).IsEqualTo("Test summary.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task LoadDocumentation_WithMissingFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");

        var members = XmlDocumentationReader.LoadDocumentation(path);

        _ = await Assert.That(members).IsNull();
    }

    [Test]
    public async Task LoadDocumentation_WithSameFileTwiceAfterDeletion_UsesCachedResult()
    {
        var path = CreateTempXmlDocumentationFile("T:Some.Namespace.CachedType", "Cached summary.");

        var firstResult = XmlDocumentationReader.LoadDocumentation(path);
        File.Delete(path);

        var secondResult = XmlDocumentationReader.LoadDocumentation(path);

        _ = await Assert.That(firstResult).IsNotNull();
        _ = await Assert.That(secondResult).IsNotNull();
        _ = await Assert.That(secondResult!["T:Some.Namespace.CachedType"]).IsEqualTo("Cached summary.");
    }

    [Test]
    public async Task TryGetSummary_WithNullType_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => XmlDocumentationReader.TryGetSummary(null!, out _)).Throws<ArgumentNullException>();

    [Test]
    public async Task TryGetSummary_WithTypeWhoseAssemblyHasNoXmlDocFile_ReturnsFalse()
    {
        var found = XmlDocumentationReader.TryGetSummary(typeof(XmlDocumentationReaderTests), out var summary);

        _ = await Assert.That(found).IsFalse();
        _ = await Assert.That(summary).IsNull();
    }

    private static string CreateTempXmlDocumentationFile(string memberName, string summary)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");

        File.WriteAllText(
            path,
            $"""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="{memberName}">
                  <summary>
                  {summary}
                  </summary>
                </member>
              </members>
            </doc>
            """
        );

        return path;
    }
}
