using Microsoft.Data.Sqlite;

namespace MyHi.Companion.Core.Data;

/// <summary>One forward-only schema migration. Never edit a shipped migration; add a new one.</summary>
public sealed record Migration(int Version, string Sql);

/// <summary>
/// Applies pending migrations at startup inside a transaction, tracked in
/// <c>SchemaVersion</c> per 14-Database.md. Phase 00 ships this with an empty
/// migration set — the schema itself (Workout, WorkoutSample, Device) is Phase 06's
/// job; this phase only needs the DB file and the version table to exist.
/// </summary>
public sealed class MigrationRunner(IReadOnlyList<Migration> migrations)
{
    private const string CreateSchemaVersionTable =
        """
        CREATE TABLE IF NOT EXISTS SchemaVersion (
            Version    INTEGER NOT NULL PRIMARY KEY,
            AppliedUtc TEXT    NOT NULL
        );
        """;

    public void Apply(SqliteConnection connection)
    {
        using (var createTable = connection.CreateCommand())
        {
            createTable.CommandText = CreateSchemaVersionTable;
            createTable.ExecuteNonQuery();
        }

        var currentVersion = GetCurrentVersion(connection);
        var pending = migrations
            .Where(m => m.Version > currentVersion)
            .OrderBy(m => m.Version)
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        foreach (var migration in pending)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = migration.Sql;
                cmd.ExecuteNonQuery();
            }

            using (var recordVersion = connection.CreateCommand())
            {
                recordVersion.Transaction = transaction;
                recordVersion.CommandText = "INSERT INTO SchemaVersion (Version, AppliedUtc) VALUES ($version, $appliedUtc);";
                recordVersion.Parameters.AddWithValue("$version", migration.Version);
                recordVersion.Parameters.AddWithValue("$appliedUtc", DateTimeOffset.UtcNow.ToString("O"));
                recordVersion.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public static int GetCurrentVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }
}
