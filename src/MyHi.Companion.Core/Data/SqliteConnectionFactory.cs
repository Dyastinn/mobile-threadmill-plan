using Microsoft.Data.Sqlite;

namespace MyHi.Companion.Core.Data;

/// <summary>
/// Opens connections with the PRAGMAs 14-Database.md requires on every connection.
/// <c>foreign_keys</c> in particular is off by default in Microsoft.Data.Sqlite, and
/// <c>ON DELETE CASCADE</c> silently does nothing without it.
/// </summary>
public sealed class SqliteConnectionFactory(string databasePath)
{
    public string DatabasePath { get; } = databasePath;

    public SqliteConnection Create()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        ApplyPragmas(connection);
        return connection;
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            """;
        cmd.ExecuteNonQuery();
    }
}
