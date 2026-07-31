using Microsoft.Data.Sqlite;
using MyHi.Companion.Core.Data;

namespace MyHi.Companion.Tests.Data;

public class MigrationRunnerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("myhi-db-tests-").FullName;

    private string DbPathFor(string name) => Path.Combine(_tempDir, name);

    [Fact]
    public void First_run_creates_database_file_and_schema_version_table()
    {
        var dbPath = DbPathFor("phase00.db");
        Assert.False(File.Exists(dbPath));

        var factory = new SqliteConnectionFactory(dbPath);
        using var connection = factory.Create();
        new MigrationRunner([]).Apply(connection);

        Assert.True(File.Exists(dbPath));

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';";
        Assert.Equal("SchemaVersion", cmd.ExecuteScalar());
    }

    [Fact]
    public void Empty_migration_set_leaves_version_at_zero()
    {
        var dbPath = DbPathFor("phase00.db");
        var factory = new SqliteConnectionFactory(dbPath);
        using var connection = factory.Create();

        new MigrationRunner([]).Apply(connection);

        Assert.Equal(0, MigrationRunner.GetCurrentVersion(connection));
    }

    [Fact]
    public void Applies_pending_migrations_in_order_inside_one_transaction()
    {
        var dbPath = DbPathFor("phase00.db");
        var factory = new SqliteConnectionFactory(dbPath);
        using var connection = factory.Create();

        var migrations = new[]
        {
            new Migration(1, "CREATE TABLE Foo (Id INTEGER PRIMARY KEY);"),
            new Migration(2, "ALTER TABLE Foo ADD COLUMN Name TEXT;"),
        };

        new MigrationRunner(migrations).Apply(connection);

        Assert.Equal(2, MigrationRunner.GetCurrentVersion(connection));

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Foo);";
        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Equal(["Id", "Name"], columns);
    }

    [Fact]
    public void Re_running_does_not_reapply_already_applied_migrations()
    {
        var dbPath = DbPathFor("phase00.db");
        var factory = new SqliteConnectionFactory(dbPath);
        var migrations = new[] { new Migration(1, "CREATE TABLE Foo (Id INTEGER PRIMARY KEY);") };

        using (var first = factory.Create())
        {
            new MigrationRunner(migrations).Apply(first);
        }

        using var second = factory.Create();
        // Would throw "table already exists" if re-applied.
        new MigrationRunner(migrations).Apply(second);

        Assert.Equal(1, MigrationRunner.GetCurrentVersion(second));
    }

    [Fact]
    public void Foreign_keys_pragma_is_enabled_so_cascade_delete_works()
    {
        var dbPath = DbPathFor("phase00.db");
        var factory = new SqliteConnectionFactory(dbPath);
        using var connection = factory.Create();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, recursive: true);
    }
}
