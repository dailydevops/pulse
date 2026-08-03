namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="OutboxOptions"/> ensuring that
/// <see cref="OutboxOptions.TableName"/> is not <see langword="null"/>, empty, or whitespace.
/// </summary>
/// <remarks>
/// <see cref="OutboxOptions.ConnectionString"/> is intentionally not validated here: it is required
/// only by ADO.NET-based providers (e.g., PostgreSQL, SQL Server, SQLite) and legitimately remains
/// <see langword="null"/> for Entity Framework Core-based outbox usage, which resolves its connection
/// through a registered <c>DbContext</c> instead.
/// </remarks>
internal sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TableName))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(OutboxOptions.TableName)} must not be null, empty, or whitespace."
            );
        }

        return ValidateOptionsResult.Success;
    }
}
