# Database Schema

SQLite, accessed via `Microsoft.Data.Sqlite`. Migration-based, forward-only.

---

## Design decisions

### Dual keys on `Workout`

| Column | Purpose |
|--------|---------|
| `Id` INTEGER PK AUTOINCREMENT | Local physical key. Used for all foreign keys and joins. |
| `WorkoutId` TEXT UNIQUE (GUID) | Portable business key. Used only at backup/restore boundaries. |

`WorkoutSample` joins on the workout thousands of rows at a time; joining on a
36-character TEXT GUID instead of a rowid is measurably slower and larger. The GUID is
only needed when data crosses devices.

**Integer PKs must never be used for de-duplication across devices.** The same integer
means different workouts on two phones. De-duplicating on it silently drops unrelated
workouts. This is the single most likely cause of silent data loss in this project.

### Telemetry cadence: fixed 5 seconds

| Cadence | Rows / 1 h | Storage / workout | 250 workouts/yr |
|---------|-----------|-------------------|-----------------|
| 1 Hz | 3,600 | ~180 KB | ~45 MB/yr |
| **5 s** | **720** | **~36 KB** | **~9 MB/yr** |
| On-change | ~5–20 | ~1 KB | ~0.3 MB/yr |

On-change is tempting because treadmill speed is a step function, but heart rate
varies continuously, so on-change degenerates back to per-sample for HR while adding
interpolation logic on the read side. Fixed cadence is simpler to write, simpler to
chart, and 9 MB/year is irrelevant on a phone.

Samples are the part you'll regret losing. A summary you could approximately
reconstruct from memory; a speed curve you cannot.

### All times are UTC

`StartedAtUtc` and `EndedAtUtc` are UTC. `StartOffsetMinutes` stores the local UTC
offset at the time of the workout.

Local-time-without-offset shifts every daily and weekly bucket when a backup is
restored in another timezone, or across a DST boundary. Store UTC + offset; compute
the local day from both.

### All measurements are metric

Distance in metres, speed in km/h, energy in kcal. **Unit conversion happens at
display time only and never touches the database.** An imperial toggle that writes
converted values corrupts the dataset permanently.

### Derived data is never stored

Statistics and personal records are computed from `Workout` and `WorkoutSample` on
demand. They are not columns, not tables, and not exported in backups.

If a query becomes slow at scale, add a cache table that is explicitly rebuildable and
rebuild it after any import, but do not start there.

---

## Schema

### `Workout`

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

### `WorkoutSample`

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

**`HeartRate` source (v2.1):** prefer `0x2A37` from the standard Heart Rate Service
`180D` over the FTMS field: a dedicated characteristic is less likely to be mangled by
a vendor shim. Store `NULL` when the user is not gripping the sensors; do not carry the
last value forward, or the average will be computed over fabricated data.

If the probe finds handgrip readings unusable, keep writing the column but hide the
metric in the UI. Recording a column you don't display is cheap and reversible;
back-filling one you never recorded is not.

`ON DELETE CASCADE` requires `PRAGMA foreign_keys = ON` on every connection.
`Microsoft.Data.Sqlite` does not enable it by default.

### `Device`

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

If the probe procedure (Part F2) finds the device uses a **random resolvable address**,
the MAC is not stable and `UX_Device_MacAddress` is wrong. Match on name instead.
Resolve before implementing.

### `SchemaVersion`

```sql
CREATE TABLE SchemaVersion (
    Version    INTEGER NOT NULL PRIMARY KEY,
    AppliedUtc TEXT    NOT NULL
);
```

Forward-only migrations applied at startup inside a transaction. Never edit a shipped
migration; add a new one.

---

## Connection configuration

```sql
PRAGMA journal_mode = WAL;      -- concurrent read during sample writes
PRAGMA foreign_keys = ON;       -- required for CASCADE
PRAGMA synchronous = NORMAL;    -- safe with WAL, much faster writes
PRAGMA busy_timeout = 5000;
```

WAL matters here: the foreground service writes samples while the UI reads for charts.

---

## Sample write strategy

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

---

## Query patterns

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
loading everything and thinning it in C#.

---

## Performance targets

| Operation | Target | Dataset |
|-----------|--------|---------|
| Sample flush (60 rows) | < 50 ms | any |
| Full workout sample insert (720 rows) | < 1 s | any |
| Workout list page (50 rows) | < 100 ms | 5,000 workouts |
| Weekly aggregate | < 200 ms | 5,000 workouts |
| Single workout chart load | < 200 ms | 2-hour workout |

Seed a synthetic 5,000-workout database in Phase 13 and measure. Do not assume.

---

## Backup mapping

`WorkoutId` and `DeviceUid` are the identities used in backup files. Integer `Id`
values are **not exported** and are reassigned on import.

Exported: `Workout`, `WorkoutSample`, `Device`, plus settings from `Preferences`.
Not exported: `SchemaVersion`, statistics, personal records (all derived or local).

See `15-Backup-Restore.md`.
