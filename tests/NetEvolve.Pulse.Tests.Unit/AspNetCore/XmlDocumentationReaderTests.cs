namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
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
    public async Task LoadDocumentation_WithMalformedXml_ReturnsNull()
    {
        var path = CreateTempFile("<doc><members><member name=\"T:Broken\">");

        try
        {
            _ = await Assert.That(XmlDocumentationReader.LoadDocumentation(path)).IsNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task LoadDocumentation_WithoutMembers_ReturnsNull()
    {
        var path = CreateTempFile("<?xml version=\"1.0\"?><doc><assembly><name>Empty</name></assembly></doc>");

        try
        {
            _ = await Assert.That(XmlDocumentationReader.LoadDocumentation(path)).IsNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task LoadDocumentation_WithOnlyIncompleteMembers_ReturnsNull()
    {
        var path = CreateTempFile(
            """
            <?xml version="1.0"?>
            <doc>
              <members>
                <member><summary>No name.</summary></member>
                <member name="T:Some.Namespace.NoSummary"><remarks>No summary.</remarks></member>
              </members>
            </doc>
            """
        );

        try
        {
            _ = await Assert.That(XmlDocumentationReader.LoadDocumentation(path)).IsNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task LoadDocumentation_WithMixedMembers_KeepsOnlyCompleteEntries()
    {
        var path = CreateTempFile(
            """
            <?xml version="1.0"?>
            <doc>
              <members>
                <member><summary>No name.</summary></member>
                <member name="T:Some.Namespace.NoSummary"><remarks>No summary.</remarks></member>
                <member name="T:Some.Namespace.Valid"><summary>Valid summary.</summary></member>
              </members>
            </doc>
            """
        );

        try
        {
            var members = XmlDocumentationReader.LoadDocumentation(path);

            _ = await Assert.That(members).IsNotNull();
            _ = await Assert.That(members!.Count).IsEqualTo(1);
            _ = await Assert.That(members["T:Some.Namespace.Valid"]).IsEqualTo("Valid summary.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task TryGetSummary_WithNullType_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => XmlDocumentationReader.TryGetSummary(null!, out _)).Throws<ArgumentNullException>();

    [Test]
    public async Task TryGetSummary_WithUndocumentedType_ReturnsFalse()
    {
        var found = XmlDocumentationReader.TryGetSummary(typeof(XmlDocumentationReaderTests), out var summary);

        _ = await Assert.That(found).IsFalse();
        _ = await Assert.That(summary).IsNull();
    }

    [Test]
    public async Task TryGetSummary_WithTypeWhoseAssemblyHasNoXmlDocFile_ReturnsFalse()
    {
        var type = typeof(TestAttribute);
        var xmlPath = Path.ChangeExtension(type.Assembly.Location, ".xml");

        _ = await Assert.That(File.Exists(xmlPath)).IsFalse();

        var found = XmlDocumentationReader.TryGetSummary(type, out var summary);

        _ = await Assert.That(found).IsFalse();
        _ = await Assert.That(summary).IsNull();
    }

    [Test]
    public async Task TryGetSummary_WithDynamicAssemblyType_ReturnsFalse()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Dynamic{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run
        );
        var type = assembly.DefineDynamicModule("Module").DefineType("DynamicType").CreateType();

        var found = XmlDocumentationReader.TryGetSummary(type, out var summary);

        _ = await Assert.That(found).IsFalse();
        _ = await Assert.That(summary).IsNull();
    }

    [Test]
    public async Task TryGetSummary_WithDocumentedType_ReturnsSummary()
    {
        var found = XmlDocumentationReader.TryGetSummary(typeof(AspNetCoreOptions), out var summary);

        _ = await Assert.That(found).IsTrue();
        _ = await Assert.That(summary).StartsWith("Provides configuration options for the ASP.NET Core");
    }

    [Test]
    public async Task TryGetSummary_WithMultilineSummaryContainingCref_ReturnsSingleLineWithReferencedName()
    {
        _ = XmlDocumentationReader.TryGetSummary(typeof(AspNetCoreOptions), out var summary);

        _ = await Assert
            .That(summary)
            .IsEqualTo(
                "Provides configuration options for the ASP.NET Core Minimal API integration of the Pulse mediator, such as the endpoints mapped via EndpointRouteBuilderExtensions."
            );
    }

    [Test]
    public async Task LoadDocumentation_WithInlineReferenceElements_RendersTheirNames()
    {
        var path = CreateTempFile(
            """
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:Some.Namespace.Refs">
                  <summary>
                  Uses <see cref="T:System.Collections.Generic.List`1"/>, <see cref="M:Some.Type.Run(System.String)"/>,
                  <paramref name="value"/>, <typeparamref name="T"/> and <see langword="null"/>.
                  </summary>
                </member>
              </members>
            </doc>
            """
        );

        try
        {
            var members = XmlDocumentationReader.LoadDocumentation(path);

            _ = await Assert.That(members).IsNotNull();
            _ = await Assert.That(members!["T:Some.Namespace.Refs"]).IsEqualTo("Uses List, Run, value, T and null.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task LoadDocumentation_WithBlockElements_SeparatesThemBySpaces()
    {
        var path = CreateTempFile(
            """
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:Some.Namespace.Blocks">
                  <summary>Intro<br/>line.<para>First.</para><para>Second.</para><list type="bullet"><item><description>One.</description></item><item><description>Two.</description></item></list></summary>
                </member>
              </members>
            </doc>
            """
        );

        try
        {
            var members = XmlDocumentationReader.LoadDocumentation(path);

            _ = await Assert.That(members).IsNotNull();
            _ = await Assert
                .That(members!["T:Some.Namespace.Blocks"])
                .IsEqualTo("Intro line. First. Second. One. Two.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task LoadDocumentation_WithEmptyLongFormReferenceElements_RendersTheirNames()
    {
        var path = CreateTempFile(
            """
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:Some.Namespace.LongForm">
                  <summary>Uses <see cref="T:Some.Namespace.Target"></see> and <see langword="true"></see>.</summary>
                </member>
              </members>
            </doc>
            """
        );

        try
        {
            var members = XmlDocumentationReader.LoadDocumentation(path);

            _ = await Assert.That(members).IsNotNull();
            _ = await Assert.That(members!["T:Some.Namespace.LongForm"]).IsEqualTo("Uses Target and true.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task TryGetSummary_WithSameTypeTwice_ReturnsSameInstance()
    {
        _ = XmlDocumentationReader.TryGetSummary(typeof(AspNetCoreOptions), out var first);
        _ = XmlDocumentationReader.TryGetSummary(typeof(AspNetCoreOptions), out var second);

        _ = await Assert.That(first).IsNotNull();
        _ = await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async Task TryGetSummary_WithDocumentedNestedType_ReturnsSummary()
    {
        var found = XmlDocumentationReader.TryGetSummary(typeof(DocumentedNestedType), out var summary);

        _ = await Assert.That(found).IsTrue();
        _ = await Assert.That(summary).IsEqualTo("Documented nested type.");
    }

    [Test]
    public async Task TryGetSummary_WithDocumentedClosedGenericType_ReturnsSummary()
    {
        var found = XmlDocumentationReader.TryGetSummary(typeof(DocumentedGenericType<string>), out var summary);

        _ = await Assert.That(found).IsTrue();
        _ = await Assert.That(summary).IsEqualTo("Documented generic type.");
    }

    [Test]
    public async Task TryGetSummary_WithDocumentedOpenGenericType_ReturnsSummary()
    {
        var found = XmlDocumentationReader.TryGetSummary(typeof(DocumentedGenericType<>), out var summary);

        _ = await Assert.That(found).IsTrue();
        _ = await Assert.That(summary).IsEqualTo("Documented generic type.");
    }

    [Test]
    public async Task TryGetSummary_WithGenericParameterType_ReturnsFalse()
    {
        var found = XmlDocumentationReader.TryGetSummary(
            typeof(DocumentedGenericType<>).GetGenericArguments()[0],
            out var summary
        );

        _ = await Assert.That(found).IsFalse();
        _ = await Assert.That(summary).IsNull();
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, content);
        return path;
    }

    private static string CreateTempXmlDocumentationFile(string memberName, string summary) =>
        CreateTempFile(
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

#pragma warning disable S2094, S2326 // Empty types intentionally used only to exercise the XML documentation lookup.
    /// <summary>Documented nested type.</summary>
    internal sealed class DocumentedNestedType;

    /// <summary>Documented generic type.</summary>
    /// <typeparam name="T">Unused type parameter.</typeparam>
    internal sealed class DocumentedGenericType<T>;
#pragma warning restore S2094, S2326
}
