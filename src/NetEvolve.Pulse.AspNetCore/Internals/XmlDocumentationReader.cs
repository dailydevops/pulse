namespace NetEvolve.Pulse.Internals;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

/// <summary>
/// Reads <c>&lt;summary&gt;</c> text for a <see cref="Type"/> from its assembly's generated
/// XML documentation file.
/// </summary>
internal static class XmlDocumentationReader
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>?> DocumentationCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly ConcurrentDictionary<Type, string?> SummaryCache = new();

    /// <summary>
    /// Attempts to retrieve the <c>&lt;summary&gt;</c> documentation text for <paramref name="type"/>
    /// from the XML documentation file generated for its declaring assembly.
    /// </summary>
    /// <param name="type">The type to look up.</param>
    /// <param name="summary">
    /// When this method returns <see langword="true"/>, contains the trimmed summary text;
    /// otherwise, <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a summary was found for <paramref name="type"/>; otherwise, <see langword="false"/>.
    /// </returns>
    internal static bool TryGetSummary(Type type, out string? summary)
    {
        ArgumentNullException.ThrowIfNull(type);

        summary = SummaryCache.GetOrAdd(
            type,
            static t =>
            {
                var assemblyLocation = t.Assembly.Location;
                if (string.IsNullOrEmpty(assemblyLocation))
                {
                    return null;
                }

                var xmlPath = Path.ChangeExtension(assemblyLocation, ".xml");
                var members = LoadDocumentation(xmlPath);
                if (members is null)
                {
                    return null;
                }

                var memberId = "T:" + t.FullName;
                return members.TryGetValue(memberId, out var value) ? value : null;
            }
        );

        return summary is not null;
    }

    /// <summary>
    /// Loads and caches the <c>&lt;summary&gt;</c> documentation entries contained in the XML
    /// documentation file located at <paramref name="xmlPath"/>, keyed by their <c>name</c>
    /// attribute (e.g. <c>T:Namespace.TypeName</c>).
    /// </summary>
    /// <param name="xmlPath">The full path to the XML documentation file.</param>
    /// <returns>
    /// A read-only dictionary of member id to trimmed summary text, or <see langword="null"/>
    /// when the file does not exist or contains no usable documentation.
    /// </returns>
    internal static IReadOnlyDictionary<string, string>? LoadDocumentation(string xmlPath) =>
        DocumentationCache.GetOrAdd(
            xmlPath,
            static path =>
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                try
                {
                    var document = XDocument.Load(path);
                    var members = document
                        .Descendants("member")
                        .Select(member => new
                        {
                            Name = (string?)member.Attribute("name"),
                            Summary = member.Element("summary")?.Value,
                        })
                        .Where(entry => entry.Name is not null && entry.Summary is not null)
                        .ToDictionary(entry => entry.Name!, entry => entry.Summary!.Trim(), StringComparer.Ordinal);

                    return members.Count > 0 ? members : null;
                }
                catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
                {
                    return null;
                }
            }
        );
}
