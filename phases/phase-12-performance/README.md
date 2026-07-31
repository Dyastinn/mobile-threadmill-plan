# Phase 12 — Performance

> Measure, then fix what is actually slow. Not the other way round.

**Hardware:** required · **Size:** S · **Blocked by:** Phase 11

---

## Goal

Put real numbers next to every performance claim in this project instead of a
feeling. By the end of this phase you'll have: a synthetic 5,000-workout database
you can reuse for every future performance question, a timing harness around the
database's hot paths, one hour of memory/CPU/battery data from a real workout, and
an honest answer to "is this actually slow, and if so, where."

This phase is almost entirely **measurement**, not code-writing — see
`../README.md`'s collaboration model. The two small pieces of logic you do write
(the seed generator and the timing harness) follow the usual "you write it, the
agent reviews" pattern. Everything else is process: attaching a profiler, reading
its output, recording what it says.

## Learning goals

- **Why `Release`, never `Debug`, for anything you're timing.** Debug builds run
  with the .NET interpreter (`UseInterpreter=true`) so C# Hot Reload works — that
  alone can make a method look 10–50× slower than it will be in the shipped app.
  Every measurement in this phase is against a `Release` build.
- **The EventPipe diagnostics pipeline** (`dotnet-trace` / `dotnet-gcdump` /
  `dotnet-dsrouter`) — why a phone needs a *router* forwarding a diagnostic
  connection back to your dev machine, when a desktop .NET process would just talk
  to these tools directly.
- **Reading a flame graph** (speedscope format) — width is time spent, not call
  order; a wide bar low in the stack is where the CPU actually is.
- **What a GC dump snapshot is**, and how *comparing two snapshots* taken minutes
  apart — not looking at one in isolation — is what actually reveals a leak: a
  type whose live object count keeps climbing between snapshots is the leak,
  everything else is normal churn.
- **Android's own profiler suite** (CPU, Memory, Energy) as the platform-native
  alternative/complement to the .NET tools above — useful because it sees native
  allocations and system-level battery attribution that EventPipe can't.
- **`EXPLAIN QUERY PLAN`** and why an aggregate query over 5,000 rows can be instant
  or can be 2 seconds depending entirely on whether SQLite is using the index you
  think it's using.
- Why **"flat" beats "small."** A memory graph that's flat at 160 MB is a healthy
  app running a workload. A graph rising from 90 MB toward 150 MB over the same
  hour is a leak that just hasn't run out of room yet — it will, on a longer walk
  or an older phone.

## Reference docs

| Topic | URL |
|---|---|
| .NET MAUI Performance Profiling guide (the primary reference for this whole phase — Android/iOS/Windows workflows, `dotnet-trace`, `dotnet-gcdump`, memory leak diagnosis) | https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/profiling?view=net-maui-10.0 |
| `maui profile` CLI (`maui profile manual` / `maui profile startup`) | https://learn.microsoft.com/en-us/dotnet/maui/developer-tools/cli/profile?view=net-maui-10.0 |
| `dotnet-trace` | https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace |
| `dotnet-gcdump` | https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump |
| `dotnet-dsrouter` | https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dsrouter |
| `dotnet-counters` (live EventCounter values — useful as a lighter-weight companion to a full trace) | https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters |
| Android Studio Profiler overview | https://developer.android.com/studio/profile |
| Android Studio CPU Profiler | https://developer.android.com/studio/profile/cpu-profiler |
| Android Studio Memory Profiler | https://developer.android.com/studio/profile/memory-profiler |
| Profile battery usage with Batterystats and Battery Historian | https://developer.android.com/topic/performance/power/setup-battery-historian |
| `dumpsys` | https://developer.android.com/tools/dumpsys |
| Android app startup/launch time | https://developer.android.com/topic/performance/vitals/launch-time |
| `Microsoft.Data.Sqlite` overview (already in `docs/learning/03-Doc-Links.md`) | https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/ |

## Measure

| Metric | Target |
|--------|--------|
| Memory over a 1-hour workout | under 150 MB, and **flat** |
| CPU during active recording | note it |
| Battery drain per hour of workout | note it |
| BLE notification → UI latency | note it |
| Sample flush duration (60 rows) | < 50 ms |
| Full workout sample insert (720 rows) | < 1 s |
| Workout list page (50 rows) at 5,000 workouts | < 100 ms |
| Weekly aggregate at 5,000 workouts | < 200 ms |
| Single workout chart load (2-hour workout) | < 200 ms |

**The memory slope matters more than the absolute number.** A rising graph means the
sample buffer or an event subscription is leaking, and 140 MB rising is worse than
160 MB flat.

---

## Step-by-step walkthrough

### 0. Install the diagnostic tools

Concrete steps:
1. Install the three global tools (once, on your dev machine — not the phone):
   ```powershell
   dotnet tool install -g dotnet-trace
   dotnet tool install -g dotnet-gcdump
   dotnet tool install -g dotnet-dsrouter
   ```
2. Check versions — this phase's workflow needs **at least `9.0.652701`** of each
   (older versions require running `dotnet-dsrouter` manually as a separate
   process instead of via the `--dsrouter` flag used below):
   ```powershell
   dotnet-trace --version
   dotnet-gcdump --version
   dotnet-dsrouter --version
   ```
   If any are older, `dotnet tool update -g <name>`.
3. Confirm `adb` is on your `PATH` (`adb --version`) — the phone must be connected
   with USB debugging enabled, same setup as `docs/learning/01-Emulator-Setup.md`.

### 1. Always measure a `Release` build

Every command in this phase targets `-c Release`. If a number looks alarming,
the first question is "was this actually a Release build?" before anything else.
**Never ship a build with the diagnostic properties below turned on** — they're
development/test-only and expose extra endpoints.

### 2. Task — seed a synthetic 5,000-workout database (you write this)

**Concept.** Every screen-load and query-time target in the table above is
meaningless without a database big enough to make a slow query actually slow. A
fresh dev database with 12 workouts in it will make every query look instant
regardless of whether the indexes from Phase 10/11 are doing their job. You need a
deterministic generator you can re-run.

**Spec.**
- Creates: a small tool — a `[Fact]`-adjacent xUnit test-project console utility,
  or a `dotnet run`-able project under `src/`, whichever you prefer — that inserts
  5,000 `Workout` rows plus realistic `WorkoutSample` rows (mix of short and
  ~2-hour workouts, so the "2-hour workout chart load" target has something real
  to load).
- Spread workout dates across at least two years and across a DST boundary
  (reuses the Phase 10/11 timezone-bucketing concern — a seeder that only
  generates workouts in July never exercises that code path).
- Deterministic: same seed value (e.g. a fixed `Random` seed) produces the same
  row counts every run, so "5,000 workouts" in this doc always means the same
  dataset.
- Writes through the same `Microsoft.Data.Sqlite` connection factory and the same
  buffered-insert pattern Phase 06/07 already established — don't hand-roll a
  second insert path just for the seeder.

Concrete steps:
1. Decide where the seeder lives — a new console-style entry point is simplest;
   ask at the review checkpoint if you're unsure where it fits given how Phase 06
   structured the data layer.
2. Write the row-generation logic: realistic speed/distance/calorie curves aren't
   needed here (that's Phase 01/03's job) — flat or lightly randomized values are
   fine, this dataset exists to stress *row counts*, not decoding correctness.
3. Run it once, confirm `SELECT COUNT(*) FROM Workout` returns 5000 and
   `WorkoutSample` row counts look plausible (roughly cadence × duration per
   workout, per the 5-second cadence from `14-Database.md`).
4. Keep the generated `.db` file out of git (it's large and reproducible) — add it
   to `.gitignore` if it isn't covered already.

### 3. Task — a timing harness for the query/insert targets (you write this)

**Concept.** The five row-count-dependent targets in the Measure table (sample
flush, full insert, list page, weekly aggregate, chart load) all need the same
thing: run the operation against the seeded database, time it, compare to the
target. Wrapping each in `System.Diagnostics.Stopwatch` by hand five times is
fine for a one-off check, but you'll re-run this after every future schema or
query change, so it's worth a small reusable harness.

**Spec.**
- A small helper — `TimeAsync(string label, Func<Task> operation)` or similar —
  that starts a `Stopwatch`, awaits the operation, stops it, and writes
  `label: elapsed ms` to the console/log.
- One call site per row in the Measure table's bottom five rows, each running
  against the seeded 5,000-workout database from step 2.
- `EXPLAIN QUERY PLAN <query>` run manually (via any SQLite browser, or
  `Microsoft.Data.Sqlite`'s `ExecuteReader` against the literal `EXPLAIN QUERY
  PLAN ...` string) against the weekly-aggregate query if its timing comes back
  high — confirms whether it's actually using the `Workout(StartedAtUtc)` index
  from Phase 10/11 or silently doing a full table scan.

Concrete steps:
1. Write the `Stopwatch`-based helper — see
   [`Stopwatch` class docs](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch)
   if the API is unfamiliar.
2. Call it around: a 60-sample flush, a 720-row full insert, the workout list
   query (50 rows), the weekly aggregate query, and a single 2-hour workout's
   chart data load.
3. Run it against the seeded database, record the five numbers next to the
   Measure table above.
4. For any number over target, run `EXPLAIN QUERY PLAN` on that specific query
   before changing anything — the fix is almost always "add/fix an index," and
   guessing which one without the query plan wastes time.

### 4. Measure memory over a 1-hour workout — `[HUMAN]`

Two independent methods — do both, they catch different things (EventPipe sees
managed .NET allocations; Android's profiler also sees native/JNI memory).

**Method A — Android Studio Profiler:**
1. Build a **profileable** `Release` APK (Android Studio does this automatically
   for a `Release` variant on a device running Android 10+) and install it.
2. In Android Studio: **View → Tool Windows → Profiler**, or the Profiler tab.
3. Select **"Attach to a running process"** and pick the MyHi Companion process
   from the dropdown — no need to launch it from Android Studio.
4. Open the **Memory** timeline. Start the treadmill workout in the app right
   after attaching, so the whole hour is captured.
5. Let it run the full hour, screen locked per the Phase 07 foreground-service
   setup. Watch (or come back and review) the memory timeline: is it flat after
   the initial ramp-up, or does it keep climbing?
6. Take a heap dump (camera icon in the Memory timeline) near the start and again
   near the end; compare object counts by class — a class whose count roughly
   doubled over the hour is the leak.

**Method B — `dotnet-gcdump`, for a managed-memory cross-check:**
1. Build and deploy with the diagnostic properties on (physical device — adjust
   `DiagnosticAddress` if using the emulator instead, per the table below):
   ```powershell
   dotnet build -t:Run -c Release -f net10.0-android -p:DiagnosticAddress=127.0.0.1 -p:DiagnosticPort=9000 -p:DiagnosticSuspend=false -p:DiagnosticListenMode=connect
   ```
2. Roughly every 15 minutes during the hour, in a separate terminal:
   ```powershell
   dotnet-gcdump collect --dsrouter android
   ```
   This produces a `.gcdump` file each time (default: current directory).
3. After the hour, open the first and last `.gcdump` in Visual Studio (or
   [PerfView](https://github.com/microsoft/perfview) if you're not on Windows
   with VS) and diff them — same "which type's live count kept climbing"
   question as Method A's heap dumps, from the managed side.

*(Emulator variant of the build command, if you're not on a physical phone:
`-p:DiagnosticAddress=10.0.2.2` instead of `127.0.0.1` — but note the whole point
of this measurement is the real device on the real belt, so the emulator path is
for rehearsing the tool workflow only, not for recording the actual hour.)*

**Record:** peak MB, end-of-hour MB, and a plain "flat" or "rising, ~X MB/hour"
verdict.

### 5. Measure CPU during active recording

1. Run `maui profile manual --framework net10.0-android` from the repo root (or
   pass `--project` if not run from the project directory). This launches the app
   in `Release` **without** suspending it at startup.
2. In the app, connect to the treadmill and start a workout — get it into the
   steady "actively recording" state you actually want to profile.
3. Back in the terminal, press **Enter** to attach `dotnet-trace` and start
   collection.
4. Let it record through a representative stretch of active recording (a minute
   or two is enough — this isn't the hour-long test).
5. Press **Enter** again to stop and finalize. Add `--format speedscope` to the
   original command if you want a file viewable at
   [speedscope.app](https://speedscope.app/) instead of the Visual-Studio-only
   `.nettrace` default.
6. Open the trace, sort by self time, and note which methods are actually hot —
   this is the "note it" measurement, there's no numeric target, just "does
   anything unexpected show up."

### 6. Measure battery drain per hour of workout — `[HUMAN]`

1. Fully charge the phone, then note the starting battery percentage
   (**Settings → Battery**, or `adb shell dumpsys battery`).
2. Reset batterystats so this run isn't polluted by earlier testing:
   ```powershell
   adb shell dumpsys batterystats --reset
   ```
3. Run the same one-hour locked workout as step 4 (you can do this as the same
   session — batterystats doesn't interfere with the profilers above).
4. Pull the stats:
   ```powershell
   adb shell dumpsys batterystats > batterystats.txt
   ```
   For just this app's numbers:
   ```powershell
   adb shell dumpsys batterystats --charged com.<yourpackageid>
   ```
5. Optional but worth it if the number looks bad: feed `batterystats.txt` into
   [Battery Historian](https://developer.android.com/topic/performance/power/setup-battery-historian)
   for a visual timeline of what was drawing power when.
6. Record the percentage drop over the hour as your "battery drain per hour"
   figure.

### 7. Measure BLE notification → UI latency

**Concept.** This is a small logic addition, not a UI change — timestamp the
moment a `TreadmillSample` is raised by `ITreadmillService` and the moment the
bound property update actually reaches the UI thread, then log the delta.

Concrete steps:
1. At the point `SampleReceived` fires (or where the ViewModel receives it), grab
   `DateTime.UtcNow` (or a `Stopwatch` if you want sub-millisecond resolution).
2. At the point the ViewModel's bound property setter actually runs (e.g. inside
   the `[ObservableProperty]`-generated setter, or immediately before it via a
   `partial void On...Changing` hook), grab the time again and log the
   difference.
3. Run a few minutes of a live or fake-service workout, watch the logged deltas
   in `adb logcat` or the rolling log file from Phase 00.
4. Record a typical value — there's no target here, just "note it," but a number
   consistently over ~250 ms would be worth a closer look given the 4 Hz UI
   throttle Phase 03 already applies.

### 8. Roll it up

Fill in every row of the Measure table with what steps 3–7 produced, and write
down the pass/fail against each target explicitly before calling the phase done.

---

## Tests

- Seed a synthetic 5,000-workout database and measure. Do not assume.
- `[HUMAN]` One-hour workout with the profiler attached
- Every screen opens in under 500 ms on the seeded database

## Acceptance

- [ ] Flat memory over one hour
- [ ] Statistics queries under 200 ms at 5,000 workouts
