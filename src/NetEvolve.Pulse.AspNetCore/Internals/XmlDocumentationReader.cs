namespace NetEvolve.Pulse.AspNetCore.Internals;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "An empty Assembly.Location is handled explicitly: no summary is returned and the OpenAPI metadata falls back to the defaults."
    )]
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

                // XML doc ids use '.' for nested types and the open generic definition (e.g. `1).
                var fullName = (t.IsConstructedGenericType ? t.GetGenericTypeDefinition() : t).FullName;
                if (fullName is null)
                {
                    return null;
                }

                var memberId = "T:" + fullName.Replace('+', '.');
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
                            Summary = member.Element("summary") is { } summary ? RenderText(summary) : null,
                        })
                        .Where(entry => entry.Name is not null && entry.Summary is not null)
                        .ToDictionary(entry => entry.Name!, entry => entry.Summary!, StringComparer.Ordinal);

                    return members.Count > 0 ? members : null;
                }
                catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
                {
                    return null;
                }
            }
        );

    /// <summary>
    /// Renders the text of a documentation element as a single line, replacing inline reference
    /// elements (<c>see</c>, <c>seealso</c>, <c>paramref</c>, <c>typeparamref</c>) with their names
    /// and separating block elements (<c>para</c>, <c>br</c>, list items) by a space.
    /// </summary>
    private static string RenderText(XElement element) =>
        string.Join(' ', RenderNodes(element).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string RenderNodes(XElement element) => string.Concat(element.Nodes().Select(RenderNode));

    private static string RenderNode(XNode node) =>
        node switch
        {
            XText text => text.Value,
            XElement { Name.LocalName: "para" or "br" or "list" or "item" or "term" or "description" } element => " "
                + RenderNodes(element)
                + " ",
            XElement element when element.Nodes().Any() => RenderNodes(element),
            XElement element when (string?)element.Attribute("cref") is { } cref => FormatCref(cref),
            XElement element when (string?)element.Attribute("langword") is { } langword => langword,
            XElement element when (string?)element.Attribute("name") is { } name => name,
            _ => string.Empty,
        };

    /// <summary>
    /// Reduces a documentation id such as <c>M:Namespace.Type.Method``1(System.String)</c> to its simple name.
    /// </summary>
    private static string FormatCref(string cref)
    {
        var name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;

        var parameters = name.IndexOf('(', StringComparison.Ordinal);
        if (parameters >= 0)
        {
            name = name[..parameters];
        }

        name = name[(name.LastIndexOf('.') + 1)..];

        var arity = name.IndexOf('`', StringComparison.Ordinal);
        return arity >= 0 ? name[..arity] : name;
    }
}
