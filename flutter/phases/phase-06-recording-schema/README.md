# Phase 06 — Workout Recording & Schema (Flutter track)

**Hardware:** none for development · **Size:** M · **Blocked by:** Phase 04
**Hard dependency:** V1 counter semantics (`PHASE-00-FINDINGS.md`). **Do not start without it.**

## Goal

Persist workouts and telemetry durably, via `sqflite` — the same schema and
the same write-strategy reasoning as the original track, since neither is
framework-specific: SQLite is SQLite, and "buffer in memory, flush every
30–60s in one transaction" is a database-design decision, not a C#-vs-Dart
one.

## The concept

Inserting every sample the instant it arrives means opening a transaction,
writing one row, and committing — 720 times an hour. Almost none of that
cost is the write itself; it's the transaction overhead around it.
`WorkoutSampleBuffer` batches: `add()` appends to an in-memory list, no I/O;
`flush()` writes every buffered row inside one `sqflite` transaction. Same
rows, far fewer round trips. Crash worst-case: lose at most one flush
interval (under a minute), which is the acceptance bar this phase already
sets, not a bound the naive per-sample approach meaningfully improves on.

`Workout` carries two identity columns for the same reason as the original:
`id` (SQLite's own integer rowid) is only unique on *this* phone; `workoutId`
(a UUID, minted once at `startWorkout`) is unique across every phone a
backup might ever land on. **Integer ids must never be used for
de-duplication across devices** — the same integer means a different
workout on two phones.

## Schema

Identical to the original track's, field-for-field — see
[`../../../phases/phase-06-recording-schema/README.md`](../../../phases/phase-06-recording-schema/README.md)
for the full design rationale (dual keys, fixed 5-second telemetry cadence,
UTC + stored offset, metric-only storage, derived data never stored). SQL
below is the same DDL `sqflite` runs unchanged — SQLite doesn't care what
wrote the `CREATE TABLE` statement.

```sql
CREATE TABLE Workout (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    workoutId           TEXT    NOT NULL,
    startedAtUtc        TEXT    NOT NULL,
    endedAtUtc          TEXT    NULL,
    startOffsetMinutes  INTEGER NOT NULL,
    durationSeconds     INTEGER NOT NULL DEFAULT 0,
    distanceMeters      REAL    NOT NULL DEFAULT 0,
    calories            INTEGER NOT NULL DEFAULT 0,
    avgSpeedKph         REAL    NOT NULL DEFAULT 0,
    maxSpeedKph         REAL    NOT NULL DEFAULT 0,
    avgHeartRate        INTEGER NULL,
    maxHeartRate        INTEGER NULL,
    status              INTEGER NOT NULL, -- 0=InProgress 1=Completed 2=Abandoned
    deviceId            INTEGER NULL REFERENCES Device(id),
    notes               TEXT    NULL,
    createdAtUtc        TEXT    NOT NULL,
    updatedAtUtc        TEXT    NOT NULL
);
CREATE UNIQUE INDEX ux_workout_workoutId ON Workout(workoutId);
CREATE INDEX ix_workout_startedAtUtc     ON Workout(startedAtUtc);
CREATE INDEX ix_workout_status           ON Workout(status) WHERE status = 0;

CREATE TABLE WorkoutSample (
    workoutRowId  INTEGER NOT NULL REFERENCES Workout(id) ON DELETE CASCADE,
    elapsedSec    INTEGER NOT NULL,
    speedKph      REAL    NULL,
    distanceM     REAL    NULL,
    calories      INTEGER NULL,
    heartRate     INTEGER NULL,
    flags         INTEGER NOT NULL DEFAULT 0, -- bit 0 = connection gap marker
    PRIMARY KEY (workoutRowId, elapsedSec)
) WITHOUT ROWID;

CREATE TABLE Device (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    deviceUid     TEXT    NOT NULL,
    macAddress    TEXT    NOT NULL,
    name          TEXT    NOT NULL,
    lastSeenUtc   TEXT    NULL,
    isPreferred   INTEGER NOT NULL DEFAULT 0,
    createdAtUtc  TEXT    NOT NULL
);
CREATE UNIQUE INDEX ux_device_deviceUid  ON Device(deviceUid);
CREATE UNIQUE INDEX ux_device_macAddress ON Device(macAddress);
```

Connection config, applied via `sqflite`'s `onConfigure` on every open —
`sqflite`, like `Microsoft.Data.Sqlite`, doesn't turn on `foreign_keys` by
default:

```dart
Future<void> _onConfigure(Database db) async {
  await db.execute('PRAGMA journal_mode = WAL');
  await db.execute('PRAGMA foreign_keys = ON');
  await db.execute('PRAGMA synchronous = NORMAL');
  await db.execute('PRAGMA busy_timeout = 5000');
}
```

## Tasks

### 6.1 — Migrations

`sqflite`'s `openDatabase(path, version: 1, onCreate: ..., onUpgrade: ...)`
is the built-in equivalent of the original's hand-rolled `MigrationRunner` +
`SchemaVersion` table — `sqflite` tracks the schema version itself, so this
track doesn't need to hand-build that table. `onCreate` runs the full DDL
above for a fresh install; `onUpgrade` is where future migrations go,
forward-only, never editing a shipped one.

```dart
Future<Database> openAppDatabase(String path) => openDatabase(
  path,
  version: 1,
  onConfigure: _onConfigure,
  onCreate: (db, version) async {
    await db.execute(workoutTableSql);
    await db.execute(workoutSampleTableSql);
    await db.execute(deviceTableSql);
  },
);
```

Test: open a fresh temp-file database, query `sqlite_master` for
`type='table'`, assert `Workout`/`WorkoutSample`/`Device` are present; insert
a `Workout` row, insert a dependent `WorkoutSample` row, delete the
`Workout` row, assert the sample is gone too (proves `foreign_keys` is
actually on).

### 6.2 — `WorkoutRepository`: header-first writes, crash recovery

```dart
typedef InProgressWorkout = ({int id, String workoutId, DateTime startedAtUtc});

class WorkoutRepository {
  final Database _db;
  WorkoutRepository(this._db);

  /// Writes the header with status=0 (in progress) at workout START, not the
  /// end — a crash then leaves a recoverable partial workout instead of
  /// nothing.
  Future<int> startWorkout(DateTime startedAtUtc, int startOffsetMinutes, int? deviceId) {
    final workoutId = const Uuid().v4(); // the portable identity, minted once, here
    // TODO: db.insert('Workout', {...}, status: 0), return the new rowid.
    throw UnimplementedError();
  }

  Future<List<InProgressWorkout>> findInProgressWorkouts() {
    // TODO: SELECT id, workoutId, startedAtUtc FROM Workout WHERE status = 0
    throw UnimplementedError();
  }

  Future<void> completeWorkout(int id, WorkoutSummary summary) {
    // TODO: UPDATE Workout SET status=1, endedAtUtc=..., ... WHERE id = ?
    throw UnimplementedError();
  }

  Future<void> abandonWorkout(int id) {
    // TODO: UPDATE Workout SET status=2, updatedAtUtc=... WHERE id = ?
    throw UnimplementedError();
  }
}
```

`uuid` (pub.dev package) is this track's `Guid.NewGuid()`.

### 6.3 — `WorkoutSampleBuffer`: buffer in memory, flush in one transaction

```dart
typedef WorkoutSampleRow = ({
  int elapsedSec, double? speedKph, double? distanceM,
  int? calories, int? heartRate, int flags,
});

class WorkoutSampleBuffer {
  final _pending = <WorkoutSampleRow>[];
  int get pendingCount => _pending.length;

  void add(WorkoutSampleRow sample) => _pending.add(sample);

  /// Writes every buffered sample for one workout in a single transaction,
  /// clears the buffer. Idempotent — flushing the same rows twice must not
  /// duplicate data.
  Future<int> flush(Database db, int workoutRowId) async {
    if (_pending.isEmpty) return 0;
    final batch = db.batch();
    for (final s in _pending) {
      batch.insert(
        'WorkoutSample',
        {
          'workoutRowId': workoutRowId,
          'elapsedSec': s.elapsedSec,
          'speedKph': s.speedKph,
          'distanceM': s.distanceM,
          'calories': s.calories,
          'heartRate': s.heartRate,
          'flags': s.flags,
        },
        conflictAlgorithm: ConflictAlgorithm.replace, // this track's "INSERT OR REPLACE"
      );
    }
    await batch.commit(noResult: true); // one transaction for the whole batch
    final flushed = _pending.length;
    _pending.clear();
    return flushed;
  }
}
```

`Database.batch()` is `sqflite`'s built-in equivalent of reusing one
prepared `SqliteCommand` inside a transaction — every statement in a batch
commits as one transaction when `.commit()` runs, so this doesn't need the
manual "add parameters once, reassign per row" ceremony the raw
`Microsoft.Data.Sqlite` version needed. `ConflictAlgorithm.replace` is
`INSERT OR REPLACE`, giving the same idempotent-flush guarantee.

Test: add 60 samples, flush, assert 60 rows and `pendingCount == 0`; flush an
empty buffer returns 0 with no transaction opened; flush the same rows twice
and assert the row count is unchanged, not doubled; build 720 samples and
assert the flush completes in under 1 second (same target as the original —
if it's slow, check that `batch()` is actually being used instead of one
`insert()` call per row awaited individually).

### 6.4 — Startup recovery

Call `findInProgressWorkouts()` once at app start, right after opening the
database. Simplest defensible policy: `abandonWorkout()` every row found.
More ambitious: compute a summary from whatever samples made it to disk and
`completeWorkout()` with that instead. Either is acceptable; record which
one and why, same "make the call, then say why" pattern used throughout this
project.

### 6.5 — Flush triggers

Four triggers must call `flush()`: a periodic `Timer.periodic` (30–60s while
a workout is active, cancelled on finish), workout pause, workout finish,
and Flutter's `AppLifecycleState.paused` (the `WidgetsBindingObserver`
equivalent of MAUI's `OnSleep` — fires when the app is backgrounded).

### 6.6 — Counter semantics: shape only, blocked on V1

Same placeholder-not-logic approach as the original track and as
Phase 04 — do not fill in reset-detection logic until `PHASE-00-FINDINGS.md`
V1 resolves.

## Query patterns

Same SQL as the original — aggregate in SQL, not in Dart over a loaded
table; downsample in SQL (`WHERE elapsedSec % $n = 0`), not by loading
everything and thinning it client-side. `sqflite`'s `rawQuery` runs this
verbatim:

```dart
final rows = await db.rawQuery('''
  SELECT
    date(datetime(startedAtUtc, '+' || startOffsetMinutes || ' minutes')) AS localDay,
    COUNT(*) AS workouts, SUM(distanceMeters) AS totalMeters,
    SUM(calories) AS totalCalories, SUM(durationSeconds) AS totalSeconds
  FROM Workout
  WHERE status = 1 AND startedAtUtc >= ? AND startedAtUtc < ?
  GROUP BY localDay ORDER BY localDay
''', [fromUtc, toUtc]);
```

## Performance targets

Identical to the original — see
[`../../../phases/phase-06-recording-schema/README.md#performance-targets`](../../../phases/phase-06-recording-schema/README.md#performance-targets).
Seed a synthetic 5,000-workout database in Phase 12 and measure; don't
assume.

## Acceptance

- [ ] No data loss across app restart
- [ ] Sample cadence correct within ±1s
- [ ] 720-row flush under 1s
- [ ] No derived value (statistic, PR) stored anywhere
