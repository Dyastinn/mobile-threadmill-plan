# Phase 06 — Workout Recording & Schema

**Hardware:** none for development · **Size:** M · **Blocked by:** Phase 04
**Hard dependency:** V1 counter semantics (`../../ASSUMPTIONS.md` A1). **Do not start
without it.**

---

## Goal

Persist workouts and telemetry durably. Full schema in `../../14-Database.md` — read it
before writing a migration.

**Phase 00 already built the plumbing this phase writes real data through.** Open all
three of these now, before writing anything new:

- `src/MyHi.Companion.Core/Data/SqliteConnectionFactory.cs` — opens connections and
  applies the required PRAGMAs (`journal_mode=WAL`, `foreign_keys=ON`,
  `synchronous=NORMAL`, `busy_timeout=5000`) on every one. You never call this
  yourself.
- `src/MyHi.Companion.Core/Data/MigrationRunner.cs` — applies a list of
  `Migration(Version, Sql)` records in order, once each, inside a transaction,
  tracked in a `SchemaVersion` table it creates itself. Already has its own passing
  tests (`src/MyHi.Companion.Tests/Data/MigrationRunnerTests.cs`) — read them, they're
  the template for the tests you'll write in this phase.
- `src/MyHi.Companion/Data/AppDatabase.cs` — calls the runner with an **empty**
  migration list today (`new MigrationRunner([])`), which only guarantees the
  database file and `SchemaVersion` table exist. Giving it the real schema is task 6.1.

This means the phase is narrower than "build a database layer from scratch": write
the real schema as migration 1, then build the two classes that read and write
through it correctly — a workout header repository and a buffered sample writer.

### Understanding what you're building (read this before the tasks)

**The everyday problem.** Imagine journaling every five seconds and, the moment
you finish writing, walking outside to drop that one entry in the mailbox — then
walking back, sitting down, writing the next entry, and walking out again. Over an
hour that's 720 round trips. Almost none of that effort is the writing; it's the
walk — opening the door, going down the path, coming back. The obvious fix: keep a
notepad on your desk, jot each entry there as it happens, and carry the whole stack
out to the mailbox once every 30–60 seconds. Same entries, one trip per batch
instead of one trip per entry. That's exactly what `WorkoutSampleBuffer` does.
`Add()` is the notepad — an in-memory `List<WorkoutSampleRow>`, no I/O at all.
`Flush()` is the walk to the mailbox — opening a `SqliteTransaction`, writing every
buffered row, committing once. The "walk" (opening a transaction, waiting on the
disk to durably commit it) is the expensive part, not the "writing" (appending a
struct to a `List`) — same as the postal analogy, where the trip costs more than
the sentence.

**Why not just insert every sample as it arrives.** The simplest-sounding
alternative is to skip the buffer entirely: when `ITreadmillService.SampleReceived`
fires, immediately `INSERT` that one row and commit. No buffer class, no `Flush()`,
no timer to wire up in task 6.5 — genuinely less code. At the fixed 5-second
cadence this project uses, that's 720 separate open-a-transaction-and-commit
cycles per hour of workout, every workout, forever — real battery drain and real
flash wear, paid whether or not a crash ever happens. What does that cost buy you?
Almost nothing: if the app crashes mid-workout, the buffered approach loses at
most one flush interval — under a minute of telemetry, per the 30–60 s interval
task 6.5 wires up — which is already the acceptance bar in this phase's
Implementation requirements. Writing per-sample doesn't make that bound
meaningfully better; it just pays for insurance against a loss that's already
small and already tolerated. Batching earns its complexity here because the
guaranteed cost (720 commits/hour) is real and the naive approach's only
advantage (marginally less data lost in a rare crash) isn't. Contrast this with
`WorkoutRepository.StartWorkout`/`CompleteWorkout`: those write directly,
unbuffered, on purpose — a workout header is written twice per workout, not 720
times, so there's no "720 trips" problem to solve and adding a buffer there would
be pure overhead with nothing to show for it.

**The pattern, named plainly.** This is **batching writes behind an in-memory
buffer with an explicit commit boundary** — sometimes called write-behind
buffering. The cost: a caller (`WorkoutSampleBuffer`) now has state that can be
lost (whatever's in `_pending` when the process dies), and a flush must be
idempotent (hence `INSERT OR REPLACE` — flushing the same rows twice, e.g. after a
retry, must not duplicate rows) since you can no longer assume every write happens
exactly once. The payoff, specific to this project: roughly 60–120 commits per
hour instead of 720 — a 6–12x reduction — while keeping the transaction as the
atomicity boundary, so a flush either fully lands or fully doesn't, never a
half-written batch. Two more decisions in this phase lean on the same
cost-vs-payoff reasoning, worth naming briefly rather than treating as arbitrary:
`Workout` carries **two** identity columns — `Id` (integer, autoincrement, used
for every foreign key and join) and `WorkoutId` (a GUID, minted once in
`StartWorkout`) — because they serve different audiences. `Id` only has to be
unique on *this* phone; `WorkoutId` has to be unique across *every* phone that
might ever restore a backup, and an autoincrement integer can't promise that (two
phones will both mint `Id = 1` for their first workout). One column doing both
jobs would silently merge unrelated workouts during a restore — the extra column
is the cost, cross-device correctness is the payoff. And writing the `Workout` row
with `Status = 0` at workout **start**, not after it finishes, is the same
fail-fast instinct as Phase 01a's length validation: pay a small cost upfront (a
row exists before you know the workout succeeded) so a crash mid-workout leaves a
recoverable, flaggable row instead of nothing at all.

## Learning goals

- ADO.NET in `Microsoft.Data.Sqlite`: `SqliteCommand`, parameterized queries,
  `SqliteTransaction` — the same shape you'd use against any other .NET data
  provider later (SQL Server, Postgres, ...), so this transfers
- Why buffering writes and flushing in one transaction beats one transaction per row —
  both for correctness (atomicity: a flush either fully happens or fully doesn't) and
  for real hardware constraints (flash wear, battery)
- The forward-only migration pattern: a `SchemaVersion` table, a list of
  `(Version, Sql)` pairs applied once each, never edited after being shipped — add a
  new migration instead
- `WITHOUT ROWID` tables and composite primary keys — when SQLite's default hidden
  rowid is redundant and worth explicitly dropping
- The crash-recovery pattern: write state **before** an operation completes
  (`Status = 0`), not after, so a crash mid-operation is detectable and recoverable
  instead of silently losing data
- Portable identity vs. physical primary key — why `Workout` has two different kinds
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

## The two decisions that matter

- **`Workout.Id`** — `INTEGER PRIMARY KEY AUTOINCREMENT`. Local, physical, used for all
  foreign keys and joins.
- **`Workout.WorkoutId`** — `TEXT` GUID, `UNIQUE`, generated at workout creation. The
  portable identity used by backup/restore.
  **Integer PKs must never be used for de-duplication across devices.** The same
  integer means different workouts on two phones; de-duplicating on it silently drops
  unrelated workouts. This is the single most likely cause of quiet data loss here.
- **`WorkoutSample`** — telemetry at a **fixed 5-second cadence** (~720 rows/hour,
  ~9 MB/year at 250 workouts/year). Fixed beats on-change because heart rate varies
  continuously, so on-change degenerates to per-sample anyway while adding
  interpolation logic on the read side.

---

## Your tasks

### 6.1 — Write the real schema as migration 1

Creates: `src/MyHi.Companion.Core/Data/Migrations.cs`.
Touches: `src/MyHi.Companion/Data/AppDatabase.cs`.

`MigrationRunner` already exists and already works. Your job is to give it a real
migration list instead of an empty one.

Concrete steps:
1. Create `src/MyHi.Companion.Core/Data/Migrations.cs`.
2. Copy the `Workout`, `WorkoutSample`, and `Device` `CREATE TABLE` statements — and
   their indexes — **verbatim** from `../../14-Database.md`'s "Schema" section into
   one SQL string. That document is the source of truth for the schema; this
   migration is only the mechanism that applies it. Do **not** include the
   `SchemaVersion` table itself — `MigrationRunner` already creates that
   unconditionally, before it even looks at which migrations are pending.
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
7. `[Fact]` — apply `Migrations.All` to a fresh temp database, query
   `sqlite_master` for `type = 'table'`, and assert `Workout`, `WorkoutSample`, and
   `Device` are all present.
8. `[Fact]` — insert a `Workout` row, insert a `WorkoutSample` row referencing it,
   delete the `Workout` row, then assert the `WorkoutSample` row is gone too. This is
   the test that actually proves `ON DELETE CASCADE` works — which only happens
   because `SqliteConnectionFactory` already turns `PRAGMA foreign_keys` on. See
   `Foreign_keys_pragma_is_enabled_so_cascade_delete_works` in
   `MigrationRunnerTests.cs` for the same assertion style, one layer down.
9. Run `dotnet test src/MyHi.Companion.Tests` — all green, including every Phase 00
   test (regression).

### 6.2 — `WorkoutRepository`: header-first writes and crash recovery

Creates: `src/MyHi.Companion.Core/Data/WorkoutRepository.cs`.

This class owns the `Workout` row's lifecycle: create it the moment a workout starts
(`Status = 0`), update it as the workout progresses, and find any left in-progress by
a previous run that never reached a clean finish. It does **not** own samples —
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
    /// physical Id. Called at workout START, not at the end — see 14-Database.md's
    /// note on why: a crash then leaves a recoverable partial workout, not nothing.
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
   `DateTimeOffset.UtcNow` — SQLite has nothing here that stamps timestamps for you.
3. `StartWorkout` is the **only** place `WorkoutId` (the GUID) is minted. Nowhere else
   in the app should ever generate a new one for the same logical workout.
4. Trace through, and write a one-line comment answering: what does
   `FindInProgressWorkouts` return when the app starts normally after a clean
   shutdown? It should be an empty list — say why `CompleteWorkout` guarantees that.

### 6.3 — `WorkoutSampleBuffer`: buffer in memory, flush in one transaction

Creates: `src/MyHi.Companion.Core/Data/WorkoutSampleBuffer.cs`.

`14-Database.md`'s "Sample write strategy" section already shows the SQL shape — your
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
        // row, that's the mistake 14-Database.md warns is "significantly slower,"
        // and almost always why the 720-row throughput target gets missed.

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
     and assert the row count is unchanged, not doubled — this is what
     `INSERT OR REPLACE` buys you, worth proving rather than trusting.
   - **Throughput test**: build 720 samples, flush, assert it completes in under
     1 second — the acceptance bar from `14-Database.md` and the bottom of this file.
     If it's slow, the most likely cause is rebuilding the command per row instead of
     reusing it (see the TODO above).

### 6.4 — Startup recovery

Touches: wherever `AppDatabase.Initialize()` is currently called at startup — likely
`MauiProgram.cs` or `App.xaml.cs`.

`WorkoutRepository.FindInProgressWorkouts()` (6.2) gives you the list; deciding what
happens to each row is a real design decision, not a mechanical step:

- **Simplest defensible policy:** call `AbandonWorkout(id)` on every row found at
  startup. Whatever `WorkoutSample` rows already made it to disk remain as the
  recorded (but incomplete) history of that session.
- **More ambitious policy:** compute a summary from the samples that did make it to
  disk, and `CompleteWorkout` with that summary instead of abandoning it.

Concrete steps:
1. Pick one (either is acceptable — the Tests section below only requires that *a*
   policy exists and runs).
2. Call `FindInProgressWorkouts()` once, early in startup, right after
   `AppDatabase.Initialize()`.
3. Apply your chosen policy to every row returned.
4. Write a one-line comment recording which policy you picked and why — the same
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
   from those handlers too — a flush that only happens on a timer loses up to a full
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

Per `../../ASSUMPTIONS.md` A1, this is still **open**. Do not write the real
re-baselining logic until it resolves — guessing wrong here makes every stored
workout wrong, silently, forever. What you can do now is agree on the shape so that
the moment A1 resolves, filling it in is mechanical rather than a redesign:

| V1 verdict | What the engine does |
|------------|----------------------|
| **Per-session** | Record reported values directly |
| **Cumulative** | Every workout value is a delta against the value captured at workout start; detect a mid-workout counter reset (value decreases) and re-baseline |

```csharp
namespace MyHi.Companion.Core.Data;

/// <summary>
/// Placeholder shape only — do not fill in reset-detection logic until
/// ASSUMPTIONS.md A1 (counter reset semantics) is resolved. If V1 turns out
/// per-session, most of this class disappears; if cumulative, the TODO becomes real.
/// </summary>
public sealed class CounterBaseline
{
    private double? _baselineDistanceMeters;
    private double? _lastReportedDistanceMeters;

    public double? ToWorkoutDistance(double reportedDistanceMeters)
    {
        // TODO once A1 resolves:
        // - per-session: return reportedDistanceMeters unchanged, this class is unused
        // - cumulative: baseline = first reading; return reported - baseline;
        //   detect reportedDistanceMeters < lastReportedDistanceMeters (a reset) and
        //   re-baseline from that point
        throw new NotImplementedException("Blocked on ASSUMPTIONS.md A1 — counter reset semantics.");
    }
}
```

Whatever V1 said. Per-session → record reported values directly. Cumulative → delta
against a workout-start baseline, with mid-workout reset detection. This is the reason
the phase is blocked on V1 rather than merely informed by it.

### Review checkpoint

Before this phase is marked done: the agent reviews `WorkoutRepository.cs` and
`WorkoutSampleBuffer.cs` against `14-Database.md` line by line — every PRAGMA, every
index, every nullable-vs-`DBNull` edge case. Then run the throughput test together and
actually look at the number, not just the pass/fail.

---

## Implementation requirements

- **Buffer samples in memory; flush every 30–60 s inside a single transaction.** Do not
  insert per sample — 720 individual transactions per workout is needless battery and
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
- `PRAGMA foreign_keys = ON` on **every** connection — `Microsoft.Data.Sqlite` does not
  enable it by default and `ON DELETE CASCADE` silently does nothing without it.
  Already handled by `SqliteConnectionFactory` (Phase 00); task 6.1's test proves it.
- **Metric only.** Distance in metres, speed in km/h, energy in kcal. Unit conversion
  happens at display time and never touches the database.
- **UTC plus `StartOffsetMinutes`.** Local-time-without-offset shifts every daily and
  weekly bucket when restored in another timezone, or just across a DST boundary.
- **Heart rate: store `NULL` when the user is not gripping.** Do not carry the last
  value forward, or every average is computed over fabricated data. If V3 said
  marginal, keep writing the column and hide the metric — recording a column you don't
  display is cheap and reversible; back-filling one you never recorded is not.
- **Gap markers**: `Flags` bit 0, written by the Phase 04 engine on connection loss.

## Counter semantics

See task 6.6 above for the shape to build without guessing the logic. Once
`../../ASSUMPTIONS.md` A1 resolves: per-session → record directly; cumulative → delta
against a workout-start baseline, with mid-workout reset detection.

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
