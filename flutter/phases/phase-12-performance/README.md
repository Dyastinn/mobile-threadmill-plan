# Phase 12 — Performance (Flutter track)

> Measure, then fix what is actually slow. Not the other way round.

**Hardware:** required · **Size:** S · **Blocked by:** Phase 11

## Goal

Same as the original track: real numbers next to every performance claim.
A synthetic 5,000-workout database, a timing harness around the database's
hot paths, an hour of memory/CPU/battery data from a real workout, and an
honest "is this actually slow, and if so, where."

## The concept

Unchanged from the original — Knuth's "premature optimization is the root
of all evil," applied literally: optimize only what a measurement shows is
slow. `EXPLAIN QUERY PLAN` answers "is this actually a missing index" for
the cost of one command, before any code changes. A caching layer added
"just in case" is pure cost with no matching benefit if the real problem
turns out to be a one-line index fix.

**Flat matters more than low.** A memory graph flat at 160 MB is healthy; one
creeping from 90 MB to 150 MB over the same hour is a leak — a `Stream`
subscription that's never cancelled, a buffer that never gets flushed. This
matters slightly differently in Dart than in the original C# track: Dart is
garbage-collected like .NET, but a `StreamController` with a still-active
subscription keeps its source alive the same way a forgotten C# event
subscription does — the specific leak shape to watch for translates
directly.

## Reference docs

| Topic | URL |
|---|---|
| Flutter DevTools overview | https://docs.flutter.dev/tools/devtools/overview |
| DevTools Performance view | https://docs.flutter.dev/tools/devtools/performance |
| DevTools Memory view | https://docs.flutter.dev/tools/devtools/memory |
| DevTools CPU Profiler | https://docs.flutter.dev/tools/devtools/cpu-profiler |
| `flutter run --profile` / profile mode | https://docs.flutter.dev/testing/build-modes |
| Android Studio Profiler (native/JNI cross-check) | https://developer.android.com/studio/profile |
| `dumpsys batterystats` | https://developer.android.com/topic/performance/power/setup-battery-historian |
| SQLite `EXPLAIN QUERY PLAN` | https://sqlite.org/eqp.html |

## A genuine Flutter/Linux advantage worth naming

Unlike the original track's `dotnet-trace`/`dotnet-gcdump`/`dotnet-dsrouter`
pipeline — which needs a router process forwarding a diagnostic connection
from phone to dev machine, and whose GUI consumers (Visual Studio, PerfView)
are Windows-centric — **Flutter DevTools is a web app that runs identically
on Linux, macOS, and Windows**, connecting to the running app's VM service
over a local port. `flutter run --profile` prints a DevTools URL directly;
no router, no platform-specific viewer. This is the same category of
Linux-parity win that motivated the framework switch in the first place (see
`../../README.md`), showing up again here.

## Measure

Same targets as the original track — see
[`../../../phases/phase-12-performance/README.md#measure`](../../../phases/phase-12-performance/README.md#measure)
for the full table (memory, CPU, battery, latency, and the five row-count-dependent
database targets). None of the numeric targets are framework-specific.

## Step-by-step

### 0. Always measure a profile (or release) build

`flutter run --profile` — never `--debug`. Debug builds run with JIT and
extra instrumentation that can make a method look many times slower than
the shipped app; this is the direct Dart-track equivalent of the original's
"never time a Debug/interpreted build" rule. Use `--release` for the actual
one-hour endurance-style measurements in step 4 onward, where you want the
real shipped performance rather than profiler-attachable output; `--profile`
for anything where you need DevTools attached (steps 3, 5).

### 1. Seed a synthetic 5,000-workout database (you write this)

A `dart run`-able script under `myhi_companion_core/tool/`, inserting 5,000
`Workout` rows plus realistic `WorkoutSample` rows, spread across at least
two years and a DST boundary, deterministic (fixed random seed), written
through the same `sqflite` batch-insert pattern Phase 06 established — don't
hand-roll a second insert path just for the seeder. Keep the generated `.db`
file out of git.

### 2. A timing harness for the query/insert targets (you write this)

```dart
Future<T> timeAsync<T>(String label, Future<T> Function() operation) async {
  final stopwatch = Stopwatch()..start();
  final result = await operation();
  stopwatch.stop();
  print('$label: ${stopwatch.elapsedMilliseconds} ms');
  return result;
}
```

One call site per row in the Measure table's bottom five rows, run against
the seeded database. For any number over target, run `EXPLAIN QUERY PLAN
<query>` via `db.rawQuery('EXPLAIN QUERY PLAN ' + query)` before changing
anything — confirms whether the `Workout(startedAtUtc)` index from Phase
06/10 is actually being used or the query is silently doing a full table
scan.

### 3. Measure memory over a 1-hour workout — `[HUMAN]`

Two independent methods, same "catch different things" reasoning as the
original:

**Method A — Flutter DevTools Memory view:** `flutter run --profile`, open
the printed DevTools URL, go to the Memory tab, start the workout right
after attaching so the whole hour is captured, let it run screen-locked
(Phase 07's foreground service keeps it alive), watch for a flat vs. rising
trend, take a snapshot near the start and again near the end and diff
object counts by type — a type whose count kept climbing is the leak.

**Method B — Android Studio Profiler, for a native/JNI cross-check:** same
procedure as the original track's Method A (attach to the running process,
Memory timeline, heap dumps at start/end) — this one sees native BLE-plugin
allocations DevTools' Dart-heap view can't.

**Record:** peak MB, end-of-hour MB, a "flat" or "rising, ~X MB/hour" verdict.

### 4. Measure CPU during active recording

`flutter run --profile`, connect and start a workout, get into steady
"actively recording" state, open DevTools' CPU Profiler tab, record a
representative minute or two, stop, and look at the flame chart — width is
time spent, not call order. Note anything unexpectedly hot; there's no
numeric target here, same as the original.

### 5. Measure battery drain per hour of workout — `[HUMAN]`

Identical `adb`-based procedure to the original track — this is Android
platform tooling, unaffected by the UI framework:

```bash
adb shell dumpsys batterystats --reset
# run the one-hour locked workout
adb shell dumpsys batterystats --charged <your.package.name>
```

Record the percentage drop over the hour.

### 6. Measure BLE notification → UI latency

Timestamp the moment a `TreadmillSample` is emitted on `TreadmillService.samples`
and the moment the corresponding Riverpod state update actually lands (e.g.
inside the notifier's listener callback), log the delta. Run a few minutes
of a live or fake-service workout, note a typical value — worth a closer
look if consistently over ~250 ms given the 4 Hz throttle from Phase 03.

### 7. Roll it up

Fill in every row of the Measure table with what steps 2–6 produced, and
record pass/fail against each target explicitly before calling the phase
done.

## Tests

- Seed a synthetic 5,000-workout database and measure. Do not assume.
- `[HUMAN]` one-hour workout with DevTools attached.
- Every screen opens in under 500 ms on the seeded database.

## Acceptance

- [ ] Flat memory over one hour
- [ ] Statistics queries under 200 ms at 5,000 workouts
