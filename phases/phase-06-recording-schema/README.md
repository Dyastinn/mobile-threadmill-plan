# Phase 06 — Workout Recording & Schema

**Hardware:** none for development · **Size:** M · **Blocked by:** Phase 04
**Hard dependency:** V1 counter semantics
(`../phase-00-probe-app/PHASE-00-FINDINGS.md`). **Do not start without it.**

---

## Goal

Persist workouts and telemetry durably. Full schema in the "Schema reference"
section below. Read it before writing a migration.

**Phase 00 already built the plumbing this phase writes real data through.** Open all
of these now, before writing anything new:

- `src/core/data/db.ts`: the `Db` interface every repository talks to (`exec`, `run`,
  `all`, `first`, `prepare`, `transaction`). Six methods; this is the whole data API
  you'll use.
- `src/features/data/expoDb.ts` + `openAppDatabase.ts`: open the one `expo-sqlite`
  connection, apply the required PRAGMAs (`journal_mode=WAL`, `foreign_keys=ON`,
  `synchronous=NORMAL`, `busy_timeout=5000`), and serialise every query through one
  queue. You never call these yourself.
- `src/core/data/migrationRunner.ts`: applies a list of `{ version, sql }` migrations
  in order, once each, inside a transaction, tracked in a `SchemaVersion` table it
  creates itself. Already has its own passing tests
  (`migrationRunner.test.ts`). Read them, they're the template for the tests you'll
  write in this phase.
- `src/core/data/migrations.ts`: an **empty** list today, which only guarantees the
  database file and `SchemaVersion` table exist. Giving it the real schema is task 6.1.
- `src/core/data/testing/nodeSqliteDb.ts`: the same `Db` interface over Node's
  built-in SQLite, so every test in this phase runs real SQL in plain Jest.

This means the phase is narrower than "build a database layer from scratch": write
the real schema as migration 1, then build the classes that read and write through it
correctly: a workout header repository, a buffered sample writer, and the recorder
that connects them to Phase 04's engine.

### Understanding what you're building (read this before the tasks)

**The everyday problem.** Inserting every sample the moment it arrives means
opening a transaction, writing one row, and committing, 720 times an hour. Almost
none of that cost is the write itself; it's the transaction overhead around it.
The fix is to buffer samples in memory and commit them in batches every 30–60
seconds instead of one at a time. Same rows written, far fewer round trips to
disk. That's exactly what `WorkoutSampleBuffer` does. `add()` pushes onto an
in-memory array, no I/O at all. `flush()` opens a transaction, writes every buffered
row through one prepared statement, and commits once. Opening the transaction and
waiting for the disk to durably commit it is the expensive part, not pushing an
object onto an array.

**Why not just insert every sample as it arrives.** The simplest-sounding
alternative is to skip the buffer entirely: when a sample is recorded, immediately
`INSERT` that one row and commit. No buffer class, no `flush()`, no timer to wire up
in task 6.5, genuinely less code. At the fixed 5-second cadence this project uses,
that's 720 separate open-a-transaction-and-commit cycles per hour of workout, every
workout, forever. Real battery drain and real flash wear, paid whether or not a crash
ever happens.

What does that cost buy you? Almost nothing. If the app crashes mid-workout, the
buffered approach loses at most one flush interval (under a minute of telemetry,
per the 30–60 s interval task 6.5 wires up), which is already the acceptance bar
in this phase's Implementation requirements. Writing per-sample doesn't make that
bound meaningfully better; it just pays for insurance against a loss that's
already small and already tolerated. Batching earns its complexity here because
the guaranteed cost (720 commits/hour) is real and the naive approach's only
advantage (marginally less data lost in a rare crash) isn't.

Contrast this with `WorkoutRepository.startWorkout`/`completeWorkout`: those write
directly, unbuffered, on purpose. A workout header is written twice per workout,
not 720 times, so there's no "720 trips" problem to solve and adding a buffer
there would be pure overhead with nothing to show for it.

**The pattern, named plainly.** This is **batching writes behind an in-memory
buffer with an explicit commit boundary**, sometimes called write-behind
buffering. The cost: a caller (`WorkoutSampleBuffer`) now has state that can be
lost (whatever's in the pending array when the process dies), and a flush must be
idempotent (hence `INSERT OR REPLACE`: flushing the same rows twice, e.g. after a
retry, must not duplicate rows) since you can no longer assume every write happens
exactly once. The payoff, specific to this project: roughly 60–120 commits per
hour instead of 720, a 6–12x reduction, while keeping the transaction as the
atomicity boundary, so a flush either fully lands or fully doesn't, never a
half-written batch.

Two more decisions in this phase lean on the same cost-vs-payoff reasoning, worth
naming briefly rather than treating as arbitrary. `Workout` carries **two**
identity columns: `Id` (integer, autoincrement, used for every foreign key and
join) and `WorkoutId` (a GUID, minted once in `startWorkout`), because they serve
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

- Parameterised SQL through a small interface: named parameters (`$workoutId`),
  prepared statements, transactions. The same shape you'd use against any SQL
  database from any language later, so this transfers.
- Why buffering writes and flushing in one transaction beats one transaction per row,
  both for correctness (atomicity: a flush either fully happens or fully doesn't) and
  for real hardware constraints (flash wear, battery)
- The forward-only migration pattern: a `SchemaVersion` table, a list of
  `{ version, sql }` pairs applied once each, never edited after being shipped. Add a
  new migration instead
- `WITHOUT ROWID` tables and composite primary keys: when SQLite's default hidden
  rowid is redundant and worth explicitly dropping
- The crash-recovery pattern: write state **before** an operation completes
  (`Status = 0`), not after, so a crash mid-operation is detectable and recoverable
  instead of silently losing data
- Portable identity vs. physical primary key: why `Workout` has two different kinds
  of "ID" and what breaks (silently) if you conflate them across devices
- **Injecting what `core/` can't import**: a UUID generator and a clock, passed into
  the repository's constructor, so the same code runs on the phone and in Node tests

## Reference docs

| Topic | Link | Relevant to |
|---|---|---|
| `expo-sqlite` reference | https://docs.expo.dev/versions/latest/sdk/sqlite/ | What `expoDb.ts` wraps: `runAsync`, `prepareAsync`, `getAllAsync`. Read the "Transactions" notes to see why the project wraps them itself |
| `node:sqlite` | https://nodejs.org/api/sqlite.html | What the test adapter wraps; useful when a test fails and you want to see what SQLite actually got |
| SQLite Foreign Key Support | https://sqlite.org/foreignkeys.html | Why `PRAGMA foreign_keys = ON` is required before `ON DELETE CASCADE` does anything, and why it's per-connection |
| SQLite Write-Ahead Logging (WAL) | https://www.sqlite.org/wal.html | Why the connection sets `journal_mode = WAL`: faster commits and crash safety for the buffered writer |
| `WITHOUT ROWID` tables | https://www.sqlite.org/withoutrowid.html | Task 6.1, `WorkoutSample` |
| SQLite date and time functions | https://www.sqlite.org/lang_datefunc.html | The local-day query pattern below |
| `AppState` | https://reactnative.dev/docs/appstate | Task 6.5: flush when the app goes to the background |

## Technology decision: `expo-sqlite` (raw SQL, no ORM)

**What problem does it solve?** Workout history and per-workout telemetry need
durable local storage. The telemetry table specifically writes in bursts
(buffered ~30-60 s, then one transaction, per this phase's schema) rather than one
row at a time. That write pattern is the crux of this decision, not "which SQLite
wrapper is nicest."

**Why are we using it?** It's Expo's own first-party SQLite module, versioned with
the SDK (so an Expo upgrade never strands it), with prepared statements
(`prepareAsync` → reuse one statement for a whole batch) and explicit transactions,
which is exactly what the buffered flush needs. It needs no native setup beyond
`npx expo install`. Writing raw SQL means every query in this project can be pasted
straight into any SQLite browser to inspect the `.db` file by hand, which Phase 12 and
Phase 14 rely on.

**Alternatives considered:**

1. **Drizzle ORM** (on top of `expo-sqlite`). Pros: a TypeScript-typed schema, typed
   query results, generated migrations; the closest real alternative, and arguably
   nicer everywhere *except* the sample-table writes. Cons: a second layer to learn
   between you and SQL, a migration generator to trust, and batch inserts are less
   explicit than "one prepared statement, reassigned parameters, one transaction".
   Learning curve: medium. The better choice for an app with many tables and lots of
   simple CRUD.
2. **`op-sqlite`.** Pros: the fastest React Native SQLite binding (JSI, synchronous
   calls available), popular for heavy workloads. Cons: third-party native module with
   its own upgrade cadence, solving a performance problem this app (720 rows/hour)
   doesn't have. Learning curve: low. The better choice if Phase 12 measures
   `expo-sqlite` as too slow, which the targets make unlikely.
3. **WatermelonDB.** Pros: reactive queries, built for large datasets and sync. Cons:
   its own model layer, decorators and a sync protocol this offline app will never
   use; a big framework for five tables. Learning curve: high. The better choice for
   an app syncing thousands of records to a server.
4. **A JSON store (kv-store / MMKV) instead of SQL.** Pros: no schema, no migrations.
   Cons: this data is genuinely relational (one workout has many samples), and Phase
   10's `GROUP BY` aggregates are exactly where SQL wins. Learning curve: lowest. The
   better choice for settings, which is precisely what Phase 08 uses it for.

**Why not the alternatives?** Drizzle loses specifically because of this phase's
buffered-transaction write pattern, the one place explicit statement/transaction
control earns its extra verbosity; it's a judgement call, not an obvious win.
`op-sqlite` and WatermelonDB solve problems this app doesn't have. A JSON store can't
do Phase 10's aggregates.

**Long-term considerations.** First-party Expo, lowest abandonment risk of the five.
The `Db` interface (Phase 00) means repositories don't import `expo-sqlite` at all, so
switching to `op-sqlite` later would be one new adapter file. Swapping to Drizzle
later is moderate cost (repositories rewritten), but the schema below stays valid
either way, since it's plain SQLite under all of them.

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

SQLite, accessed via `expo-sqlite` through the `Db` interface. Migration-based,
forward-only.

### Design decisions

**Dual keys on `Workout`.** `Id` (`INTEGER PRIMARY KEY AUTOINCREMENT`) is the
local physical key, used for all foreign keys and joins. `WorkoutId` (`TEXT
UNIQUE`, a GUID) is the portable business key, used only at backup/restore
boundaries. `WorkoutSample` joins on the workout thousands of rows at a time;
joining on a 36-character TEXT GUID instead of a rowid is measurably slower and
larger. The GUID is only needed when data crosses devices. **Integer PKs must
never be used for de-duplication across devices.** The same integer means
different workouts on two phones. De-duplicating on it silently drops unrelated
workouts: the single most likely cause of silent data loss in this project.

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

*This is a judgement call, not a settled fact: revisit if Phase 10's per-workout
speed curves look too coarse in practice.*

**All times are UTC.** `StartedAtUtc` and `EndedAtUtc` are UTC ISO 8601 strings
(`new Date().toISOString()`). `StartOffsetMinutes` stores the local UTC offset at the
time of the workout. Local-time-without-offset shifts every daily and weekly bucket
when a backup is restored in another timezone, or across a DST boundary. Store UTC +
offset; compute the local day from both.

**The sign trap.** JavaScript's `date.getTimezoneOffset()` returns the offset with
the *opposite* sign from the usual convention: it's `-480` in the Philippines
(UTC+8). Store `StartOffsetMinutes = -date.getTimezoneOffset()`, so UTC+8 is `480`
and the local-day query below adds eight hours, as it should. A test for 23:30 local
time catches this immediately; skipping the test means every workout after 16:00
lands on the wrong day.

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

`ON DELETE CASCADE` requires `PRAGMA foreign_keys = ON` on the connection doing the
delete. SQLite does not enable it by default. The app has one connection and
`openAppDatabase.ts` sets it there; the cascade test in task 6.1 proves it.

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
treadmill still has to be found again. Tell the user they need to reconnect once.

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
PRAGMA journal_mode = WAL;      -- faster commits, crash-safe
PRAGMA foreign_keys = ON;       -- required for CASCADE
PRAGMA synchronous = NORMAL;    -- safe with WAL, much faster writes
PRAGMA busy_timeout = 5000;
```

These are set once, in `openAppDatabase.ts`, on the app's only connection. A second
connection would need them again, which is why the project never opens one (see
Phase 00 task 0.11 on `withExclusiveTransactionAsync`).

### Sample write strategy

**Buffer in memory, flush every 30–60 seconds in a single transaction.**

Do not insert per sample. 720 individual transactions per workout is needless battery
and flash wear for no benefit. Worst-case crash loses under a minute of telemetry.

Flush on: the timer, workout pause, workout finish, and the app going to the
background.

```ts
await db.transaction(async (tx) => {
  const insert = await tx.prepare(`
    INSERT OR REPLACE INTO WorkoutSample
        (WorkoutRowId, ElapsedSec, SpeedKph, DistanceM, Calories, HeartRate, Flags)
    VALUES ($w, $e, $s, $d, $c, $h, $f)`);
  try {
    // insert.run({ $w: ..., $e: ..., ... }) once per buffered row
  } finally {
    await insert.finalize();
  }
});
```

Reusing one prepared statement with new parameters per row is significantly faster
than preparing a statement per row. `INSERT OR REPLACE` makes the flush idempotent, so
a retried flush after a partial failure is safe. The `finally` matters: a statement
that's never finalized holds resources until the connection closes.

**Target: one hour of samples (720 rows) inserts in under 1 second** on the phone. If
it doesn't, the statement reuse is probably wrong.

### Query patterns

Aggregate in SQL, not in TypeScript over a full table read.

```sql
-- Daily totals, local-day correct via stored offset
SELECT
    date(datetime(StartedAtUtc, printf('%+d minutes', StartOffsetMinutes))) AS LocalDay,
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

**Why `printf('%+d minutes', ...)` and not `'+' || StartOffsetMinutes || ' minutes'`.**
The concatenation looks simpler and works for UTC+8 (`'+480 minutes'`), but a
negative offset produces `'+-300 minutes'`, which SQLite doesn't accept: `datetime()`
returns `NULL`, `date()` returns `NULL`, and that workout silently falls out of every
daily bucket. `%+d` always writes exactly one sign (`+480`, `-300`, `+0`). Checked
against real SQLite while writing this section; the test in task 6.2 keeps it honest.

**Downsample sample series for display.** A 2-hour workout is 1,440 points; a 400 px
chart needs perhaps 200. Downsample in SQL (`WHERE ElapsedSec % $n = 0`) rather than
loading everything and thinning it in TypeScript. Phase 10 uses this pattern directly.

### Performance targets

| Operation | Target | Dataset |
|-----------|--------|---------|
| Sample flush (60 rows) | < 50 ms | any |
| Full workout sample insert (720 rows) | < 1 s | any |
| Workout list page (50 rows) | < 100 ms | 5,000 workouts |
| Weekly aggregate | < 200 ms | 5,000 workouts |
| Single workout chart load | < 200 ms | 2-hour workout |

Measured on the phone in Phase 12, against a seeded 5,000-workout database. The
`node:sqlite` tests below give a rough early signal only: a desktop CPU is much
faster than a phone's. Do not assume.

### Backup mapping

`WorkoutId` and `DeviceUid` are the identities used in backup files (Phase 09).
Integer `Id` values are **not exported** and are reassigned on import.

Exported: `Workout`, `WorkoutSample`, `Device`, plus settings from the kv-store
(Phase 08). Not exported: `SchemaVersion`, statistics, personal records (all derived
or local).

---

## Your tasks

### 6.1 — Write the real schema as migration 1

Touches: `src/core/data/migrations.ts`.

The migration runner already exists and already works. Your job is to give it a real
migration list instead of an empty one.

Concrete steps:
1. Copy the `Workout`, `WorkoutSample`, and `Device` `CREATE TABLE` statements, and
   their indexes, **verbatim** from the "Schema" section above into one SQL string.
   That section is the source of truth for the schema; this migration is only the
   mechanism that applies it. Do **not** include the `SchemaVersion` table itself.
   The runner already creates that unconditionally, before it even looks at which
   migrations are pending.
2. Shape:
   ```ts
   export interface Migration {
     readonly version: number;
     readonly sql: string;
   }

   export const migrations: readonly Migration[] = [
     { version: 1, sql: /* sql */ `CREATE TABLE Device ( ... ); CREATE TABLE Workout ( ... ); ...` },
   ];
   ```
   Order inside the string matters: `Workout` references `Device`, so create `Device`
   first.
3. `npx tsc --noEmit` and `npx expo lint`.

Then prove it, following `migrationRunner.test.ts`'s exact pattern (a fresh
`nodeSqliteDb` per test, the PRAGMAs applied, the runner):

4. Create `src/core/data/workoutSchema.test.ts`.
5. `test('migration 1 creates the three tables', ...)`: apply `migrations`, query
   `sqlite_master` for `type = 'table'`, and expect `Workout`, `WorkoutSample`, and
   `Device` all to be present.
6. `test('deleting a workout cascades to its samples', ...)`: insert a `Workout` row,
   insert a `WorkoutSample` row referencing it, delete the `Workout` row, then expect
   the `WorkoutSample` row to be gone too. This is the test that actually proves
   `ON DELETE CASCADE` works, which only happens because the PRAGMA is on. Run it once
   *without* the PRAGMA to watch it fail, so you know the test can fail.
7. `npm test`. All green, including every Phase 00 test (regression).

### 6.2 — `WorkoutRepository`: header-first writes and crash recovery

Creates: `src/core/data/workoutRepository.ts`.

This class owns the `Workout` row's lifecycle: create it the moment a workout starts
(`Status = 0`), update it as the workout progresses, and find any left in progress by
a previous run that never reached a clean finish. It does **not** own samples;
that's 6.3.

The shape:

```ts
export interface InProgressWorkout {
  readonly id: number;
  readonly workoutId: string;
  readonly startedAtUtc: string;
}

export interface WorkoutSummary {
  readonly endedAtUtc: string;
  readonly durationSeconds: number;
  readonly distanceMeters: number;
  readonly calories: number;
  readonly avgSpeedKph: number;
  readonly maxSpeedKph: number;
  readonly avgHeartRate: number | null;
  readonly maxHeartRate: number | null;
}

export interface RepositoryDeps {
  /** A new GUID. On the phone: expo-crypto's randomUUID. In tests: anything unique. */
  readonly newId: () => string;
  /** The current time. Injected so tests can pin it. */
  readonly now: () => Date;
}

export class WorkoutRepository {
  constructor(private readonly db: Db, private readonly deps: RepositoryDeps) {}

  /**
   * Writes the header row with Status = 0 (in progress) and returns the new
   * physical Id. Called at workout START, not at the end: see the "Schema" section
   * above on why. A crash then leaves a recoverable partial workout, not nothing.
   */
  async startWorkout(startedAt: Date, deviceId: number | null): Promise<number> {
    // TODO: INSERT INTO Workout (WorkoutId, StartedAtUtc, StartOffsetMinutes,
    // Status, CreatedAtUtc, UpdatedAtUtc, DeviceId) VALUES ($workoutId, ...);
    // WorkoutId = this.deps.newId(): the portable identity, minted exactly once,
    // here. StartOffsetMinutes = -startedAt.getTimezoneOffset() (the sign trap above).
    // Return the physical Id from the run result's lastInsertRowId.
  }

  /** Status = 0 rows found at startup: a previous run never reached finished. */
  async findInProgressWorkouts(): Promise<InProgressWorkout[]> {
    // TODO: SELECT Id AS id, WorkoutId AS workoutId, StartedAtUtc AS startedAtUtc
    // FROM Workout WHERE Status = 0; the partial index from 6.1 makes this cheap.
    // (Aliasing columns to camelCase in SQL is the simplest way to get rows that
    // already match your TypeScript interface.)
  }

  async completeWorkout(id: number, summary: WorkoutSummary): Promise<void> {
    // TODO: UPDATE Workout SET Status = 1, EndedAtUtc = $e, DurationSeconds = $d,
    // DistanceMeters = $dm, Calories = $c, AvgSpeedKph = $avg, MaxSpeedKph = $max,
    // AvgHeartRate = $ahr, MaxHeartRate = $mhr, UpdatedAtUtc = $now WHERE Id = $id;
  }

  async abandonWorkout(id: number): Promise<void> {
    // TODO: UPDATE Workout SET Status = 2, UpdatedAtUtc = $now WHERE Id = $id;
  }
}
```

Concrete steps:
1. Create the file with the shape above; fill in the four method bodies.
2. Every `INSERT`/`UPDATE` sets `CreatedAtUtc`/`UpdatedAtUtc` explicitly from
   `this.deps.now().toISOString()`. SQLite has nothing here that stamps timestamps for
   you.
3. `startWorkout` is the **only** place `WorkoutId` (the GUID) is minted. Nowhere else
   in the app should ever generate a new one for the same logical workout.
4. Add `expo-crypto` (`npx expo install expo-crypto`) and pass its `randomUUID` as
   `newId` where `services.tsx` creates the repository. It's the first-party way to get
   a UUID on the phone; `core/` can't import it, which is exactly why it's injected.
5. Trace through, and write a one-line comment answering: what does
   `findInProgressWorkouts` return when the app starts normally after a clean
   shutdown? It should be an empty list. Say why `completeWorkout` guarantees that.
6. Tests in `workoutRepository.test.ts`: start then find returns one row; complete
   then find returns none; a workout started at 23:30 local in UTC+8 stores
   `StartOffsetMinutes = 480` and the daily query puts it on the right day. To test
   the offset without changing the machine's timezone, pass a `Date` and compute the
   expected value from that same date's `getTimezoneOffset()`, plus hand-written rows
   run through the daily query: one with `StartOffsetMinutes = 480` at
   `15:30Z` (23:30 local, same day), and one with `-300` at `02:30Z` (the previous
   local day). The negative one is the test that would have caught the `'+' ||`
   version of the query.

### 6.3 — `WorkoutSampleBuffer`: buffer in memory, flush in one transaction

Creates: `src/core/data/workoutSampleBuffer.ts`.

The "Sample write strategy" section above already shows the SQL shape. Your job is to
wrap it in a class a caller can `add()` to constantly and `flush()` occasionally,
without the caller needing to know a transaction is involved.

```ts
export interface WorkoutSampleRow {
  readonly elapsedSec: number;
  readonly speedKph: number | null;
  readonly distanceM: number | null;
  readonly calories: number | null;
  readonly heartRate: number | null;
  readonly flags: number; // bit 0 = connection gap
}

export class WorkoutSampleBuffer {
  private pending: WorkoutSampleRow[] = [];

  get pendingCount(): number {
    return this.pending.length;
  }

  add(row: WorkoutSampleRow): void {
    this.pending.push(row);
  }

  /**
   * Writes every buffered sample for one workout in a single transaction and clears
   * the buffer. Idempotent: flushing the same rows twice (e.g. a retry after a
   * partial failure) must not duplicate or corrupt data. Returns the number flushed.
   */
  async flush(db: Db, workoutRowId: number): Promise<number> {
    if (this.pending.length === 0) return 0;

    // Take the batch BEFORE the first await. Samples that arrive while this flush
    // is in progress go into a fresh array and wait for the next flush.
    const batch = this.pending;
    this.pending = [];

    // TODO: db.transaction(async (tx) => { ... }) around the prepared INSERT OR
    // REPLACE from "Sample write strategy": prepare ONCE, run once per row with
    // { $w: workoutRowId, $e: row.elapsedSec, ... }, finalize in a finally.
    // Do not prepare per row: that's the mistake the section above warns is
    // "significantly slower", and almost always why the 720-row target is missed.
    //
    // TODO: if the transaction throws, put `batch` back in front of this.pending
    // before rethrowing, so a failed flush doesn't drop the rows. Why is that safe
    // to retry? (Hint: OR REPLACE.)
    return batch.length;
  }
}
```

Concrete steps:
1. Create the file with the shape above and fill in the two `TODO`s.
2. Nullable columns need nothing special: pass `null` and SQLite stores `NULL`. Don't
   use `0` or `undefined` for "absent" (`undefined` isn't a valid SQL parameter and
   will throw).
3. Create `src/core/data/workoutSampleBuffer.test.ts`:
   - Add 60 samples, flush, expect 60 rows and `pendingCount` back to 0.
   - Flush an empty buffer: returns 0 and never calls `db.transaction` (spy on it).
   - **Idempotency test**: flush the same rows twice (re-`add` them between flushes)
     and expect the row count unchanged, not doubled. This is what `INSERT OR
     REPLACE` buys you, worth proving rather than trusting.
   - **Failure test**: make the second row violate a constraint (e.g. a `workoutRowId`
     that doesn't exist, with foreign keys on), expect `flush` to reject, zero rows
     written (the transaction rolled back), and `pendingCount` back to the full batch.
   - **Throughput smoke test**: 720 samples flush in well under a second in Node. It's
     only a rough signal; the real number comes from the phone in Phase 12.

### 6.4 — Startup recovery

Touches: `src/features/services.tsx` (or wherever the database is opened at startup).

`findInProgressWorkouts()` (6.2) gives you the list; deciding what happens to each row
is a real design decision, not a mechanical step:

- **Simplest defensible policy:** call `abandonWorkout(id)` on every row found at
  startup. Whatever `WorkoutSample` rows already made it to disk remain as the
  recorded (but incomplete) history of that session.
- **More ambitious policy:** compute a summary from the samples that did make it to
  disk, and `completeWorkout` with that summary instead of abandoning it. Phase 14's
  test 7 ("force-close mid-workout, relaunch, partial workout recovered") is easier
  to pass with this one.

Concrete steps:
1. Pick one (either is acceptable; the Tests section below only requires that *a*
   policy exists and runs).
2. Call `findInProgressWorkouts()` once, early in startup, right after the database
   is opened and migrated.
3. Apply your chosen policy to every row returned.
4. Write a one-line comment recording which policy you picked and why, the same
   "make the call, then say why" pattern Phase 01b used for the fake service's
   Start-preserves-target-speed decision.

### 6.5 — `WorkoutRecorder`: connect the engine to the database, and the flush triggers

Creates: `src/core/workout/workoutRecorder.ts`,
`src/features/workout/flushOnBackground.ts`.

Something has to own "a workout is being recorded right now": listen to Phase 04's
engine, keep the 5-second cadence, hold the buffer, and decide when to flush. That's
`WorkoutRecorder`, a plain `core/` class like the engine.

- Constructor takes the `WorkoutEngine`, the `WorkoutRepository`, the `Db`, and a
  flush interval (default 45 000 ms; tests pass something small).
- On the engine's `'active'` (from `idle`): `startWorkout(...)`, remember the
  returned row id, start a `setInterval` that calls `flush`.
- On every `onSampleRecorded`: keep one row per 5-second bucket of
  `elapsedActiveSeconds` (e.g. when `elapsedActiveSeconds % 5 === 0`, or the first
  record that lands in a new bucket: your call, say which). Always keep records with
  `isConnectionGap: true`, as `flags: 1`.
- On `'paused'`: flush.
- On `'finished'`: stop the interval, flush, then `completeWorkout(...)` with a
  summary computed from what was recorded.
- `flushNow(): Promise<void>` for the background trigger below.

Four triggers must flush: the periodic timer, workout pause, workout finish, and the
app going to the background.

Concrete steps:
1. Write `WorkoutRecorder` with the behaviour above. Create it in `services.tsx`.
2. `flushOnBackground.ts` (this one's in `features/` because it uses React Native):
   subscribe once, at startup, to `AppState.addEventListener('change', ...)` and call
   `recorder.flushNow()` when the new state is `'background'`. That's the React Native
   equivalent of "the app is about to sleep." Keep it a best-effort flush of what's
   already buffered, nothing expensive.
3. Tests in `workoutRecorder.test.ts` with fake timers and `FakeTreadmillService`:
   start a workout, advance 60 s of samples, expect one `Workout` row with
   `Status = 0` and roughly 12 sample rows after a flush; stop it, expect `Status = 1`.
4. Manually verify on the phone: start a fake workout, press Home, and check the
   database directly (pull it with
   `adb exec-out run-as com.myhi.companion cat files/SQLite/myhi.db > myhi.db` and
   open it in any SQLite browser) or a debug log line, to confirm a flush happened.

Worth noting now, mattering in Phase 07: the periodic flush is a `setInterval`, so it
only fires with the screen off if the keep-alive is running. The pause/finish flushes
are triggered by events, which arrive regardless.

### 6.6 — Counter semantics: agree on the shape, not the logic (blocked)

If Phase 04's task 4.3 already implemented baselining from a resolved V1, skip this.
Otherwise, per `../phase-00-probe-app/PHASE-00-FINDINGS.md` V1, this is still
**open**. Do not write the real re-baselining logic until it resolves. Guessing wrong
here makes every stored workout wrong, silently, forever. What you can do now is agree
on the shape so that the moment V1 resolves, filling it in is mechanical rather than a
redesign:

| V1 verdict | What the engine does |
|------------|----------------------|
| **Per-session** | Record reported values directly |
| **Cumulative** | Every workout value is a delta against the value captured at workout start; detect a mid-workout counter reset (value decreases) and re-baseline |

```ts
/**
 * Placeholder shape only: do not fill in reset-detection logic until
 * PHASE-00-FINDINGS.md V1 (counter reset semantics) is resolved. If V1 turns out
 * per-session, most of this class disappears; if cumulative, the TODO becomes real.
 */
export class CounterBaseline {
  private baselineMeters: number | null = null;
  private lastReportedMeters: number | null = null;

  toWorkoutDistance(reportedMeters: number): number | null {
    // TODO once V1 resolves:
    // - per-session: return reportedMeters unchanged; this class is unused
    // - cumulative: baseline = first reading; return reported - baseline;
    //   detect reportedMeters < lastReportedMeters (a reset) and re-baseline from
    //   that point
    throw new Error('Blocked on PHASE-00-FINDINGS.md V1: counter reset semantics.');
  }
}
```

This is the reason the phase is blocked on V1 rather than merely informed by it.

### Review checkpoint

Before this phase is marked done: the agent reviews `workoutRepository.ts`,
`workoutSampleBuffer.ts` and `workoutRecorder.ts` against the "Schema reference"
section above line by line: every PRAGMA, every index, every nullable column, the
offset sign. Then run the flush on the phone together and actually look at the
number, not just the pass/fail.

---

## Implementation requirements

- **Buffer samples in memory; flush every 30–60 s inside a single transaction.** Do not
  insert per sample. 720 individual transactions per workout is needless battery and
  flash wear. Worst-case crash loses under a minute. (Tasks 6.3, 6.5)
- Flush on: the timer, workout pause, workout finish, and the app going to the
  background. (Task 6.5)
- Prepare one statement and reuse it with new parameters. Preparing per row is
  significantly slower, and if the 720-row target is missed this is almost always why.
  (Task 6.3)
- `INSERT OR REPLACE` makes a flush idempotent, so a retried flush after partial
  failure is safe. (Task 6.3)
- **Write the workout header at workout start** with `Status = 0` (in progress), not at
  the end. A crash then leaves a recoverable partial workout rather than nothing.
  (Task 6.2)
- On app start, detect `Status = 0` rows and either recover or discard per policy. The
  partial index on `Status = 0` makes this query trivial. (Task 6.4)
- `PRAGMA foreign_keys = ON` on the connection. SQLite does not enable it by default
  and `ON DELETE CASCADE` silently does nothing without it. Already handled by
  `openAppDatabase.ts` (Phase 00); task 6.1's test proves it.
- **Metric only.** Distance in metres, speed in km/h, energy in kcal. Unit conversion
  happens at display time and never touches the database.
- **UTC plus `StartOffsetMinutes`**, with the sign flipped from `getTimezoneOffset()`.
  Local-time-without-offset shifts every daily and weekly bucket when restored in
  another timezone, or just across a DST boundary.
- **Heart rate: store `NULL` when the user is not gripping.** Do not carry the last
  value forward, or every average is computed over fabricated data. If V3 said
  marginal, keep writing the column and hide the metric. Recording a column you don't
  display is cheap and reversible; back-filling one you never recorded is not.
- **Gap markers**: `Flags` bit 0, from the Phase 04 engine's `isConnectionGap`.

---

## Tests

- 10 workouts via `FakeTreadmillService`, restart, all present with correct sample counts
- Kill the app mid-workout; on restart the partial workout is handled per policy (task 6.4)
- **Throughput: 1 hour of samples (720 rows) inserts in under 1 s** on the phone (task 6.3; measured properly in Phase 12)
- Flush of 60 rows under 50 ms on the phone (task 6.3)
- Idempotent flush: run the same flush twice, row count unchanged (task 6.3)
- A failed flush keeps its rows for the next attempt (task 6.3)
- Cascade delete removes samples (proves `foreign_keys` is actually on) (task 6.1)
- Timezone: a workout at 23:30 local stores UTC + offset and reconstructs the right
  local day (task 6.2)
- `[HUMAN]` One real 20-minute workout; summary matches the treadmill console

## Acceptance

- [ ] No data loss across app restart
- [ ] Sample cadence correct within ±1 s
- [ ] 720-row insert under 1 s on the phone
- [ ] No derived value (statistic, PR) stored anywhere
