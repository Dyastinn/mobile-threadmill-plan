# Phase 12 — Performance

> Measure, then fix what is actually slow. Not the other way round.

**Hardware:** required · **Size:** S · **Blocked by:** Phase 11

---

## Goal

Put real numbers next to every performance claim in this project instead of a
feeling. By the end of this phase you'll have: a synthetic 5,000-workout database
you can reuse for every future performance question, a timing harness around the
database's hot paths that runs on the phone, one hour of memory/CPU/battery data from
a real workout, and an honest answer to "is this actually slow, and if so, where."

This phase is almost entirely **measurement**, not code-writing. See
`../README.md`'s collaboration model. The two small pieces of logic you do write
(the seed generator and the timing harness) follow the usual "you write it, the
agent reviews" pattern. Everything else is process: running a build, reading a tool's
output, recording what it says. With the phone on wireless adb, the agent can run the
`adb`/`dumpsys` commands below for you and tabulate the output.

### Understanding what you're building (read this before the tasks)

Profiling before touching code is the same discipline as a doctor running blood
work before writing a prescription: symptoms alone don't say which organ is
failing. "The workout list feels slow" tells you nothing about whether the cause
is a missing SQLite index, a chatty BLE handler re-rendering the dashboard, or a
listener that never unsubscribes slowly growing memory. This phase exists because
Phases 06-11 built the actual system, the database, the BLE stack, the recording
pipeline, without ever running it against a realistic 5,000-workout dataset or a real
one-hour session. The seeded database from step 2 and the measurements in steps 4-7
are this project's blood test: they turn "feels slow" into an actual number next to
an actual target in the Measure table.

**Flat matters more than low.** 160 MB that stays 160 MB for the whole hour is a
healthy app running a real workload. 90 MB that creeps to 150 MB over that same
hour is a leak: an `onSample` subscription that never unsubscribes, a buffer that
grows and never gets flushed, a capture log array that's never trimmed. A two-hour
workout or an older phone turns a barely-noticeable slope into a crash. That's why
the Measure table records a slope ("flat" vs. "rising, ~X MB/hour"), not just a single
end-of-hour number.

**Why not just add the obvious optimizations up front.** The simpler-sounding plan
is to skip all this measurement and apply the fixes performance work "usually" needs:
`React.memo` every component, cache the workout list, wrap every query in a
memoization layer. Each of those is real code, with its own bugs and its own
maintenance cost, and every one of them is aimed at a guess. If the weekly-aggregate
query is actually slow because it's missing the `Workout(StartedAtUtc)` index, a
one-line fix, then the caching layer added "just in case" is pure cost with no
matching benefit: more code, more places a bug can hide, and still a query that's slow
the first time it runs. `EXPLAIN QUERY PLAN` in step 3 answers "is this actually the
index problem" directly, for the cost of one command, before any code changes.
Measuring first isn't the cautious, slower option here. It's the cheaper one, because
it replaces five possible fixes with the one that's actually needed.

**The pattern, named plainly.** This is Donald Knuth's "premature optimization is
the root of all evil," applied literally rather than as a slogan: optimize only
what a measurement has shown to be slow. The cost is real: running the measurements
takes longer than just changing code and hoping. The payoff, specific to this project,
is that every optimization this phase produces is provably necessary, not
speculative, which matters because Phase 12 is the last chance before Phase 13's
polish pass to catch a problem while it's still cheap to fix. An obviously quadratic
loop spotted by eye in code review doesn't need a profiler session to justify fixing
it. The full measurement ceremony earns its keep when the cause isn't obvious, not for
every single change.

## Learning goals

- **Why a release build, never the development build, for anything you're timing.**
  The development build runs JavaScript in development mode: extra checks, warnings,
  and Strict Mode double-invoking effects, with the bundle served over the network by
  Metro. That alone can make code look several times slower than it will be in the
  shipped app. Every number in this phase comes from
  `npx expo run:android --variant release`.
- **Two kinds of memory.** The JavaScript heap (your objects, closures, listeners) is
  one slice; the native side (React Native itself, ble-manager, SQLite, images) is the
  rest. `dumpsys meminfo` sees the whole process; a JavaScript heap snapshot sees only
  your slice, but tells you *which objects* are in it.
- **What a heap snapshot comparison is**, and how *comparing two snapshots* taken
  minutes apart, not looking at one in isolation, is what actually reveals a leak: an
  object type whose count keeps climbing between snapshots is the leak, everything else
  is normal churn.
- **`EXPLAIN QUERY PLAN`** and why an aggregate query over 5,000 rows can be instant
  or can be 2 seconds depending entirely on whether SQLite is using the index you
  think it's using.
- **Why "flat" beats "small."** A memory graph that's flat at 160 MB is a healthy
  app running a workload. A graph rising from 90 MB toward 150 MB over the same
  hour is a leak that just hasn't run out of room yet.

## Reference docs

| Topic | URL |
|---|---|
| React Native performance overview (dev vs. release, what "slow" usually means) | https://reactnative.dev/docs/performance |
| React Native DevTools (Memory heap snapshots, Performance panel) | https://reactnative.dev/docs/react-native-devtools |
| Expo debugging tools | https://docs.expo.dev/debugging/tools/ |
| Android Studio Profiler overview | https://developer.android.com/studio/profile |
| `dumpsys` (`meminfo`, `batterystats`, `gfxinfo`) | https://developer.android.com/tools/dumpsys |
| Profile battery usage with Batterystats and Battery Historian | https://developer.android.com/topic/performance/power/setup-battery-historian |
| SQLite `EXPLAIN QUERY PLAN` | https://www.sqlite.org/eqp.html |
| `adb` | https://developer.android.com/tools/adb |

## Measure

| Metric | Target |
|--------|--------|
| Memory over a 1-hour workout (`TOTAL PSS`) | under 150 MB, and **flat** |
| CPU during active recording | note it |
| Battery drain per hour of workout | note it |
| BLE notification → screen update latency | note it |
| Sample flush duration (60 rows) | < 50 ms |
| Full workout sample insert (720 rows) | < 1 s |
| Workout list page (50 rows) at 5,000 workouts | < 100 ms |
| Weekly aggregate at 5,000 workouts | < 200 ms |
| Single workout chart load (2-hour workout) | < 200 ms |

**The memory slope matters more than the absolute number.** A rising graph means the
sample buffer or an event subscription is leaking, and 140 MB rising is worse than
160 MB flat. (150 MB is a starting target for a React Native app on this phone, not a
measured baseline. If the first flat reading sits at 170 MB, record it, and treat the
*slope* as the pass/fail.)

---

## Step-by-step walkthrough

### 0. Tools

Concrete steps:
1. `adb` on your `PATH` (`adb --version`) and the phone paired over wireless
   debugging (`docs/learning/01-Device-And-Emulator-Setup.md`). Unplugged, because
   charging changes power behaviour and the battery numbers are meaningless on a cable.
2. React Native DevTools: with the development build running and `npx expo start`
   open, press `j` in the terminal. It opens in Chrome/Edge. Only for the heap-snapshot
   comparison in step 4B; never for timing.
3. Optional: Android Studio, only if `dumpsys` and DevTools leave a question
   unanswered (step 5).

### 1. Always measure a release build

Every timing in this phase is against `npx expo run:android --variant release`. If a
number looks alarming, the first question is "was this actually a release build?"
before anything else. The benchmark screen in step 3 should print the build type on
screen (`__DEV__` is `false` in a release build) so a screenshot of the results can't
be ambiguous.

### 2. Task — seed a synthetic 5,000-workout database (you write this)

**Concept.** Every screen-load and query-time target in the table above is
meaningless without a database big enough to make a slow query actually slow. A
fresh database with 12 workouts in it will make every query look instant regardless
of whether the indexes from Phase 06 are doing their job. You need a deterministic
generator you can re-run.

**Spec.**
- Creates: `src/core/data/seed/seedDatabase.ts`, a pure function
  `seedDatabase(db: Db, options: { workouts: number; seed: number }): Promise<void>`
  that inserts `Workout` rows plus realistic `WorkoutSample` rows (mix of short and
  ~2-hour workouts, so the "2-hour workout chart load" target has something real to
  load). It lives in `core/` so it runs both in Node (against `nodeSqliteDb`, fast,
  for a quick check) and on the phone (against the real database).
- Spread workout dates across at least two years and across a DST boundary (reuses
  the Phase 10 timezone-bucketing concern; a seeder that only generates workouts in
  July never exercises that code path). Include some negative `StartOffsetMinutes`.
- Deterministic: same seed value produces the same rows every run, so "5,000
  workouts" in this doc always means the same dataset. `Math.random` can't be seeded;
  write a ten-line seeded generator (a "mulberry32" is the usual choice).
- Writes through the same `WorkoutSampleBuffer` from Phase 06 for the samples. Don't
  hand-roll a second insert path just for the seeder.

Concrete steps:
1. Write the row-generation logic: realistic speed/distance/calorie curves aren't
   needed here (that's Phase 01/03's job). Flat or lightly randomized values are fine;
   this dataset exists to stress *row counts*, not decoding correctness.
2. Run it in Node once (`seedDatabase.test.ts` with a smaller count is enough), confirm
   `SELECT COUNT(*) FROM Workout` returns what you asked for and `WorkoutSample` counts
   look plausible (roughly duration ÷ 5 per workout).
3. On the phone, the benchmark screen (step 3) seeds into a **separate database file**
   (`myhi-bench.db`), never `myhi.db`. Your real history must never share a file with
   5,000 fake workouts.

### 3. Task — a timing harness for the query/insert targets (you write the harness; the screen is UI)

**Concept.** The five row-count-dependent targets in the Measure table (sample flush,
full insert, list page, weekly aggregate, chart load) all need the same thing: run the
operation against the seeded database, on the phone, time it, compare to the target.
Wrapping each in `performance.now()` by hand five times is fine for a one-off check,
but you'll re-run this after every future schema or query change, so it's worth a
small reusable harness.

**Spec.**
- `src/core/perf/timeAsync.ts`:
  `timeAsync(label: string, operation: () => Promise<unknown>, runs = 5): Promise<{ label: string; medianMs: number; maxMs: number }>`.
  Run the operation several times and report the median: the first run includes cold
  caches and is rarely representative. `performance.now()` is available in React
  Native and in Node.
- One call per row in the Measure table's bottom five rows, each against the seeded
  benchmark database.
- `EXPLAIN QUERY PLAN <query>` for any query whose number comes back over target:
  `db.all('EXPLAIN QUERY PLAN SELECT ...')` returns rows whose `detail` column says
  `SEARCH Workout USING INDEX IX_Workout_StartedAtUtc ...` (good) or `SCAN Workout`
  (a full table scan).

The benchmark screen (UI, written for you), `src/features/diagnostics/BenchmarkScreen.tsx`,
reachable from the Diagnostics list:

```tsx
import { useState } from 'react';
import { ScrollView } from 'react-native';

import { runBenchmarks, type BenchmarkResult } from './runBenchmarks';
import { Button } from '@/features/shared/theme/Button';
import { Screen } from '@/features/shared/theme/Screen';
import { Surface } from '@/features/shared/theme/Surface';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';

export function BenchmarkScreen() {
  const [results, setResults] = useState<BenchmarkResult[]>([]);
  const [running, setRunning] = useState(false);

  async function run() {
    setRunning(true);
    try {
      setResults(await runBenchmarks());
    } finally {
      setRunning(false);
    }
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={{ padding: spacing.lg, gap: spacing.md }}>
        <Text variant="caption">Build: {__DEV__ ? 'DEVELOPMENT (numbers not valid)' : 'release'}</Text>
        <Button label={running ? 'Running...' : 'Seed (if needed) and run'} onPress={() => void run()} disabled={running} />
        {results.map((r) => (
          <Surface key={r.label} style={{ padding: 14, gap: 2 }}>
            <Text>{r.label}</Text>
            <Text variant="caption">
              median {r.medianMs.toFixed(1)} ms · max {r.maxMs.toFixed(1)} ms · target {r.targetMs} ms ·{' '}
              {r.medianMs <= r.targetMs ? 'PASS' : 'OVER'}
            </Text>
          </Surface>
        ))}
      </ScrollView>
    </Screen>
  );
}
```

`runBenchmarks.ts` is yours: open (or create and seed) `myhi-bench.db` through the same
opener as the real database, call `timeAsync` five times with the five operations and
their targets, return the list.

Concrete steps:
1. Write `timeAsync` and a test for it (fake operations with known delays, using real
   timers; the median of `[10, 12, 50, 11, 13]` ms is `12`).
2. Write `runBenchmarks.ts`. Run it on the release build; screenshot the results.
3. Record the five numbers next to the Measure table above.
4. For any number over target, run `EXPLAIN QUERY PLAN` on that specific query before
   changing anything. The fix is almost always "add/fix an index," and guessing which
   one without the query plan wastes time.

### 4. Measure memory over a 1-hour workout — `[HUMAN]`

Two independent methods. Do both; they catch different things.

**Method A — `dumpsys meminfo`, the whole process, on the release build:**
1. Release APK, phone unplugged on wireless adb, HyperOS controls set (Phase 07).
2. Start the workout, then sample memory every 10 minutes for the hour:
   ```powershell
   adb shell dumpsys meminfo com.myhi.companion | Select-String "TOTAL PSS"
   ```
   (Or ask the agent to run a loop that does this every 10 minutes and writes a
   table.)
3. **Record:** each reading, peak MB, end-of-hour MB, and a plain "flat" or
   "rising, ~X MB/hour" verdict. Sawtooth is fine (garbage collection); a staircase
   that only goes up is not.

**Method B — JavaScript heap snapshots, to find *what* leaks (development build):**
Only needed if Method A shows a rising slope.
1. Development build, React Native DevTools open (`j`), **Memory** tab.
2. Run a 10-minute fake-treadmill workout (`FakeTreadmillService` is fine here: you're
   looking for leaked objects, not BLE behaviour). Take a heap snapshot at minute 1 and
   another at minute 10.
3. Compare the two (the "Comparison" view): sort by the count delta. A type whose
   count grows roughly with elapsed time (closures from `onSample`, sample objects,
   capture entries) is the leak. Everything else is normal churn.
4. The fix is almost always a missing unsubscribe in some effect's cleanup or some
   class's `dispose()`. Phase 03's "no listener survives leaving the dashboard" test is
   the template for proving the fix.

### 5. Measure CPU during active recording

1. Quick number: during a release-build workout,
   ```powershell
   adb shell top -n 1 | Select-String "myhi"
   ```
   a few times over a minute. A steady low single-digit percentage is what a 1 Hz BLE
   app should look like.
2. If that number is surprising, find out where it goes. In the development build,
   React Native DevTools' **Performance** panel records a trace of JavaScript work:
   look for renders far more often than the 4 Hz throttle should allow, or a single
   long task. (Development-mode timings are inflated, so read it for *what* runs and
   *how often*, not for *how long*.) For native-side CPU, Android Studio's CPU
   profiler can attach to a debuggable build.
3. Record a typical percentage. This is a "note it" measurement, there's no numeric
   target, just "does anything unexpected show up."

### 6. Measure battery drain per hour of workout — `[HUMAN]`

1. Fully charge the phone, then note the starting battery percentage
   (**Settings → Battery**, or `adb shell dumpsys battery`).
2. Reset batterystats so this run isn't polluted by earlier testing:
   ```powershell
   adb shell dumpsys batterystats --reset
   ```
3. Run the same one-hour locked workout as step 4 (the same session is fine).
4. Pull the stats:
   ```powershell
   adb shell dumpsys batterystats > batterystats.txt
   adb shell dumpsys batterystats --charged com.myhi.companion
   ```
5. Optional but worth it if the number looks bad: feed `batterystats.txt` into
   [Battery Historian](https://developer.android.com/topic/performance/power/setup-battery-historian)
   for a visual timeline of what was drawing power when. The keep-alive's wake lock
   (Phase 07) will show up there, and should, for exactly the workout's duration and no
   longer.
6. Record the percentage drop over the hour as your "battery drain per hour" figure.

### 7. Measure BLE notification → screen update latency

**Concept.** A small logic addition, not a UI change. `TreadmillSample.receivedAtMs`
already records when the notification arrived. Record when the dashboard actually
committed the new value to the screen, and log the difference.

Concrete steps:
1. In `useDashboard`, add an effect that depends on the current sample:
   `useEffect(() => { if (sample) log(`ui latency ${Date.now() - sample.receivedAtMs} ms`); }, [sample]);`
   Effects run after React has committed the render, so this measures "arrived" to
   "on screen" closely enough.
2. Run a few minutes of a live workout on the release build, watch the logged deltas
   in `adb logcat -s ReactNativeJS` or the rolling log file from Phase 00.
3. Record a typical value. There's no target here, just "note it," but a number
   consistently over ~250 ms would be worth a closer look given the 4 Hz throttle Phase
   03 already applies (a throttled sample can legitimately wait up to 250 ms; more than
   that means something else is in the way).
4. Remove the log line afterwards, or put it behind a developer setting.

### 8. Roll it up

Fill in every row of the Measure table with what steps 3–7 produced, and write
down the pass/fail against each target explicitly before calling the phase done.

---

## Tests

- Seed a synthetic 5,000-workout database and measure. Do not assume.
- `[HUMAN]` One-hour workout with memory sampled every 10 minutes
- Every screen opens in under 500 ms on the seeded database (release build)

## Acceptance

- [ ] Flat memory over one hour
- [ ] Statistics queries under 200 ms at 5,000 workouts, on the phone
