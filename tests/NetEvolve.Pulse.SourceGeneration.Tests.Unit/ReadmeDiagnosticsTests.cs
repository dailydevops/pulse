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
    public async Task WhenReadmeIsReadThenEveryDiagnosticHasARowWithItsSeverity(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var readme = await File.ReadAllLinesAsync(
                Path.Combine(AppContext.BaseDirectory, "SourceGeneration.README.md"),
                cancellationToken
            )
            .ConfigureAwait(false);

        var missingRows = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)
            .Select(descriptor => $"| {descriptor.Id} | {descriptor.DefaultSeverity} |")
            .Where(row => !readme.Any(line => line.StartsWith(row, StringComparison.Ordinal)))
            .ToArray();

        _ = await Assert.That(missingRows).IsEmpty();
    }
}
