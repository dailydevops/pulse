namespace NetEvolve.Pulse.Tests.Unit.Audit;

using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using NetEvolve.Extensions.TUnit;
using TUnit.Core;

/// <summary>
/// Phase 2 / Round 01 / U03 — XML doc references non-existent NuGet package.
///
/// The XML doc on <see cref="AssemblyScanningExtensions"/> recommends referencing
/// "NetEvolve.Pulse.Generators" but the actual package id (driven by the project's
/// AssemblyName / PackageId) is "NetEvolve.Pulse.SourceGeneration".
///
/// This test is intentionally FAILING — it asserts the XML doc contains the real
/// package id. Phase 3 must update the XML doc and then this assertion will pass.
/// </summary>
[TestGroup("Audit-Round01-U03")]
public class U03_AssemblyScanningPackageNameTests
{
    private const string ActualPackageId = "NetEvolve.Pulse.SourceGeneration";
    private const string DocumentedPackageId = "NetEvolve.Pulse.Generators";

    [Test]
    public async Task AssemblyScanningExtensions_XmlDoc_References_The_Real_SourceGeneration_Package()
    {
        var xmlPath = GetPulseXmlDocPath();

        _ = await Assert
            .That(File.Exists(xmlPath))
            .IsTrue()
            .Because($"Expected XML documentation at '{xmlPath}'. Did GenerateDocumentationFile get disabled?");

        var doc = XDocument.Load(xmlPath);
        var memberId = "T:NetEvolve.Pulse.AssemblyScanningExtensions";
        var member = doc.Descendants("member")
            .FirstOrDefault(m =>
                string.Equals((string?)m.Attribute("name"), memberId, System.StringComparison.Ordinal)
            );

        _ = await Assert.That(member).IsNotNull().Because($"XML doc member '{memberId}' not found in {xmlPath}.");

        var memberText = member!.ToString();

        // The XML doc must recommend the package that actually exists on NuGet.
        // Currently the doc cites a phantom package id, which is the bug under audit (U03).
        using (Assert.Multiple())
        {
            _ = await Assert
                .That(memberText.Contains(ActualPackageId, System.StringComparison.Ordinal))
                .IsTrue()
                .Because(
                    $"AssemblyScanningExtensions XML doc must recommend the real package id '{ActualPackageId}'. "
                        + $"Currently it references the phantom id '{DocumentedPackageId}'."
                );

            _ = await Assert
                .That(memberText.Contains(DocumentedPackageId, System.StringComparison.Ordinal))
                .IsFalse()
                .Because(
                    $"AssemblyScanningExtensions XML doc must NOT reference the phantom package id '{DocumentedPackageId}'."
                );
        }
    }

    private static string GetPulseXmlDocPath()
    {
        // The test runs from its own bin directory. The NetEvolve.Pulse XML doc is copied next to
        // its DLL in the test output via project reference.
        var probe = typeof(AssemblyScanningExtensions).Assembly.Location;
        var dir = Path.GetDirectoryName(probe)!;
        return Path.Combine(dir, "NetEvolve.Pulse.xml");
    }
}
