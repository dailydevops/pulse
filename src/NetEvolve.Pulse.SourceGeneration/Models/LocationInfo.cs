namespace NetEvolve.Pulse.SourceGeneration.Models;

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Equatable, syntax-tree-free representation of a source location for use in incremental
/// pipeline models. Storing <see cref="Microsoft.CodeAnalysis.Location"/> directly would root
/// the originating <see cref="SyntaxTree"/> in the generator driver's caches.
/// </summary>
internal readonly struct LocationInfo : IEquatable<LocationInfo>
{
    /// <summary>Gets the file path of the source location.</summary>
    public string FilePath { get; }

    /// <summary>Gets the text span of the source location.</summary>
    public TextSpan TextSpan { get; }

    /// <summary>Gets the line/column span of the source location.</summary>
    public LinePositionSpan LineSpan { get; }

    /// <summary>
    /// Initializes a new <see cref="LocationInfo"/> with the given file path, text span, and line span.
    /// </summary>
    /// <param name="filePath">The file path of the source location.</param>
    /// <param name="textSpan">The text span of the source location.</param>
    /// <param name="lineSpan">The line/column span of the source location.</param>
    public LocationInfo(string filePath, TextSpan textSpan, LinePositionSpan lineSpan)
    {
        FilePath = filePath;
        TextSpan = textSpan;
        LineSpan = lineSpan;
    }

    /// <summary>
    /// Captures the location of <paramref name="node"/> without retaining a reference to its
    /// <see cref="SyntaxTree"/>.
    /// </summary>
    /// <param name="node">The syntax node whose location is captured.</param>
    /// <returns>The tree-free location representation.</returns>
    public static LocationInfo CreateFrom(SyntaxNode node)
    {
        var location = node.GetLocation();
        return new LocationInfo(
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan,
            location.GetLineSpan().Span
        );
    }

    /// <summary>
    /// Reconstructs a <see cref="Location"/> suitable for diagnostic reporting.
    /// </summary>
    /// <returns>The reconstructed location.</returns>
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    /// <inheritdoc />
    public bool Equals(LocationInfo other) =>
        string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
        && TextSpan == other.TextSpan
        && LineSpan == other.LineSpan;

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is LocationInfo other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = FilePath is null ? 0 : StringComparer.Ordinal.GetHashCode(FilePath);
            hash = (hash * 31) + TextSpan.GetHashCode();
            hash = (hash * 31) + LineSpan.GetHashCode();
            return hash;
        }
    }
}
