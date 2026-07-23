namespace NetEvolve.Pulse.Extensibility.Outbox;

using System;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Validates SQL identifiers (schema names, table names) used by outbox and idempotency-store
/// implementations to prevent SQL injection via configuration-supplied values.
/// </summary>
/// <remarks>
/// <para>
/// Persistence providers in this library construct SQL by interpolating <c>Schema</c> and
/// <c>TableName</c> options into bracketed/quoted identifiers (for example
/// <c>[schema].[table]</c> for SQL Server or <c>"schema"."table"</c> for PostgreSQL).
/// If those option values contain the closing quote character — <c>]</c>, <c>"</c>, or
/// <c>`</c> — an attacker who controls configuration (e.g. an environment variable bound by
/// <c>IConfiguration</c>) can break out of the identifier and inject arbitrary SQL.
/// </para>
/// <para>
/// To prevent this, identifiers are restricted to the safe subset
/// <c>[A-Za-z_][A-Za-z0-9_]*</c>: a letter or underscore followed by letters, digits, or
/// underscores. The maximum allowed length is 128 characters, matching SQL Server's
/// <c>sysname</c>. Validation runs at the point where each repository derives its SQL,
/// so misconfigured options fail fast at startup rather than at the first query execution.
/// </para>
/// </remarks>
public static class SqlIdentifier
{
    /// <summary>The maximum permitted identifier length (matches SQL Server <c>sysname</c>).</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Validates that <paramref name="identifier"/> is a safe SQL identifier suitable for
    /// interpolation into quoted SQL syntax.
    /// </summary>
    /// <param name="identifier">The identifier to validate. Must be non-empty and match
    /// <c>[A-Za-z_][A-Za-z0-9_]*</c> with at most <see cref="MaxLength"/> characters.</param>
    /// <param name="paramName">The parameter name reported in the thrown exception
    /// (typically <c>nameof(options.Schema)</c> or <c>nameof(options.TableName)</c>).</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="identifier"/> is <see langword="null"/>, empty, exceeds
    /// <see cref="MaxLength"/>, or contains any character outside the allowed alphanumeric/underscore
    /// set. The exception message intentionally does not echo the rejected identifier verbatim,
    /// to avoid surfacing attacker-controlled payloads in logs.
    /// </exception>
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Identifier validation is a structural check; case is not normalized."
    )]
    public static void Validate(string? identifier, string paramName)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            throw new ArgumentException($"SQL identifier '{paramName}' must be non-empty.", paramName);
        }

        if (identifier.Length > MaxLength)
        {
            throw new ArgumentException(
                $"SQL identifier '{paramName}' exceeds the maximum length of {MaxLength} characters.",
                paramName
            );
        }

        if (!IsValid(identifier))
        {
            throw new ArgumentException(
                $"SQL identifier '{paramName}' contains characters that are not permitted. "
                    + "Allowed characters: ASCII letters, digits, and underscore; "
                    + "the first character must be a letter or underscore.",
                paramName
            );
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="identifier"/> matches
    /// <c>[A-Za-z_][A-Za-z0-9_]*</c>.
    /// </summary>
    /// <param name="identifier">The identifier to inspect; may be <see langword="null"/> or empty.</param>
    public static bool IsValid([NotNullWhen(true)] string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return false;
        }

        var first = identifier[0];
        if (!IsAsciiLetter(first) && first != '_')
        {
            return false;
        }

        for (var i = 1; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
}
