namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NetEvolve.Extensions.TUnit;
using TUnit.Core;

/// <summary>
/// Keeps the Diagnostics table of the package README in sync with <see cref="DiagnosticDescriptors"/>.
/// </summary>
[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.Readme")]
public class ReadmeDiagnosticsTests
{
    [Test]
    public async Task WhenReadmeIsReadThenDiagnosticRowsMatchTheDescriptors(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var readme = await File.ReadAllLinesAsync(
                Path.Combine(AppContext.BaseDirectory, "SourceGeneration.README.md"),
                cancellationToken
            )
            .ConfigureAwait(false);

        var expectedRows = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)
            .Select(descriptor => $"{descriptor.Id} | {descriptor.DefaultSeverity}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        var readmeRows = readme
            .Where(line => line.StartsWith("| PULSE", StringComparison.Ordinal))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Select(cells => $"{cells[1]} | {cells[2]}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        using (Assert.Multiple())
        {
            _ = await Assert.That(expectedRows).IsNotEmpty();
            _ = await Assert.That(readmeRows).IsEquivalentTo(expectedRows);
        }
    }
}
