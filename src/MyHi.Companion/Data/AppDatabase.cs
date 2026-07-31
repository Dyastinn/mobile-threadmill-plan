using MyHi.Companion.Core.Data;

namespace MyHi.Companion.Data;

/// <summary>
/// Phase 00 ships an empty migration set — the schema itself (Workout,
/// WorkoutSample, Device) is Phase 06's job. This just guarantees the database
/// file and SchemaVersion table exist on first run (TASKS.md 0.11).
/// </summary>
public sealed class AppDatabase(SqliteConnectionFactory connectionFactory)
{
    public SqliteConnectionFactory ConnectionFactory { get; } = connectionFactory;

    public void Initialize()
    {
        using var connection = ConnectionFactory.Create();
        new MigrationRunner([]).Apply(connection);
    }
}
