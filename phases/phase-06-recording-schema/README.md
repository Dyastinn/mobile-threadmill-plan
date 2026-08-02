# Phase 06 — Workout Recording & Schema

**Hardware:** none for development · **Size:** M · **Blocked by:** Phase 04
**Hard dependency:** V1 counter semantics
(`../phase-00-probe-app/PHASE-00-FINDINGS.md`). **Do not start without it.**

---

## Goal

Persist workouts and telemetry durably. Full schema in the "Schema reference"
section below. Read it before writing a migration.

**Phase 00 already built the plumbing this phase writes real data through.** Open all
three of these now, before writing anything new:

- `src/MyHi.Companion.Core/Data/SqliteConnectionFactory.cs`: opens connections and
  applies the required PRAGMAs (`journal_mode=WAL`, `foreign_keys=ON`,
  `synchronous=NORMAL`, `busy_timeout=5000`) on every one. You never call this
  yourself.
- `src/MyHi.Companion.Core/Data/MigrationRunner.cs`: applies a list of
  `Migration(Version, Sql)` records in order, once each, inside a transaction,
  tracked in a `SchemaVersion` table it creates itself. Already has its own passing
  tests (`src/MyHi.Companion.Tests/Data/MigrationRunnerTests.cs`). Read them, they're
  the template for the tests you'll write in this phase.
- `src/MyHi.Companion/Data/AppDatabase.cs`: calls the runner with an **empty**
  migration list today (`new MigrationRunner([])`), which only guarantees the
  database file and `SchemaVersion` table exist. Giving it the real schema is task 6.1.

This means the phase is narrower than "build a database layer from scratch": write
the real schema as migration 1, then build the two classes that read and write
through it correctly, a workout header repository and a buffered sample writer.

### Understanding what you're building (read this before the tasks)

**The everyday problem.** Inserting every sample the moment it arrives means
opening a transaction, writing one row, and committing, 720 times an hour. Almost
none of that cost is the write itself; it's the transaction overhead around it.
The fix is to buffer samples in memory and commit them in batches every 30–60
seconds instead of one at a time. Same rows written, far fewer round trips to
disk. That's exactly what `WorkoutSampleBuffer` does. `Add()` appends to an
in-memory `List<WorkoutSampleRow>`, no I/O at all. `Flush()` opens a
`SqliteTransaction`, writes every buffered row, and commits once. Opening the
transaction and waiting for the disk to durably commit it is the expensive part,
not appending a struct to a list.

**Why not just insert every sample as it arrives.** The simplest-sounding
alternative is to skip the buffer entirely: when `ITreadmillService.SampleReceived`
fires, immediately `INSERT` that one row and commit. No buffer class, no `Flush()`,
no timer to wire up in task 6.5, genuinely less code. At the fixed 5-second
cadence this project uses, that's 720 separate open-a-transaction-and-commit
cycles per hour of workout, every workout, forever. Real battery drain and real
flash wear, paid whether or not a crash ever happens.

What does that cost buy you? Almost nothing. If the app crashes mid-workout, the
buffered approach loses at most one flush interval (under a minute of telemetry,
per the 30–60 s interval task 6.5 wires up), which is already the acceptance bar
in this phase's Implementation requirements. Writing per-sample doesn't make that
bound meaningfully better; it just pays for insurance against a loss that's
already small and already tolerated. Batching earns its complexity here because
the guaranteed cost (720 commits/hour) is real and the naive approach's only
advantage (marginally less data lost in a rare crash) isn't.

Contrast this with `WorkoutRepository.StartWorkout`/`CompleteWorkout`: those write
directly, unbuffered, on purpose. A workout header is written twice per workout,
not 720 times, so there's no "720 trips" problem to solve and adding a buffer
there would be pure overhead with nothing to show for it.

**The pattern, named plainly.** This is **batching writes behind an in-memory
buffer with an explicit commit boundary**, sometimes called write-behind
buffering. The cost: a caller (`WorkoutSampleBuffer`) now has state that can be
lost (whatever's in `_pending` when the process dies), and a flush must be
idempotent (hence `INSERT OR REPLACE`: flushing the same rows twice, e.g. after a
retry, must not duplicate rows) since you can no longer assume every write happens
exactly once. The payoff, specific to this project: roughly 60–120 commits per
hour instead of 720, a 6–12x reduction, while keeping the transaction as the
atomicity boundary, so a flush either fully lands or fully doesn't, never a
half-written batch.

Two more decisions in this phase lean on the same cost-vs-payoff reasoning, worth
naming briefly rather than treating as arbitrary. `Workout` carries **two**
identity columns: `Id` (integer, autoincrement, used for every foreign key and
join) and `WorkoutId` (a GUID, minted once in `StartWorkout`), because they serve
different audiences. `Id` only has to be unique on *this* phone; `WorkoutId` has
to be unique across *every* phone that might ever restore a backup, and an
autoincrement integer can't promise that (two phones will both mint `Id = 1` for
their first workout). One column doing both jobs would silently merge unrelated
workouts during a restore. The extra column is the cost, cross-device correctness
is the payoff. And writing the `Workout` row with `Status = 0` at workout
**start**, not after it finishes, is the same fail-fast instinct as Phase 01a's
length validation: pay a small cost upfront (a row exists before you know the
workout succeeded) so a crash mid-workout leaves a recoverable, flaggable row
instead of nothing at all.

## Learning goals

- ADO.NET in `Microsoft.Data.Sqlite`: `SqliteCommand`, parameterized queries,
  `SqliteTransaction`, the same shape you'd use against any other .NET data
  provider later (SQL Server, Postgres, ...), so this transfers
- Why buffering writes and flushing in one transaction beats one transaction per row,
  both for correctness (atomicity: a flush either fully happens or fully doesn't) and
  for real hardware constraints (flash wear, battery)
- The forward-only migration pattern: a `SchemaVersion` table, a list of
  `(Version, Sql)` pairs applied once each, never edited after being shipped. Add a
  new migration instead
- `WITHOUT ROWID` tables and composite primary keys: when SQLite's default hidden
  rowid is redundant and worth explicitly dropping
- The crash-recovery pattern: write state **before** an operation completes
  (`Status = 0`), not after, so a crash mid-operation is detectable and recoverable
  instead of silently losing data
- Portable identity vs. physical primary key: why `Workout` has two different kinds
  of "ID" and what breaks (silently) if you conflate them across devices

## Reference docs

| Topic | Link | Relevant to |
|---|---|---|
| `Microsoft.Data.Sqlite` overview | https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/ | The general shape of the API this whole phase uses |
| `Microsoft.Data.Sqlite` connection strings | https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings | Background on what `SqliteConnectionFactory` already builds for you |
| `Microsoft.Data.Sqlite` data types | https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types | SQLite's dynamic typing vs. your C# field types — read before 6.2/6.3 |
| `SqliteConnection.BeginTransaction` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlite.sqliteconnection.begintransaction | Task 6.3 — the buffered flush is one transaction, not 720 |
| SQLite Foreign Key Support | https://sqlite.org/foreignkeys.html | Why `PRAGMA foreign_keys = ON` is required before `ON DELETE CASCADE` does anything — `SqliteConnectionFactory` already sets it; this explains why it has to |
| SQLite Write-Ahead Logging (WAL) | https://www.sqlite.org/wal.html | Why the connection factory sets `journal_mode = WAL` — the UI reads for charts while the buffered writer flushes, and WAL is what keeps those from blocking each other |
| `System.Threading.Timer` | https://learn.microsoft.com/en-us/dotnet/api/system.threading.timer | Task 6.5 — the periodic flush timer |

## Technology decision: `Microsoft.Data.Sqlite`

**What problem does it solve?** Workout history and per-workout telemetry need
durable local storage. The telemetry table specifically writes in bursts
(buffered ~30-60s, then one transaction, per this phase's schema) rather than one
row at a time. That write pattern is the crux of this decision, not "which SQLite
wrapper is nicest."

**Why are we using it?** Explicit control over `SqliteTransaction`/`SqliteCommand`
reuse matters for the buffered-flush pattern (720 samples/hour, batched), the
developer is already ADO.NET-fluent, and it's maintained by the same Microsoft
team that ships EF Core's SQLite provider, so its lifecycle tracks .NET's own,
not a solo maintainer's spare time.

**Alternatives considered:**

1. **`sqlite-net-pcl`**, the one Microsoft's *own MAUI documentation* actually
   recommends for MAUI apps. Attribute-mapped POCOs, minimal code for simple
   CRUD, lowest boilerplate of any option here. But it's a thin reflection-based
   ORM: bulk-insert/transaction control is less explicit than raw ADO.NET.
   Looping `Insert()` through the ORM for a buffered batch of dozens of samples
   is exactly the pattern that favors raw `SqliteCommand` reuse inside one
   transaction instead. This is the closest real alternative and a genuine
   judgement call, not an obvious win — everywhere in this app *except* the
   sample-table writes, `sqlite-net-pcl` would arguably be simpler.
2. **Raw `System.Data.SQLite`** (the older, non-Microsoft ADO.NET provider) — no
   advantage over `Microsoft.Data.Sqlite` for a new project, not
   Microsoft-maintained, documented mobile/MAUI packaging friction.
3. **LiteDB** (embedded document/NoSQL store) — zero-schema, native C# object
   serialization, no SQL to write. But this app's data is genuinely relational:
   one workout has many samples, and this phase's indexed
   `Workout(StartedAtUtc)` aggregate queries (Phase 10's daily/weekly/monthly
   sums) are exactly where SQL's `GROUP BY` beats a document store.

**Why not the alternatives?** `sqlite-net-pcl` loses specifically because of this
phase's buffered-transaction write pattern (task 6.3), which is the one place raw
transaction/command control earns its extra verbosity. Raw `System.Data.SQLite`
rejected for having no upside and a live mobile-packaging issue. LiteDB rejected
because the domain is relational and Phase 10's aggregates need SQL, not a
document query language.

**Long-term considerations.** Microsoft-maintained, tracks .NET's release
cadence, lowest abandonment risk of the four. Swapping to `sqlite-net-pcl` later
is moderate cost (repository classes rewritten, but the schema below stays valid
either way, since it's plain SQLite under both). Lowest per-row overhead of the
options, which matters at 720 rows/hour compounding over years. The cost paid is
more typing per query (raw SQL vs. ORM), but that SQL is also directly runnable
in any SQLite browser to inspect the `.db` file by hand, which this project needs
anyway for debugging and the endurance-testing phase.

---

## The two decisions that matter

- **`Workout.Id`**: `INTEGER PRIMARY KEY AUTOINCREMENT`. Local, physical, used for all
  foreign keys and joins.
- **`Workout.WorkoutId`**: `TEXT` GUID, `UNIQUE`, generated at workout creation. The
  portable identity used by backup/restore.
  **Integer PKs must never be used for de-duplication across devices.** The same
  integer means different workouts on two phones; de-duplicating on it silently drops
  unrelated workouts. This is the single most likely cause of quiet data loss here.
- **`WorkoutSample`**: telemetry at a **fixed 5-second cadence** (~720 rows/hour,
  ~9 MB/year at 250 workouts/year). Fixed beats on-change because heart rate varies
  continuously, so on-change degenerates to per-sample anyway while adding
  interpolation logic on the read side.

---

## Schema reference

SQLite, accessed via `Microsoft.Data.Sqlite`. Migration-based, forward-only.

### Design decisions

**Dual keys on `Workout`.** `Id` (`INTEGER PRIMARY KEY AUTOINCREMENT`) is the
local physical key, used for all foreign keys and joins. `WorkoutId` (`TEXT
UNIQUE`, a GUID) is the portable business key, used only at backup/restore
boundaries. `WorkoutSample` joins on the workout thousands of rows at a time;
joining on a 36-character TEXT GUID instead of a rowid is measurably slower and
larger. The GUID is only needed when data crosses devices. **Integer PKs must
never be used for de-duplication across devices.** The same integer means
different workouts on two phones. De-duplicating on it silently drops unrelated
workouts — the single most likely cause of silent data loss in this project.

**Telemetry cadence: fixed 5 seconds.**

| Cadence | Rows / 1 h | Storage / workout | 250 workouts/yr |
|---------|-----------|-------------------|-----------------|
| 1 Hz | 3,600 | ~180 KB | ~45 MB/yr |
| **5 s** | **720** | **~36 KB** | **~9 MB/yr** |
| On-change | ~5–20 | ~1 KB | ~0.3 MB/yr |

On-change is tempting because treadmill speed is a step function, but heart rate
varies continuously, so on-change degenerates back to per-sample for HR while
adding interpolation logic on the read side. Fixed cadence is simpler to write,
simpler to chart, and 9 MB/year is irrelevant on a phone.

Samples are the part you'll regret losing. A summary you could approximately
reconstruct from memory; a speed curve you cannot.

*This is a judgement call, not a settled fact — revisit if Phase 10's per-workout
speed curves look too coarse in practice.*

**All times are UTC.** `StartedAtUtc` and `EndedAtUtc` are UTC.
`StartOffsetMinutes` stores the local UTC offset at the time of the workout.
Local-time-without-offset shifts every daily and weekly bucket when a backup is
restored in another timezone, or across a DST boundary. Store UTC + offset;
compute the local day from both.

**All measurements are metric.** Distance in metres, speed in km/h, energy in
kcal. **Unit conversion happens at display time only and never touches the
database.** An imperial toggle that writes converted values corrupts the dataset
permanently.

**Derived data is never stored.** Statistics and personal records (Phase 10) are
computed from `Workout` and `WorkoutSample` on demand. They are not columns, not
tables, and not exported in backups. If a query becomes slow at scale, add a
cache table that is explicitly rebuildable and rebuild it after any import, but
do not start there.

### Schema

**`Workout`**

```sql
CREATE TABLE Workout (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    WorkoutId           TEXT    NOT NULL,           -- GUID, portable identity
    StartedAtUtc        TEXT    NOT NULL,           -- ISO 8601, UTC
    EndedAtUtc          TEXT    NULL,               -- NULL while in progress
    StartOffsetMinutes  INTEGER NOT NULL,           -- local UTC offset at start
    DurationSeconds     INTEGER NOT NULL DEFAULT 0, -- active time, excludes pauses
    DistanceMeters      REAL    NOT NULL DEFAULT 0,
    Calories            INTEGER NOT NULL DEFAULT 0,
    AvgSpeedKph         REAL    NOT NULL DEFAULT 0,
    MaxSpeedKph         REAL    NOT NULL DEFAULT 0,
    AvgHeartRate        INTEGER NULL,
    MaxHeartRate        INTEGER NULL,
    Status              INTEGER NOT NULL,           -- 0=InProgress 1=Completed 2=Abandoned
    DeviceId            INTEGER NULL REFERENCES Device(Id),
    Notes               TEXT    NULL,
    CreatedAtUtc        TEXT    NOT NULL,
    UpdatedAtUtc        TEXT    NOT NULL
);

CREATE UNIQUE INDEX UX_Workout_WorkoutId ON Workout(WorkoutId);
CREATE INDEX IX_Workout_StartedAtUtc     ON Workout(StartedAtUtc);
CREATE INDEX IX_Workout_Status           ON Workout(Status) WHERE Status = 0;
```

`Status = 0` (in progress) is written at workout **start**, not at the end. A crash
therefore leaves a recoverable partial workout rather than nothing. The partial index
on `Status = 0` makes the startup recovery query trivial.

`DurationSeconds` is **active** time. If the treadmill's elapsed-time counter includes
paused time, the app must track active time itself.

**`WorkoutSample`**

```sql
CREATE TABLE WorkoutSample (
    WorkoutRowId  INTEGER NOT NULL REFERENCES Workout(Id) ON DELETE CASCADE,
    ElapsedSec    INTEGER NOT NULL,
    SpeedKph      REAL    NULL,
    DistanceM     REAL    NULL,
    Calories      INTEGER NULL,
    HeartRate     INTEGER NULL,
    Flags         INTEGER NOT NULL DEFAULT 0,  -- bit 0 = connection gap marker
    PRIMARY KEY (WorkoutRowId, ElapsedSec)
) WITHOUT ROWID;
```

`WITHOUT ROWID` suits this table: the composite PK is the natural access path and it
removes the redundant rowid index.

`Flags` bit 0 marks a **connection gap**. When the link drops mid-workout, write a gap
marker rather than interpolating. Charts must show a break, not a fabricated straight
line.

**`HeartRate` source:** prefer `0x2A37` from the standard Heart Rate Service
`180D` over the FTMS field: a dedicated characteristic is less likely to be
mangled by a vendor shim (see `../phase-01-protocol-decode/README.md`'s FTMS
protocol reference). Store `NULL` when the user is not gripping the sensors; do
not carry the last value forward, or the average will be computed over
fabricated data.

If the probe finds handgrip readings unusable, keep writing the column but hide the
metric in the UI. Recording a column you don't display is cheap and reversible;
back-filling one you never recorded is not.

`ON DELETE CASCADE` requires `PRAGMA foreign_keys = ON` on every connection.
`Microsoft.Data.Sqlite` does not enable it by default.

**`Device`**

```sql
CREATE TABLE Device (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    DeviceUid     TEXT    NOT NULL,   -- GUID, portable identity for backup
    MacAddress    TEXT    NOT NULL,
    Name          TEXT    NOT NULL,
    LastSeenUtc   TEXT    NULL,
    IsPreferred   INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc  TEXT    NOT NULL
);

CREATE UNIQUE INDEX UX_Device_DeviceUid  ON Device(DeviceUid);
CREATE UNIQUE INDEX UX_Device_MacAddress ON Device(MacAddress);
```

A restored device row will not auto-connect on a new phone: the MAC transfers but the
bond does not. Tell the user they need to reconnect once.

If `../phase-00-probe-app/PHASE-00-FINDINGS.md` found the device uses a **random
resolvable address**, the MAC is not stable and `UX_Device_MacAddress` is wrong.
Match on name instead. Resolve before implementing.

**`SchemaVersion`**

```sql
CREATE TABLE SchemaVersion (
    Version    INTEGER NOT NULL PRIMARY KEY,
    AppliedUtc TEXT    NOT NULL
);
```

Forward-only migrations applied at startup inside a transaction. Never edit a shipped
migration; add a new one.

### Connection configuration

```sql
PRAGMA journal_mode = WAL;      -- concurrent read during sample writes
PRAGMA foreign_keys = ON;       -- required for CASCADE
PRAGMA synchronous = NORMAL;    -- safe with WAL, much faster writes
PRAGMA busy_timeout = 5000;
```

WAL matters here: the foreground service (Phase 07) writes samples while the UI reads
for charts (Phase 10).

### Sample write strategy

**Buffer in memory, flush every 30–60 seconds in a single transaction.**

Do not insert per sample. 720 individual transactions per workout is needless battery
and flash wear for no benefit. Worst-case crash loses under a minute of telemetry.

Flush on: the timer, workout pause, workout finish, and `OnSleep`.

```csharp
using var tx = connection.BeginTransaction();
using var cmd = connection.CreateCommand();
cmd.CommandText = """
    INSERT OR REPLACE INTO WorkoutSample
        (WorkoutRowId, ElapsedSec, SpeedKph, DistanceM, Calories, HeartRate, Flags)
    VALUES ($w, $e, $s, $d, $c, $h, $f)
    """;
// add parameters once, reassign values per row, ExecuteNonQuery per row
tx.Commit();
```

Reusing one prepared command with reassigned parameters is significantly faster than
building a command per row. `INSERT OR REPLACE` makes the flush idempotent, so a
retried flush after a partial failure is safe.

**Target: one hour of samples (720 rows) inserts in under 1 second.** If it doesn't,
the parameter reuse is probably wrong.

### Query patterns

Aggregate in SQL, not in C# over a full table read.

```sql
-- Weekly totals, local-day correct via stored offset
SELECT
    date(datetime(StartedAtUtc, '+' || StartOffsetMinutes || ' minutes')) AS LocalDay,
    COUNT(*)             AS Workouts,
    SUM(DistanceMeters)  AS TotalMeters,
    SUM(Calories)        AS TotalCalories,
    SUM(DurationSeconds) AS TotalSeconds
FROM Workout
WHERE Status = 1
  AND StartedAtUtc >= $fromUtc
  AND StartedAtUtc <  $toUtc
GROUP BY LocalDay
ORDER BY LocalDay;
```

Every aggregate filters on `StartedAtUtc`, hence the index.

**Downsample sample series for display.** A 2-hour workout is 1,440 points; a 400 px
chart needs perhaps 200. Downsample in SQL (`WHERE ElapsedSec % $n = 0`) rather than
loading everything and thinning it in C#. Phase 10 uses this pattern directly.

### Performance targets

| Operation | Target | Dataset |
|-----------|--------|---------|
| Sample flush (60 rows) | < 50 ms | any |
| Full workout sample insert (720 rows) | < 1 s | any |
| Workout list page (50 rows) | < 100 ms | 5,000 workouts |
| Weekly aggregate | < 200 ms | 5,000 workouts |
| Single workout chart load | < 200 ms | 2-hour workout |

Seed a synthetic 5,000-workout database in Phase 12 and measure. Do not assume.

### Backup mapping

`WorkoutId` and `DeviceUid` are the identities used in backup files (Phase 09).
Integer `Id` values are **not exported** and are reassigned on import.

Exported: `Workout`, `WorkoutSample`, `Device`, plus settings from `Preferences`.
Not exported: `SchemaVersion`, statistics, personal records (all derived or local).

---

## Your tasks

### 6.1 — Write the real schema as migration 1

Creates: `src/MyHi.Companion.Core/Data/Migrations.cs`.
Touches: `src/MyHi.Companion/Data/AppDatabase.cs`.

`MigrationRunner` already exists and already works. Your job is to give it a real
migration list instead of an empty one.

Concrete steps:
1. Create `src/MyHi.Companion.Core/Data/Migrations.cs`.
2. Copy the `Workout`, `WorkoutSample`, and `Device` `CREATE TABLE` statements, and
   their indexes, **verbatim** from the "Schema" section above into one SQL
   string. That section is the source of truth for the schema; this migration is
   only the mechanism that applies it. Do **not** include the `SchemaVersion`
   table itself. `MigrationRunner` already creates that unconditionally, before
   it even looks at which migrations are pending.
3. Wrap it as:
   ```csharp
   namespace MyHi.Companion.Core.Data;

   public static class Migrations
   {
       public static readonly IReadOnlyList<Migration> All =
       [
           new Migration(1, /* the CREATE TABLE / CREATE INDEX SQL, as one string */),
       ];
   }
   ```
4. Open `src/MyHi.Companion/Data/AppDatabase.cs` and change
   `new MigrationRunner([]).Apply(connection);` to
   `new MigrationRunner(Migrations.All).Apply(connection);`.
5. Build `Core` standalone to confirm it compiles with nothing MAUI-flavoured leaking
   in:
   ```powershell
   dotnet build src/MyHi.Companion.Core/MyHi.Companion.Core.csproj
   ```
   Zero errors, zero warnings.

Then prove it, following `MigrationRunnerTests.cs`'s exact pattern (temp directory,
`SqliteConnectionFactory`, `MigrationRunner`):

6. Create `src/MyHi.Companion.Tests/Data/WorkoutSchemaMigrationTests.cs`.
7. `[Fact]`: apply `Migrations.All` to a fresh temp database, query
   `sqlite_master` for `type = 'table'`, and assert `Workout`, `WorkoutSample`, and
   `Device` are all present.
8. `[Fact]`: insert a `Workout` row, insert a `WorkoutSample` row referencing it,
   delete the `Workout` row, then assert the `WorkoutSample` row is gone too. This is
   the test that actually proves `ON DELETE CASCADE` works, which only happens
   because `SqliteConnectionFactory` already turns `PRAGMA foreign_keys` on. See
   `Foreign_keys_pragma_is_enabled_so_cascade_delete_works` in
   `MigrationRunnerTests.cs` for the same assertion style, one layer down.
9. Run `dotnet test src/MyHi.Companion.Tests`. All green, including every Phase 00
   test (regression).

### 6.2 — `WorkoutRepository`: header-first writes and crash recovery

Creates: `src/MyHi.Companion.Core/Data/WorkoutRepository.cs`.

This class owns the `Workout` row's lifecycle: create it the moment a workout starts
(`Status = 0`), update it as the workout progresses, and find any left in-progress by
a previous run that never reached a clean finish. It does **not** own samples;
that's 6.3.

The shape:

```csharp
namespace MyHi.Companion.Core.Data;

public sealed record InProgressWorkout(long Id, string WorkoutId, DateTimeOffset StartedAtUtc);

public sealed record WorkoutSummary(
    DateTimeOffset EndedAtUtc, int DurationSeconds, double DistanceMeters,
    int Calories, double AvgSpeedKph, double MaxSpeedKph,
    int? AvgHeartRate, int? MaxHeartRate);

public sealed class WorkoutRepository(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// Writes the header row with Status = 0 (in progress) and returns the new
    /// physical Id. Called at workout START, not at the end — see the "Schema" section
    /// above's note on why: a crash then leaves a recoverable partial workout, not nothing.
    /// </summary>
    public long StartWorkout(DateTimeOffset startedAtUtc, int startOffsetMinutes, long? deviceId)
    {
        // TODO: INSERT INTO Workout (WorkoutId, StartedAtUtc, StartOffsetMinutes,
        // Status, CreatedAtUtc, UpdatedAtUtc, DeviceId) VALUES (...);
        // WorkoutId = Guid.NewGuid().ToString() — the portable identity, minted
        // exactly once, here. Return the physical Id via SqliteConnection.LastInsertRowId.
    }

    /// <summary>Status = 0 rows found at startup — a previous run never reached Finished.</summary>
    public IReadOnlyList<InProgressWorkout> FindInProgressWorkouts()
    {
        // TODO: SELECT Id, WorkoutId, StartedAtUtc FROM Workout WHERE Status = 0;
        // the partial index from 6.1 makes this cheap even at scale.
    }

    public void CompleteWorkout(long id, WorkoutSummary summary)
    {
        // TODO: UPDATE Workout SET Status = 1, EndedAtUtc = $e, DurationSeconds = $d,
        // DistanceMeters = $dm, Calories = $c, AvgSpeedKph = $avg, MaxSpeedKph = $max,
        // AvgHeartRate = $ahr, MaxHeartRate = $mhr, UpdatedAtUtc = $now WHERE Id = $id;
    }

    public void AbandonWorkout(long id)
    {
        // TODO: UPDATE Workout SET Status = 2, UpdatedAtUtc = $now WHERE Id = $id;
    }
}
```

Concrete steps:
1. Create the file with the shape above; fill in the four method bodies.
2. Every `INSERT`/`UPDATE` needs `CreatedAtUtc`/`UpdatedAtUtc` set explicitly from
   `DateTimeOffset.UtcNow`. SQLite has nothing here that stamps timestamps for you.
3. `StartWorkout` is the **only** place `WorkoutId` (the GUID) is minted. Nowhere else
   in the app should ever generate a new one for the same logical workout.
4. Trace through, and write a one-line comment answering: what does
   `FindInProgressWorkouts` return when the app starts normally after a clean
   shutdown? It should be an empty list. Say why `CompleteWorkout` guarantees that.

### 6.3 — `WorkoutSampleBuffer`: buffer in memory, flush in one transaction

Creates: `src/MyHi.Companion.Core/Data/WorkoutSampleBuffer.cs`.

The "Sample write strategy" section above already shows the SQL shape. Your
job is to wrap it in a class a caller can `Add()` to constantly and `Flush()`
occasionally, without the caller needing to know a transaction is involved.

```csharp
using Microsoft.Data.Sqlite;

namespace MyHi.Companion.Core.Data;

public readonly record struct WorkoutSampleRow(
    int ElapsedSec, double? SpeedKph, double? DistanceM,
    int? Calories, int? HeartRate, byte Flags);

public sealed class WorkoutSampleBuffer
{
    private readonly List<WorkoutSampleRow> _pending = [];

    public int PendingCount => _pending.Count;

    public void Add(WorkoutSampleRow sample) => _pending.Add(sample);

    /// <summary>
    /// Writes every buffered sample for one workout in a single transaction and
    /// clears the buffer. Idempotent: flushing the same rows twice (e.g. a retry
    /// after a partial failure) must not duplicate or corrupt data.
    /// </summary>
    public int Flush(SqliteConnection connection, long workoutRowId)
    {
        if (_pending.Count == 0) return 0;

        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            INSERT OR REPLACE INTO WorkoutSample
                (WorkoutRowId, ElapsedSec, SpeedKph, DistanceM, Calories, HeartRate, Flags)
            VALUES ($w, $e, $s, $d, $c, $h, $f);
            """;
        // TODO: add all seven parameters ONCE here (cmd.Parameters.Add("$w", ...),
        // etc.) with placeholder values, then inside the loop below only reassign
        // .Value on each — do not rebuild CommandText or call CreateCommand() per
        // row, that's the mistake the "Sample write strategy" section above warns is
        // "significantly slower," and almost always why the 720-row throughput target
        // gets missed.

        foreach (var sample in _pending)
        {
            // TODO: reassign every parameter's .Value from `sample` and
            // `workoutRowId`, then cmd.ExecuteNonQuery();
            // A nullable field (e.g. HeartRate == null) needs DBNull.Value, not C#
            // null, or Microsoft.Data.Sqlite throws.
        }

        transaction.Commit();
        var flushed = _pending.Count;
        _pending.Clear();
        return flushed;
    }
}
```

Concrete steps:
1. Create the file with the shape above.
2. Fill in the parameter setup and the loop body.
3. Handle every nullable column explicitly, e.g.
   `sample.HeartRate is int hr ? hr : DBNull.Value`.
4. Create `src/MyHi.Companion.Tests/Data/WorkoutSampleBufferTests.cs`:
   - Add 60 samples, flush, assert 60 rows exist and `PendingCount` is back to 0.
   - Flush an empty buffer: assert it returns 0 and doesn't open a transaction
     needlessly.
   - **Idempotency test**: flush the same rows twice (re-`Add` them between flushes)
     and assert the row count is unchanged, not doubled. This is what
     `INSERT OR REPLACE` buys you, worth proving rather than trusting.
   - **Throughput test**: build 720 samples, flush, assert it completes in under
     1 second, the acceptance bar from the "Performance targets" section above and
     the bottom of this file. If it's slow, the most likely cause is rebuilding the
     command per row instead of reusing it (see the TODO above).

### 6.4 — Startup recovery

Touches: wherever `AppDatabase.Initialize()` is currently called at startup, likely
`MauiProgram.cs` or `App.xaml.cs`.

`WorkoutRepository.FindInProgressWorkouts()` (6.2) gives you the list; deciding what
happens to each row is a real design decision, not a mechanical step:

- **Simplest defensible policy:** call `AbandonWorkout(id)` on every row found at
  startup. Whatever `WorkoutSample` rows already made it to disk remain as the
  recorded (but incomplete) history of that session.
- **More ambitious policy:** compute a summary from the samples that did make it to
  disk, and `CompleteWorkout` with that summary instead of abandoning it.

Concrete steps:
1. Pick one (either is acceptable; the Tests section below only requires that *a*
   policy exists and runs).
2. Call `FindInProgressWorkouts()` once, early in startup, right after
   `AppDatabase.Initialize()`.
3. Apply your chosen policy to every row returned.
4. Write a one-line comment recording which policy you picked and why, the same
   "make the call, then say why" pattern Phase 01b used for the fake service's
   Start-preserves-target-speed decision.

### 6.5 — Wire the flush triggers

Touches: `src/MyHi.Companion/App.xaml.cs`, plus wherever your Phase 04 `WorkoutEngine`
exposes its state-change events.

Four triggers must call `WorkoutSampleBuffer.Flush()`: a periodic timer, workout
pause, workout finish, and `OnSleep`.

Concrete steps:
1. In whichever class owns the buffer during an active workout, start a
   `System.Threading.Timer` when the workout becomes `Active`, firing roughly every
   30–60 s and calling `Flush()` on each tick. Dispose it when the workout finishes.
2. Subscribe to your Phase 04 engine's pause/finish transitions and call `Flush()`
   from those handlers too. A flush that only happens on a timer loses up to a full
   interval's worth of samples if the user stops right after a tick.
3. Open `App.xaml.cs` and add:
   ```csharp
   protected override void OnSleep()
   {
       // TODO: if a workout is active, flush its buffer synchronously here.
       // OnSleep must not block for long — this is a best-effort flush of whatever
       // is already buffered, not a place to do anything expensive.
   }
   ```
4. Manually verify: start a fake workout, background the app (press Home), and check
   the database file directly (or a debug log) to confirm a flush happened.

### 6.6 — Counter semantics: agree on the shape, not the logic (blocked)

Per `../phase-00-probe-app/PHASE-00-FINDINGS.md` V1, this is still **open**. Do not
write the real re-baselining logic until it resolves. Guessing wrong here makes
every stored workout wrong, silently, forever. What you can do now is agree on the
shape so that the moment V1 resolves, filling it in is mechanical rather than a
redesign:

| V1 verdict | What the engine does |
|------------|----------------------|
| **Per-session** | Record reported values directly |
| **Cumulative** | Every workout value is a delta against the value captured at workout start; detect a mid-workout counter reset (value decreases) and re-baseline |

```csharp
namespace MyHi.Companion.Core.Data;

/// <summary>
/// Placeholder shape only — do not fill in reset-detection logic until
/// PHASE-00-FINDINGS.md V1 (counter reset semantics) is resolved. If V1 turns out
/// per-session, most of this class disappears; if cumulative, the TODO becomes real.
/// </summary>
public sealed class CounterBaseline
{
    private double? _baselineDistanceMeters;
    private double? _lastReportedDistanceMeters;

    public double? ToWorkoutDistance(double reportedDistanceMeters)
    {
        // TODO once V1 resolves:
        // - per-session: return reportedDistanceMeters unchanged, this class is unused
        // - cumulative: baseline = first reading; return reported - baseline;
        //   detect reportedDistanceMeters < lastReportedDistanceMeters (a reset) and
        //   re-baseline from that point
        throw new NotImplementedException("Blocked on PHASE-00-FINDINGS.md V1 — counter reset semantics.");
    }
}
```

Whatever V1 said. Per-session → record reported values directly. Cumulative → delta
against a workout-start baseline, with mid-workout reset detection. This is the reason
the phase is blocked on V1 rather than merely informed by it.

### Review checkpoint

Before this phase is marked done: the agent reviews `WorkoutRepository.cs` and
`WorkoutSampleBuffer.cs` against the "Schema reference" section above line by line, every PRAGMA, every
index, every nullable-vs-`DBNull` edge case. Then run the throughput test together and
actually look at the number, not just the pass/fail.

---

## Implementation requirements

- **Buffer samples in memory; flush every 30–60 s inside a single transaction.** Do not
  insert per sample. 720 individual transactions per workout is needless battery and
  flash wear. Worst-case crash loses under a minute. (Task 6.3, 6.5)
- Flush on: the timer, workout pause, workout finish, and `OnSleep`. (Task 6.5)
- Reuse one prepared command with reassigned parameters. Building a command per row is
  significantly slower, and if the 720-row target is missed this is almost always why.
  (Task 6.3)
- `INSERT OR REPLACE` makes a flush idempotent, so a retried flush after partial
  failure is safe. (Task 6.3)
- **Write the workout header at workout start** with `Status = 0` (in progress), not at
  the end. A crash then leaves a recoverable partial workout rather than nothing.
  (Task 6.2)
- On app start, detect `Status = 0` rows and either recover or discard per policy. The
  partial index on `Status = 0` makes this query trivial. (Task 6.4)
- `PRAGMA foreign_keys = ON` on **every** connection. `Microsoft.Data.Sqlite` does not
  enable it by default and `ON DELETE CASCADE` silently does nothing without it.
  Already handled by `SqliteConnectionFactory` (Phase 00); task 6.1's test proves it.
- **Metric only.** Distance in metres, speed in km/h, energy in kcal. Unit conversion
  happens at display time and never touches the database.
- **UTC plus `StartOffsetMinutes`.** Local-time-without-offset shifts every daily and
  weekly bucket when restored in another timezone, or just across a DST boundary.
- **Heart rate: store `NULL` when the user is not gripping.** Do not carry the last
  value forward, or every average is computed over fabricated data. If V3 said
  marginal, keep writing the column and hide the metric. Recording a column you don't
  display is cheap and reversible; back-filling one you never recorded is not.
- **Gap markers**: `Flags` bit 0, written by the Phase 04 engine on connection loss.

## Counter semantics

See task 6.6 above for the shape to build without guessing the logic. Once
`../phase-00-probe-app/PHASE-00-FINDINGS.md` V1 resolves: per-session → record
directly; cumulative → delta against a workout-start baseline, with mid-workout
reset detection.

---

## Tests

- 10 workouts via `FakeTreadmillService`, restart, all present with correct sample counts
- Kill the app mid-workout; on restart the partial workout is handled per policy (task 6.4)
- **Throughput: 1 hour of samples (720 rows) inserts in under 1 s** (task 6.3)
- Flush of 60 rows under 50 ms (task 6.3)
- Idempotent flush: run the same flush twice, row count unchanged (task 6.3)
- Cascade delete removes samples (proves `foreign_keys` is actually on) (task 6.1)
- Timezone: a workout at 23:30 local stores UTC + offset and reconstructs the right
  local day
- `[HUMAN]` One real 20-minute workout; summary matches the treadmill console

## Acceptance

- [ ] No data loss across app restart
- [ ] Sample cadence correct within ±1 s
- [ ] 720-row insert under 1 s
- [ ] No derived value (statistic, PR) stored anywhere
