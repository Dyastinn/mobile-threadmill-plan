# Phase 12 — Performance

> Measure first, then fix what's actually slow. Not the other way round.

**Treadmill needed:** yes · **Size:** S · **Waits for:** Phase 11

---

## Goal

Real numbers instead of "it feels fine". By the end you'll have: a fake database with
5,000 workouts for testing speed, a timing screen on the phone for the database's main
queries, an hour of memory, CPU and battery readings from a real workout, and an honest
answer to "is anything slow, and where?"

This phase is mostly **measuring**, not coding. The two small bits of code (the fake
data maker and the timer) are yours. The rest is running builds and reading results.
With the phone on wireless `adb`, the agent can run the commands below and put the
results in a table.

### The idea

Like a doctor running tests before prescribing: "the list feels slow" doesn't say
whether it's a missing database index, the dashboard redrawing too often, or a slow
memory leak. Phases 06–11 built the system, but never tried it with 5,000 workouts or a
real hour. This phase does.

**Flat matters more than low.** 160 MB that stays at 160 MB all hour is healthy. 90 MB
that creeps up to 150 MB is a leak: a listener that never stops, a list that never
empties. A longer walk or an older phone turns that slope into a crash. So record
the *slope* ("flat" or "rising ~X MB an hour"), not just one number.

**Why not just add the usual speed-ups now?** Caching, memoising every component,
and so on are all extra code with their own bugs, aimed at a guess. If a query is slow
because of a missing index, that's a one-line fix, and `EXPLAIN QUERY PLAN` (step 3)
tells you in one command. Measuring first is the *cheaper* option: it turns five
possible fixes into the one you need. (If you spot an obviously slow loop by reading the
code, just fix it; you don't need to measure that.)

## What you'll learn

- **Why you always measure the release build.** The development build adds extra checks
  and warnings, runs effects twice (Strict Mode) and loads code from the PC. It can be
  several times slower than the real app.
- **Two kinds of memory.** The JavaScript heap (your objects and listeners) is part of
  it; the native side (React Native, Bluetooth, SQLite) is the rest. `dumpsys meminfo`
  sees the whole app; a heap snapshot sees only your part, but shows *which objects*.
- **Comparing two snapshots** taken minutes apart to find a leak: an object type whose
  count keeps growing is the leak.
- **`EXPLAIN QUERY PLAN`**: whether SQLite is using the index you think it is.

## Read first

| Topic | Link |
|---|---|
| React Native performance (dev vs. release) | https://reactnative.dev/docs/performance |
| React Native DevTools (memory snapshots, performance) | https://reactnative.dev/docs/react-native-devtools |
| Expo debugging tools | https://docs.expo.dev/debugging/tools/ |
| Android Studio Profiler (optional) | https://developer.android.com/studio/profile |
| `dumpsys` | https://developer.android.com/tools/dumpsys |
| Battery Historian | https://developer.android.com/topic/performance/power/setup-battery-historian |
| SQLite `EXPLAIN QUERY PLAN` | https://www.sqlite.org/eqp.html |
| `adb` | https://developer.android.com/tools/adb |

## What to measure

| What | Target |
|--------|--------|
| Memory over a 1-hour workout (`TOTAL PSS`) | under 150 MB, and **flat** |
| CPU while recording | write it down |
| Battery used per hour of workout | write it down |
| Time from Bluetooth packet to screen | write it down |
| Save 60 samples | < 50 ms |
| Save 720 samples | < 1 s |
| Workout list (50 rows), 5,000 workouts | < 100 ms |
| Weekly totals, 5,000 workouts | < 200 ms |
| One workout's chart (2-hour workout) | < 200 ms |

**The slope matters more than the number.** 150 MB is a starting guess for a React
Native app on this phone, not a measurement. If the first flat reading is 170 MB, write
it down, and judge by the slope.

---

## Steps

### 0. Tools

1. `adb` on your `PATH` and the phone paired over wireless debugging (see the setup
   doc). Unplugged: charging changes how the phone saves power, which spoils battery
   numbers.
2. React Native DevTools: with the development build and `npx expo start` running,
   press `j`. Only for step 4B's memory snapshots, never for timing.
3. Optional: Android Studio, only if step 5 leaves a question.

### 1. Always measure the release build

Every number here comes from `npx expo run:android --variant release`. If a number looks
bad, first check it really was the release build. The timing screen (step 3) shows the
build type, so a screenshot can't be ambiguous.

### 2. Make 5,000 fake workouts (yours)

**Why.** With 12 workouts, every query looks instant whether or not the indexes work.
You need a big, repeatable dataset.

- Create `src/core/data/seed/seedDatabase.ts`:
  `seedDatabase(db, { workouts: number; seed: number })`. It adds workouts with samples:
  a mix of short ones and ~2-hour ones (for the "2-hour chart" target). It's in `core/`,
  so it runs on the PC (fast check) and on the phone (real measurement).
- Spread the dates over at least two years and across daylight-saving changes, and
  include some negative time-zone offsets. Workouts only in July would miss the tricky
  cases.
- **Same seed, same data**, every run. `Math.random` can't be seeded, so write a small
  seeded random number generator (search "mulberry32"; about ten lines).
- Save samples through Phase 06's `WorkoutSampleBuffer`. Don't write a second way to
  insert just for this.

1. Write it. Realistic speed curves aren't needed; flat or slightly random values are
   fine. This is about the number of rows.
2. Run it on the PC once (a test with a smaller number is enough) and check the counts.
3. On the phone, the timing screen fills a **separate** database file (`myhi-bench.db`),
   never `myhi.db`. Your real workouts must never share a file with 5,000 fake ones.

### 3. Time the database (timer yours, screen given)

**Why.** Five of the targets need the same thing: run something against the big
database on the phone, time it, compare. You'll want to re-run this after any database
change, so make it reusable.

- `src/core/perf/timeAsync.ts`:
  `timeAsync(label, operation, runs = 5)` returns `{ label, medianMs, maxMs }`. Run it
  several times and report the middle value: the first run is usually slower than
  normal. `performance.now()` works on the phone and in Node.
- One call per row of the last five targets.
- If a number is over target, run `EXPLAIN QUERY PLAN` on that query:
  `db.all('EXPLAIN QUERY PLAN SELECT ...')`. A `detail` of
  `SEARCH Workout USING INDEX IX_Workout_StartedAtUtc ...` is good; `SCAN Workout`
  means it's reading the whole table.

The timing screen (given), `src/features/diagnostics/BenchmarkScreen.tsx`, linked from
the Diagnostics list:

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
        <Button label={running ? 'Running...' : 'Fill (if needed) and run'} onPress={() => void run()} disabled={running} />
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

`runBenchmarks.ts` is yours: open (or create and fill) `myhi-bench.db` the same way as
the real database, time the five operations with their targets, and return the list.

1. Write `timeAsync` and a test for it (the middle of `[10, 12, 50, 11, 13]` is `12`).
2. Write `runBenchmarks.ts`. Run it on the release build; take a screenshot.
3. Write the five numbers into the table above.
4. For anything over target, run `EXPLAIN QUERY PLAN` before changing anything. The fix
   is almost always an index, and the plan tells you which.

### 4. Memory over a 1-hour workout (treadmill)

Two methods. Do both; they find different things.

**A — the whole app, release build:**
1. Release build, unplugged, on wireless `adb`, HyperOS settings done (Phase 07).
2. Start the workout and read memory every 10 minutes for the hour:
   ```powershell
   adb shell dumpsys meminfo com.myhi.companion | Select-String "TOTAL PSS"
   ```
   (Or ask the agent to run it every 10 minutes and make a table.)
3. **Write down:** each reading, the highest, the last, and "flat" or "rising ~X MB an
   hour". Up-and-down is fine (that's memory being cleaned up). Only-going-up isn't.

**B — what's leaking, development build** (only if A shows a rising slope):
1. Development build, React Native DevTools open (`j`), **Memory** tab.
2. A 10-minute workout with the fake treadmill is fine here (you're looking for leftover
   objects, not Bluetooth behaviour). Take a snapshot at minute 1 and at minute 10.
3. Compare them ("Comparison" view), sorted by how much each count grew. A type that
   grows with time (listeners from `onSample`, sample objects, capture entries) is the
   leak.
4. The fix is almost always a missing unsubscribe in a cleanup or a `dispose()`. Phase
   03's "no listener left behind" test is the model for proving it's fixed.

### 5. CPU while recording

1. Quick number, during a release-build workout, a few times over a minute:
   ```powershell
   adb shell top -n 1 | Select-String "myhi"
   ```
   A low single-digit percentage is normal for an app getting Bluetooth data once a
   second.
2. If it's higher than that, find out why. In the development build, React Native
   DevTools' **Performance** tab records what your code does: look for redraws much more
   often than the 4-per-second limit, or one long task. (Development timings are
   inflated, so look at *what* runs and *how often*, not how long.) For the native side,
   Android Studio's CPU profiler can attach to the development build.
3. Write down a typical percentage. No target; just "anything surprising?"

### 6. Battery per hour (treadmill)

1. Charge fully, then note the battery level (`adb shell dumpsys battery`).
2. Reset the battery stats:
   ```powershell
   adb shell dumpsys batterystats --reset
   ```
3. Do the same 1-hour locked workout as step 4 (same session is fine).
4. Save the stats:
   ```powershell
   adb shell dumpsys batterystats > batterystats.txt
   adb shell dumpsys batterystats --charged com.myhi.companion
   ```
5. Optional, if it looks bad: open `batterystats.txt` in Battery Historian for a
   timeline. The keep-alive's wake lock (Phase 07) shows up there, and should, for the
   length of the workout only.
6. Write down the percentage used in the hour.

### 7. Time from Bluetooth packet to screen

**Why.** `TreadmillSample.receivedAtMs` already records when a packet arrived. Record
when the dashboard showed it, and log the difference.

1. In `useDashboard`, add:
   `useEffect(() => { if (sample) log(`ui latency ${Date.now() - sample.receivedAtMs} ms`); }, [sample]);`
   Effects run after the screen has updated, so this is close enough.
2. Run a few minutes of a real workout on the release build, and watch
   `adb logcat -s ReactNativeJS`.
3. Write down a typical value. No target, but up to 250 ms is expected (that's the
   update limit from Phase 03). Consistently more means something's in the way.
4. Remove the log line afterwards, or put it behind a developer setting.

### 8. Sum up

Fill every row of the table above, and write pass or fail next to each target before
calling the phase done.

---

## Tests

- 5,000 fake workouts, measured. Don't assume.
- Treadmill: a 1-hour workout with memory read every 10 minutes
- Every screen opens in under 500 ms with the big database (release build)

## Done when

- [ ] Memory stays flat over an hour
- [ ] Statistics queries take under 200 ms with 5,000 workouts, on the phone
