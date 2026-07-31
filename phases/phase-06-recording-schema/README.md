# Phase 06 — Workout Recording & Schema

**Hardware:** none for development · **Size:** M · **Blocked by:** Phase 04
**Hard dependency:** V1 counter semantics. **Do not start without it.**

---

## Goal

Persist workouts and telemetry durably. Full schema in `../../14-Database.md` — read it
before writing a migration.

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

## Implementation requirements

- **Buffer samples in memory; flush every 30–60 s inside a single transaction.** Do not
  insert per sample — 720 individual transactions per workout is needless battery and
  flash wear. Worst-case crash loses under a minute.
- Flush on: the timer, workout pause, workout finish, and `OnSleep`.
- Reuse one prepared command with reassigned parameters. Building a command per row is
  significantly slower, and if the 720-row target is missed this is almost always why.
- `INSERT OR REPLACE` makes a flush idempotent, so a retried flush after partial
  failure is safe.
- **Write the workout header at workout start** with `Status = 0` (in progress), not at
  the end. A crash then leaves a recoverable partial workout rather than nothing.
- On app start, detect `Status = 0` rows and either recover or discard per policy. The
  partial index on `Status = 0` makes this query trivial.
- `PRAGMA foreign_keys = ON` on **every** connection — `Microsoft.Data.Sqlite` does not
  enable it by default and `ON DELETE CASCADE` silently does nothing without it.
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

Whatever V1 said. Per-session → record directly. Cumulative → delta against a
workout-start baseline, with mid-workout reset detection. This is the reason the phase
is blocked on V1 rather than merely informed by it.

---

## Tests

- 10 workouts via `FakeTreadmillService`, restart, all present with correct sample counts
- Kill the app mid-workout; on restart the partial workout is handled per policy
- **Throughput: 1 hour of samples (720 rows) inserts in under 1 s**
- Flush of 60 rows under 50 ms
- Idempotent flush: run the same flush twice, row count unchanged
- Cascade delete removes samples (proves `foreign_keys` is actually on)
- Timezone: a workout at 23:30 local stores UTC + offset and reconstructs the right
  local day
- `[HUMAN]` One real 20-minute workout; summary matches the treadmill console

## Acceptance

- [ ] No data loss across app restart
- [ ] Sample cadence correct within ±1 s
- [ ] 720-row insert under 1 s
- [ ] No derived value (statistic, PR) stored anywhere
