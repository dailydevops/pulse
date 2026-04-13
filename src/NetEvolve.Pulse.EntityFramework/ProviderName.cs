namespace NetEvolve.Pulse;

/// <summary>
/// Static class containing constants for supported EF Core provider names, used for provider-specific
/// </summary>
internal static class ProviderName
{
    /// <summary>
    /// The provider name for the EF Core InMemory provider (<c>Microsoft.EntityFrameworkCore.InMemory</c>).
    /// Intended for testing only.
    /// </summary>
    internal const string InMemory = "Microsoft.EntityFrameworkCore.InMemory";

    /// <summary>
    /// The provider name for Npgsql (PostgreSQL).
    /// </summary>
    internal const string Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// The provider name for Microsoft.EntityFrameworkCore.Sqlite.
    /// </summary>
    internal const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>
    /// The provider name for Microsoft.EntityFrameworkCore.SqlServer.
    /// </summary>
    internal const string SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";

    /// <summary>
    /// The provider name for Pomelo MySQL (<c>Pomelo.EntityFrameworkCore.MySql</c>).
    /// </summary>
    internal const string PomeloMySql = "Pomelo.EntityFrameworkCore.MySql";

    /// <summary>
    /// The provider name for the Oracle MySQL provider (<c>MySql.EntityFrameworkCore</c>).
    /// </summary>
    internal const string OracleMySql = "MySql.EntityFrameworkCore";
}
