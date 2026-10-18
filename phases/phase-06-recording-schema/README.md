# Phase 06 — Saving Workouts

**Treadmill needed:** no for building · **Size:** M · **Waits for:** Phase 04
**Also needs:** Finding V1 (do counters reset?). **Don't start without it.**

---

## Goal

Save every workout and its data so nothing is lost. The tables are in "Schema" below.
Read that before writing anything.

**Phase 00 already built the plumbing.** Open these first:

- `src/core/data/db.ts`: the `Db` interface (`exec`, `run`, `all`, `first`, `prepare`,
  `transaction`). That's the whole database API you'll use.
- `src/features/data/expoDb.ts` and `openAppDatabase.ts`: open the database, turn on
  the settings it needs, and queue every query. You don't call these yourself.
- `src/core/data/migrationRunner.ts`: applies each migration once, in order, and
  records it in a `SchemaVersion` table. Its tests (`migrationRunner.test.ts`) are the
  template for yours.
- `src/core/data/migrations.ts`: an **empty** list today. Task 6.1 adds the real tables.
- `src/core/data/testing/nodeSqliteDb.ts`: the same `Db` on the PC, so every test here
  runs real SQL.

So this phase is: add the tables, then three classes. One saves the workout summary,
one saves the per-second data in batches, and one connects them to Phase 04's engine.

### The idea

**Save data in batches, not one row at a time.** Saving each sample on its own means
720 separate database commits an hour. Most of the cost is the commit, not the row. So
samples go into a list in memory, and every 30–60 seconds the whole list is saved in one
transaction. Same rows, far fewer commits, less battery and less wear on the phone's
storage.

**But a crash loses whatever's in the list**, so up to a minute of data. That's
acceptable here (it's the target below). Saving each row instantly would barely improve
it while costing 720 commits an hour.

The workout's summary row is different: it's written only twice per workout, so it's
saved straight away, no batching.

Two more choices:

- **Two IDs per workout.** `Id` (a number) is fast and used inside this phone's
  database. `WorkoutId` (a long random ID, a GUID) is the same on every phone, so a
  backup restored on another phone can't mix up workouts. Two phones will both have an
  `Id = 1`, but never the same GUID.
- **The workout row is written when the workout *starts***, marked "in progress". If
  the app crashes, there's a row to recover, instead of nothing.

## What you'll learn

- SQL with named parameters (`$workoutId`), prepared statements and transactions. The
  same ideas work with any SQL database.
- Why one transaction per batch beats one per row: either the whole batch is saved or
  none of it, and far fewer commits.
- Migrations: a numbered list of table changes, each applied once, never edited after
  it ships. To change something, add a new migration.
- `WITHOUT ROWID` tables: when SQLite's hidden row number is wasted space.
- Saving "in progress" *before* the work finishes, so a crash can be detected.
- Passing in what `core/` can't import (a GUID maker, a clock), so the same code runs on
  the phone and in tests.

## Read first

| Topic | Link | For |
|---|---|---|
| `expo-sqlite` | https://docs.expo.dev/versions/latest/sdk/sqlite/ | What `expoDb.ts` wraps |
| `node:sqlite` | https://nodejs.org/api/sqlite.html | What the test version wraps |
| Foreign keys | https://sqlite.org/foreignkeys.html | Why `foreign_keys = ON` is needed for cascade deletes |
| WAL | https://www.sqlite.org/wal.html | Why the database uses `journal_mode = WAL` |
| `WITHOUT ROWID` | https://www.sqlite.org/withoutrowid.html | Task 6.1 |
| Date functions | https://www.sqlite.org/lang_datefunc.html | The "local day" query below |
| `AppState` | https://reactnative.dev/docs/appstate | Task 6.5: save when the app goes to the background |

## Technology decision: `expo-sqlite`, plain SQL, no ORM

**Problem.** Workouts and their per-second data need to be saved on the phone. The
samples are saved in bursts (one batch every 30–60 seconds), and that's what this
decision is really about.

**Why this.** It's Expo's own SQLite library, updated with each Expo version. It has
prepared statements (prepare once, reuse for a whole batch) and transactions, which is
exactly what batched saving needs. Plain SQL means every query can be pasted into a
SQLite viewer to check the database by hand.

**Alternatives.**

1. **Drizzle ORM.** Good: typed tables and results, generated migrations. The closest
   alternative, and nicer for simple queries. Bad: another layer between you and SQL,
   and batch inserts are less explicit. Better for apps with many tables and lots of
   simple queries.
2. **`op-sqlite`.** Good: the fastest SQLite for React Native. Bad: another native
   library to keep up to date, for speed this app doesn't need. Better if Phase 12
   measures `expo-sqlite` as too slow.
3. **WatermelonDB.** Good: built for big datasets and syncing. Bad: its own model layer
   and sync system, for an offline app with three tables. Better for apps syncing to a
   server.
4. **A JSON store instead of SQL.** Good: no tables, no migrations. Bad: workouts have
   many samples, and Phase 10's totals need SQL's `GROUP BY`. Better for settings,
   which is what Phase 08 uses one for.

**Why not those.** Drizzle loses mainly because of the batched writes; it's a close
call. The others solve problems this app doesn't have.

**Long term.** Maintained by Expo. The repositories only use the `Db` interface, so
switching to `op-sqlite` later is one new file. The tables stay valid either way.

---

## The two decisions that matter

- **`Workout.Id`**: a number, `INTEGER PRIMARY KEY AUTOINCREMENT`. Only meaningful on
  this phone. Used to link tables.
- **`Workout.WorkoutId`**: a GUID, `UNIQUE`, made when the workout starts. The same on
  every phone; used by backups.
  **Never use `Id` to tell workouts apart across phones.** The same number means
  different workouts on two phones. Doing so would quietly delete workouts on restore.
- **`WorkoutSample`**: one row every **5 seconds** (~720 rows an hour, ~9 MB a year).
  Every 5 seconds rather than "only when something changes", because heart rate changes
  constantly anyway, and fixed timing is simpler to save and to chart.

---

## Schema

SQLite, through the `Db` interface. Changed only by adding migrations.

### Design notes

**Two keys on `Workout`.** `Id` links tables (fast, small). `WorkoutId` (GUID) is only
used for backups. Linking thousands of samples by a 36-character GUID would be slower
and bigger.

**One sample every 5 seconds.**

| How often | Rows per hour | Per workout | Per year (250 workouts) |
|---------|-----------|-------------------|-----------------|
| Every second | 3,600 | ~180 KB | ~45 MB |
| **Every 5 seconds** | **720** | **~36 KB** | **~9 MB** |
| Only on change | ~5–20 | ~1 KB | ~0.3 MB |

"Only on change" looks tempting, because speed changes in steps. But heart rate changes
all the time, so it ends up almost every second anyway, plus extra code to fill in the
gaps when reading. 9 MB a year is nothing on a phone. The speed curve is the part you'd
regret losing.

*A judgement call. Revisit if Phase 10's charts look too rough.*

**All times are UTC.** `StartedAtUtc` and `EndedAtUtc` are UTC text
(`new Date().toISOString()`). `StartOffsetMinutes` saves your time-zone offset at the
time. Without it, every "which day was this" would shift if you restored a backup in
another time zone, or across a daylight-saving change.

**The sign trap.** JavaScript's `date.getTimezoneOffset()` has the opposite sign to
normal: it's `-480` in the Philippines (UTC+8). Save
`StartOffsetMinutes = -date.getTimezoneOffset()`, so UTC+8 is `480`. A test at 23:30
local time catches this immediately. Without it, every workout after 4 pm lands on the
wrong day.

**Everything metric.** Metres, km/h, kcal. **Convert only on screen, never in the
database.** An imperial setting that saved converted values would ruin the data for
good.

**Nothing calculated is saved.** Totals and personal records (Phase 10) are calculated
when needed, from `Workout` and `WorkoutSample`. They're not columns, not tables, not in
backups. If a query gets slow, a rebuildable cache can be added later. Don't start
there.

### Tables

**`Workout`**

```sql
CREATE TABLE Workout (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    WorkoutId           TEXT    NOT NULL,           -- GUID, the same on every phone
    StartedAtUtc        TEXT    NOT NULL,           -- ISO 8601, UTC
    EndedAtUtc          TEXT    NULL,               -- NULL while in progress
    StartOffsetMinutes  INTEGER NOT NULL,           -- time-zone offset at the start
    DurationSeconds     INTEGER NOT NULL DEFAULT 0, -- active time, without pauses
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

`Status = 0` (in progress) is written when the workout **starts**. A crash leaves a row
to recover. The last index makes "find unfinished workouts" instant.

`DurationSeconds` is **active** time only.

**`WorkoutSample`**

```sql
CREATE TABLE WorkoutSample (
    WorkoutRowId  INTEGER NOT NULL REFERENCES Workout(Id) ON DELETE CASCADE,
    ElapsedSec    INTEGER NOT NULL,
    SpeedKph      REAL    NULL,
    DistanceM     REAL    NULL,
    Calories      INTEGER NULL,
    HeartRate     INTEGER NULL,
    Flags         INTEGER NOT NULL DEFAULT 0,  -- bit 0 = connection gap
    PRIMARY KEY (WorkoutRowId, ElapsedSec)
) WITHOUT ROWID;
```

`WITHOUT ROWID`: the two-column key is how this table is always looked up, so SQLite's
hidden row number would just be extra.

`Flags` bit 0 marks **a connection gap**. Charts show a break there, not a straight line.

**Heart rate:** use `0x2A37` (the Heart Rate service) over the field in `0x2ACD`. Save
`NULL` when nobody's holding the grips. Never copy the last value forward, or the
average includes made-up numbers. If V3 said unusable, keep saving the column but don't
show it: saving a column you don't show is cheap; filling in one you never saved is
impossible.

`ON DELETE CASCADE` only works if `PRAGMA foreign_keys = ON` is set on the connection.
SQLite leaves it off by default. `openAppDatabase.ts` turns it on, and task 6.1's test
proves it.

**`Device`**

```sql
CREATE TABLE Device (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    DeviceUid     TEXT    NOT NULL,   -- GUID, the same on every phone
    MacAddress    TEXT    NOT NULL,
    Name          TEXT    NOT NULL,
    LastSeenUtc   TEXT    NULL,
    IsPreferred   INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc  TEXT    NOT NULL
);

CREATE UNIQUE INDEX UX_Device_DeviceUid  ON Device(DeviceUid);
CREATE UNIQUE INDEX UX_Device_MacAddress ON Device(MacAddress);
```

A restored device won't connect by itself on a new phone; you'll need to connect once.

If the findings say the treadmill's address is **random**, the MAC isn't stable and
`UX_Device_MacAddress` is wrong: match by name instead. Sort this out before
implementing.

**`SchemaVersion`**

```sql
CREATE TABLE SchemaVersion (
    Version    INTEGER NOT NULL PRIMARY KEY,
    AppliedUtc TEXT    NOT NULL
);
```

Migrations run at startup, in a transaction, oldest first. Never edit one that has
shipped; add a new one.

### Database settings

```sql
PRAGMA journal_mode = WAL;      -- faster commits, safe after a crash
PRAGMA foreign_keys = ON;       -- needed for CASCADE
PRAGMA synchronous = NORMAL;    -- safe with WAL, much faster
PRAGMA busy_timeout = 5000;
```

Set once in `openAppDatabase.ts`. The app only ever uses one connection, so this is the
only place (see Phase 00 task 0.11 for why there's never a second one).

### Saving samples

**Keep samples in memory; save every 30–60 seconds in one transaction.** Also save when
the workout pauses, when it finishes, and when the app goes to the background.

```ts
await db.transaction(async (tx) => {
  const insert = await tx.prepare(`
    INSERT OR REPLACE INTO WorkoutSample
        (WorkoutRowId, ElapsedSec, SpeedKph, DistanceM, Calories, HeartRate, Flags)
    VALUES ($w, $e, $s, $d, $c, $h, $f)`);
  try {
    // insert.run({ $w: ..., $e: ..., ... }) for each row
  } finally {
    await insert.finalize();
  }
});
```

Prepare once, run once per row: much faster than preparing for every row.
`INSERT OR REPLACE` means saving the same rows twice is harmless, so a retry after a
failure is safe. The `finally` releases the statement even if something fails.

**Target: an hour of samples (720 rows) saves in under 1 second** on the phone. If not,
the statement is probably being prepared per row.

### Queries

Add up totals in SQL, not in TypeScript after loading everything.

```sql
-- Totals per day, using each workout's own time-zone offset
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

Every total filters on `StartedAtUtc`, which is why it has an index.

**Use `printf('%+d minutes', ...)`, not `'+' || StartOffsetMinutes || ' minutes'`.**
The second looks simpler and works for UTC+8 (`'+480 minutes'`). But a negative offset
becomes `'+-300 minutes'`, which SQLite rejects: the result is `NULL`, and that workout
quietly disappears from every daily total. `%+d` always writes exactly one sign. (Tested
against real SQLite; task 6.2's test keeps it honest.)

**Fewer points for charts.** A 2-hour workout has 1,440 samples; a phone chart needs
about 200. Pick every n-th row in SQL (`WHERE ElapsedSec % $n = 0`), not in TypeScript.
Phase 10 does this.

### Speed targets

| What | Target | With |
|-----------|--------|---------|
| Save 60 samples | < 50 ms | any |
| Save a whole workout's samples (720) | < 1 s | any |
| Workout list (50 rows) | < 100 ms | 5,000 workouts |
| Weekly totals | < 200 ms | 5,000 workouts |
| One workout's chart | < 200 ms | a 2-hour workout |

Measured on the phone in Phase 12, with 5,000 fake workouts. Tests on the PC only give
a rough early idea: a PC is much faster than a phone.

### Backups

`WorkoutId` and `DeviceUid` are what backups (Phase 09) use. The number `Id`s are **not
exported**; new ones are made on import.

Exported: `Workout`, `WorkoutSample`, `Device`, and the settings (Phase 08). Not
exported: `SchemaVersion`, totals, records (all calculated or local).

---

## Tasks

### 6.1 — The tables, as migration 1

Change `src/core/data/migrations.ts`.

1. Copy the `Workout`, `WorkoutSample` and `Device` `CREATE TABLE` statements and their
   indexes **exactly** from "Tables" above into one SQL string. Don't include
   `SchemaVersion`: the runner creates it.
2. Shape:
   ```ts
   export interface Migration {
     readonly version: number;
     readonly sql: string;
   }

   export const migrations: readonly Migration[] = [
     { version: 1, sql: `CREATE TABLE Device ( ... ); CREATE TABLE Workout ( ... ); ...` },
   ];
   ```
   Create `Device` first, because `Workout` refers to it.
3. `npx tsc --noEmit` and `npx expo lint`.

Then test it, copying `migrationRunner.test.ts`'s setup:

4. Create `src/core/data/workoutSchema.test.ts`.
5. Test: after the migrations, `sqlite_master` lists `Workout`, `WorkoutSample` and
   `Device`.
6. Test: insert a workout and a sample for it, delete the workout, and the sample is
   gone too. This proves cascade delete works. Run it once with `foreign_keys` off and
   watch it fail, so you know the test really checks something.
7. `npm test`.

### 6.2 — `WorkoutRepository`: the workout row

Create `src/core/data/workoutRepository.ts`.

This class owns the `Workout` row: create it at the start ("in progress"), update it at
the end, and find any left unfinished by a crash. It doesn't handle samples (6.3).

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
  /** Makes a new GUID. On the phone: expo-crypto. In tests: anything unique. */
  readonly newId: () => string;
  /** The current time. Passed in so tests can fix it. */
  readonly now: () => Date;
}

export class WorkoutRepository {
  constructor(private readonly db: Db, private readonly deps: RepositoryDeps) {}

  /** Writes the row with Status = 0 at the START. Returns its Id. */
  async startWorkout(startedAt: Date, deviceId: number | null): Promise<number> {
    // TODO: INSERT INTO Workout (...) VALUES (...)
    // WorkoutId = this.deps.newId(). StartOffsetMinutes = -startedAt.getTimezoneOffset().
    // Return the run result's lastInsertRowId.
  }

  /** Workouts still marked in progress: a previous run never finished them. */
  async findInProgressWorkouts(): Promise<InProgressWorkout[]> {
    // TODO: SELECT Id AS id, WorkoutId AS workoutId, StartedAtUtc AS startedAtUtc
    //       FROM Workout WHERE Status = 0
  }

  async completeWorkout(id: number, summary: WorkoutSummary): Promise<void> {
    // TODO: UPDATE Workout SET Status = 1, EndedAtUtc = ..., ... WHERE Id = $id
  }

  async abandonWorkout(id: number): Promise<void> {
    // TODO: UPDATE Workout SET Status = 2, UpdatedAtUtc = ... WHERE Id = $id
  }
}
```

1. Fill in the four methods. `AS id` in SQL renames columns so the rows match your
   TypeScript types directly.
2. Set `CreatedAtUtc`/`UpdatedAtUtc` yourself, from `this.deps.now().toISOString()`.
   SQLite won't do it for you.
3. `startWorkout` is the **only** place a `WorkoutId` is made.
4. Install `expo-crypto` (`npx expo install expo-crypto`) and pass its `randomUUID` as
   `newId` in the services file. `core/` can't import it, which is why it's passed in.
5. Add a one-line comment answering: after a normal shutdown, what does
   `findInProgressWorkouts` return? (Empty. Why does `completeWorkout` guarantee it?)
6. Tests in `workoutRepository.test.ts`: start, then find → one row; complete, then find
   → none; and the time-zone checks. Insert a row with `StartOffsetMinutes = 480` at
   `15:30Z` (23:30 local: same day) and one with `-300` at `02:30Z` (the previous day),
   and run the daily query. The negative one would have caught the `'+' ||` bug.

### 6.3 — `WorkoutSampleBuffer`: batched saving

Create `src/core/data/workoutSampleBuffer.ts`.

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

  /** Saves everything in one transaction and empties the buffer. Returns how many. */
  async flush(db: Db, workoutRowId: number): Promise<number> {
    if (this.pending.length === 0) return 0;

    // Take the batch before any await. Samples arriving meanwhile wait for the next flush.
    const batch = this.pending;
    this.pending = [];

    // TODO: db.transaction(...) around the INSERT OR REPLACE from "Saving samples":
    // prepare once, run per row, finalize in finally
    // TODO: if it fails, put `batch` back at the front of this.pending, then rethrow
    return batch.length;
  }
}
```

1. Fill in the two TODOs.
2. Missing values: pass `null`, and SQLite saves `NULL`. Never `0`. Never `undefined`
   (it isn't a valid SQL value and throws).
3. `workoutSampleBuffer.test.ts`:
   - Add 60, flush → 60 rows saved, `pendingCount` is 0.
   - Flush when empty → returns 0, and never starts a transaction (spy on it).
   - Flush the same rows twice → still the same number of rows (that's `OR REPLACE`).
   - A failing flush (make one row point at a workout that doesn't exist) → it rejects,
     no rows saved, and all rows are back in the buffer.
   - 720 rows flush in well under a second on the PC. Just a rough check; Phase 12
     measures the phone.

### 6.4 — Recovering after a crash

Change wherever the database is opened at startup (the services file).

`findInProgressWorkouts()` gives you the unfinished workouts. What to do with them is
your decision:

- **Simplest:** `abandonWorkout(id)` for each. The samples already saved stay as an
  incomplete record.
- **Nicer:** work out a summary from the saved samples and `completeWorkout` it. Phase
  14's test 7 (force-close mid-workout) is easier to pass this way.

1. Pick one.
2. Call `findInProgressWorkouts()` once at startup, right after the database is opened.
3. Apply your choice to each.
4. Write one line saying which you picked and why.

### 6.5 — `WorkoutRecorder`: connecting the engine to the database

Create `src/core/workout/workoutRecorder.ts` and `src/features/workout/flushOnBackground.ts`.

Something needs to own "a workout is being recorded": listen to Phase 04's engine, keep
one row per 5 seconds, hold the buffer, and decide when to save. That's
`WorkoutRecorder`, a plain `core/` class like the engine.

- Takes the engine, the repository, the `Db`, and a save interval (default 45 seconds;
  tests use less).
- Engine goes `active` (from `idle`): `startWorkout(...)`, remember the row id, start a
  `setInterval` that flushes.
- Each record: keep one per 5 seconds of `elapsedActiveSeconds` (your call how: e.g.
  when it's a multiple of 5; say which). Always keep gap records, as `flags: 1`.
- Engine pauses: flush.
- Engine finishes: stop the interval, flush, then `completeWorkout(...)` with a summary.
- `flushNow()` for the background case below.

1. Write `WorkoutRecorder` and add it to the services file.
2. `flushOnBackground.ts` (React Native, so in `features/`): at startup, listen with
   `AppState.addEventListener('change', ...)` and call `recorder.flushNow()` when the
   state becomes `'background'`. Keep it quick: just save what's in memory.
3. `workoutRecorder.test.ts`, with fake timers and the fake treadmill: 60 seconds of
   samples → one in-progress `Workout` row and about 12 samples after a flush; stop it →
   `Status = 1`.
4. On the phone: start a fake workout, press Home, then check a flush happened (a log
   line, or pull the database:
   `adb exec-out run-as com.myhi.companion cat files/SQLite/myhi.db > myhi.db` and open
   it in a SQLite viewer).

Coming up in Phase 07: the save interval is a `setInterval`, so with the screen off it
only runs if the keep-alive is on. The pause/finish saves are events, which arrive
anyway.

### 6.6 — Counters: the shape only, if V1 is still open

If Phase 04's task 4.3 already handled counters, skip this. Otherwise, V1 is still
**unknown**. Don't write the real logic yet; guessing wrong ruins every saved workout.
Just agree on the shape, so filling it in later is quick:

| V1 says | Then |
|------------|----------------------|
| **Per session** | Save values as they come |
| **Cumulative** | Subtract the start value; if a value goes down, the counter reset, so adjust |

```ts
/** Shape only. Fill in once V1 is answered. */
export class CounterBaseline {
  private baselineMeters: number | null = null;
  private lastReportedMeters: number | null = null;

  toWorkoutDistance(reportedMeters: number): number | null {
    // TODO once V1 is known:
    // - per session: return reportedMeters (this class isn't needed)
    // - cumulative: first reading is the baseline; return reported - baseline;
    //   if reported < last, the counter reset: start a new baseline
    throw new Error('Waiting for PHASE-00-FINDINGS.md V1.');
  }
}
```

### Review

The agent checks `workoutRepository.ts`, `workoutSampleBuffer.ts` and
`workoutRecorder.ts` against the tables above, line by line: settings, indexes, nulls,
the offset sign. Then you run a save on the phone together and look at the actual time.

---

## Rules

- **Batch samples; save every 30–60 s in one transaction.** (6.3, 6.5)
- Save on: the timer, pause, finish, and the app going to the background. (6.5)
- Prepare the statement once and reuse it. (6.3)
- `INSERT OR REPLACE` makes repeat saves harmless. (6.3)
- **Write the workout row at the start**, as in progress. (6.2)
- At startup, find in-progress rows and recover or abandon them. (6.4)
- `foreign_keys = ON`, set once at open. Task 6.1's test proves it.
- **Metric only.** Convert on screen only.
- **UTC plus `StartOffsetMinutes`**, with the sign flipped from `getTimezoneOffset()`.
- **Heart rate is `NULL` when not gripping.** Never copy the last value forward.
- **Gap markers:** `Flags` bit 0, from the engine's `isConnectionGap`.

---

## Tests

- 10 workouts with the fake, restart the app: all there, with the right number of samples
- Kill the app mid-workout: on restart, it's handled by your 6.4 choice
- **720 rows save in under 1 s** on the phone (properly measured in Phase 12)
- 60 rows save in under 50 ms on the phone
- Saving the same rows twice doesn't duplicate them
- A failed save keeps its rows for next time
- Deleting a workout deletes its samples
- A workout at 23:30 local lands on the right local day, and a negative offset doesn't
  disappear
- Treadmill: one real 20-minute workout; the summary matches the treadmill's display

## Done when

- [ ] No data is lost across an app restart
- [ ] One sample every 5 seconds, within ±1 s
- [ ] 720 rows save in under 1 s on the phone
- [ ] Nothing calculated (totals, records) is saved anywhere
