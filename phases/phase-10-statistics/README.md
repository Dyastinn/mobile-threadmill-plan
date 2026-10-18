# Phase 10 — Statistics

**Hardware:** none · **Size:** M · **Blocked by:** Phase 09

> See `../README.md` for the collaboration model. This phase leans UI-heavy: three
> of its tasks end in a chart or a list. But the SQL underneath every chart is still
> yours to write. The agent writes the chart component and the screens; you write the
> aggregate queries, the PR queries, the downsampling, and the hooks.

---

## Goal

Aggregates, a workout history, and charts, drawn with a small chart component of
our own on top of `react-native-svg`.

### Understanding what you're building (read this before the tasks)

**Why aggregate in SQL, not a TypeScript loop over a loaded table.** `Workout` rows
live in a SQLite database that already has a purpose-built engine for "sum this
column, grouped by that column" (`GROUP BY`/`SUM`/`COUNT`): the query
`../phase-06-recording-schema/README.md`'s "Query patterns" section already gives for
daily totals, which `SqliteStatisticsProvider.getAggregates` extends to
weekly/monthly/yearly. The naive alternative pulls every row into an array
(`SELECT * FROM Workout`) and sums it with `.reduce()` and a `Map` keyed by day. That's
more code to do the same job.

For the `Workout`-level aggregates (10.1/10.2), be honest about the actual stakes.
This table, even after years of daily use, tops out around a few thousand rows, and
a naive `.reduce()` over that wouldn't be slow enough to notice today. So this isn't
"SQL because performance demands it," it's "SQL because it's already the simplest
correct tool for the job": a `GROUP BY date(...)` query is fewer lines and less code
to own than a hand-rolled accumulator loop. Not a more complex alternative earning
its keep.

Where the stakes get real and quantified is task 10.4's per-workout sample series. A
two-hour workout is 1,440 rows in `WorkoutSample` at the 5-second cadence, and a
phone-width chart needs on the order of 200 points to render meaningfully. Loading all
of them just to thin them in a loop means shipping 7× more data across the native
bridge than the chart will ever draw, every time a detail screen opens. The
`WHERE ElapsedSec % $n = 0` downsampling query in `getWorkoutSeries` does that thinning
inside the database instead, so roughly 200 rows cross into JavaScript, not 1,440.
That's the one place in this phase where "compute it in SQL" earns its complexity over
the loop alternative, with a real, quantified number behind it. Everywhere else in this
phase it's simply the shorter, more idiomatic way to ask the question.

**The pattern, named plainly.** `../phase-06-recording-schema/README.md`'s rule that a
cache table is allowed "if a query becomes slow" but "do not start there" is an
application of **YAGNI** (you aren't gonna need it yet): resist adding a structure to
solve a problem you haven't actually measured. The cost of *not* pre-building a cache
table is that every chart re-runs its aggregate query from scratch, with no
memoization. The payoff is zero synchronization bugs. A cache table that ever drifts
out of sync with `Workout`/`WorkoutSample` is worse than no cache at all, because it
can silently lie, and there's one less thing to keep correct across every future phase
that writes a workout. This tradeoff would flip, a rebuildable cache table becoming
genuinely worth it, the moment this phase's own performance test (5,000 workouts,
aggregate queries under 200 ms) actually fails, not before. That's the concrete,
stated condition, not "in case it's slow later."

## Features

- Daily / weekly / monthly / yearly aggregates
- Personal records: longest distance, longest duration, fastest average
- A workout history list, and a detail screen per workout
- Cross-workout charts: distance, calories, duration, average speed over time
- **Per-workout charts**: speed and heart rate curves from `WorkoutSample`. This is
  the payoff for storing telemetry and the main thing FitShow does badly.

If V3 said heart rate is marginal or unusable, HR curves are hidden here too. The
data keeps being recorded, so they can be switched on later without a gap in history.

## Implementation requirements

- **Statistics and PRs are always computed from `Workout` / `WorkoutSample`, never
  stored.** Not columns, not tables, not exported. If a query becomes slow, add a cache
  table that is *explicitly rebuildable* and rebuild it after any import, but do not
  start there.
- **Aggregate in SQL**, not in TypeScript over a full table read. Query patterns and
  the local-day-via-stored-offset expression (with `printf('%+d minutes', ...)`, not
  string concatenation; see why there) are in `../phase-06-recording-schema/README.md`.
- Index `Workout(StartedAtUtc)`. Every aggregate filters on it.
- **Downsample sample series for display.** A 2-hour workout is 1,440 points; a
  phone-width chart needs perhaps 200. Downsample in SQL (`WHERE ElapsedSec % $n = 0`),
  not by loading everything and thinning it in TypeScript.
- Gap-marked samples render as breaks in the line, never as interpolated segments.

---

## Learning goals

- SQL aggregation (`GROUP BY`, `SUM`, `COUNT`) against a real schema, instead of
  pulling rows into JavaScript and looping. This is the pattern
  `../phase-06-recording-schema/README.md` already documents for daily totals; you'll
  extend it to weekly/monthly/yearly and to personal records.
- Downsampling a large ordered series in SQL rather than in memory.
- How a chart turns `null` into a visible gap: the component lifts the pen at every
  `null` instead of drawing through it. This is the mechanism that renders the
  connection-gap markers from Phase 04/06 as actual breaks in the line.
- Reusing the seam/fake pattern one more time: `StatisticsProvider` gets a fake for
  chart-layout work before the real SQL is reviewed, same shape as
  `TreadmillService`/`FakeTreadmillService` (01b) and `WorkoutHistoryProvider` (03).
- **Dynamic routes** in Expo Router: `src/app/history/[workoutId].tsx` is one file
  that serves `/history/<any id>`, and `useLocalSearchParams()` reads the id.

## Technology decision: hand-drawn charts on `react-native-svg`

**What problem does it solve?** This phase needs two chart shapes: cross-workout trend
lines (distance/duration/speed over time) and a per-workout speed + heart-rate curve
over a single workout's samples, with two different y-axes (km/h and bpm) and visible
gaps where the connection dropped (a `null` sample, not an interpolated line).

**Why are we using it?** The requirement is genuinely small: a line or two, a few
dozen to a couple of hundred points, axis labels, gaps, no zoom, no pan, no tooltips.
A `LineChart` component that draws exactly that on `react-native-svg` (an
Expo-supported, widely used library for drawing vector shapes) is about 150 lines of
TypeScript the agent writes once, styled directly from the monochrome tokens, with gap
handling that's a three-line `if`. It re-renders automatically when the light/dark
theme changes, because it reads colors from `useTheme()` like every other component.
No charting library to keep compatible with future SDKs.

**Alternatives considered:**

1. **`react-native-gifted-charts`.** Pros: SVG-based, actively released, line/bar/area
   charts out of the box, a secondary y-axis, configurable handling of missing values.
   Cons: a large prop surface to learn and style to match the theme, and its own
   release cadence to track; its many features (animations, tooltips, pointers) are
   features this app doesn't use. Learning curve: low-medium. The better choice if the
   charts grow interactive (tap a point to see its value) or multiply in kind.
2. **`victory-native`** (Skia-based). Pros: high performance, flexible, multiple
   axes, a well-known name. Cons: pulls in `@shopify/react-native-skia`,
   `react-native-reanimated` and `react-native-gesture-handler`, three heavy native
   dependencies for two simple line charts. Learning curve: medium. The better choice
   for animated, gesture-driven charts with thousands of points.
3. **`react-native-chart-kit`.** Pros: very simple API, many tutorials. Cons: weak
   support for gaps and dual axes, and little recent maintenance. Learning curve: low.
   The better choice for a quick single-series prototype.

**Why not the alternatives?** Every library here is bigger than the problem. Gifted
Charts is the strongest alternative and the one to switch to if a real need for
interactivity shows up; the chart component's props (`series`, `xLabels`) are shaped
so that a swap would only touch `LineChart.tsx` and the two hooks that build its input.

**Long-term considerations.** `react-native-svg` is one of the most widely used React
Native libraries and is versioned in Expo's SDK compatibility list. The chart code is
ours to maintain, but it's small, has no hidden behaviour, and has no reason to change
unless the requirements do.

**Practical example:** `src/features/shared/charts/LineChart.tsx`, used by this phase's
statistics screen and workout detail screen.

---

## Reference docs

- `../phase-06-recording-schema/README.md`: schema, the local-day query pattern, the
  downsampling pattern, performance targets. Read this in full before task 10.1;
  almost everything below builds directly on it.
- **SQLite date and time functions**: https://www.sqlite.org/lang_datefunc.html. The
  modifiers `'weekday 1'`, `'-7 days'` and `strftime` formats are how weekly and monthly
  buckets get their keys.
- **`react-native-svg`**: https://github.com/software-mansion/react-native-svg. Only
  needed if you want to read how the chart component draws; `Svg`, `Path`, `Line`,
  `Text` are the four pieces it uses.
- **Expo Router dynamic routes**: https://docs.expo.dev/router/introduction/ (the
  "dynamic routes" section) for `[workoutId].tsx`.
- `docs/learning/04-Monochrome-Theme.md`: the gray ramp. The "Chart colors" section
  below maps chart roles onto theme tokens.

## Chart colors — the monochrome mapping

The chart reads the same tokens as every other component, so there's no second
palette and no hex value in chart code:

| Use | Token |
|---|---|
| Primary series (speed; the "main" line on any chart) | `colors.textPrimary`, solid |
| Secondary series (heart rate, a comparison line) | `colors.textSecondary`, **dashed** |
| Axis labels | `colors.textSecondary` |
| Grid lines | `colors.border` |

**Second series by stroke pattern, not by hue**: the heart-rate line is dashed rather
than a second color, following the same "state without hue" rule as the rest of the
app (`04-Monochrome-Theme.md`'s "Conveying state without color" table).

---

## Walkthrough

### Task 10.1 — `StatisticsProvider`: daily/weekly/monthly/yearly aggregates

**Logic: you write this.**

Creates: `src/core/statistics/statisticsProvider.ts`,
`src/core/statistics/sqliteStatisticsProvider.ts`.

`../phase-06-recording-schema/README.md`'s query pattern section already gives you the
*daily* version of this query, local-day-correct via the stored UTC offset:

```sql
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

Weekly/monthly/yearly are the same query with a different bucket expression. Week
bucketing is the one that's easy to get subtly wrong across a DST boundary and a year
boundary. One approach worth considering: key each week by the date of its Monday
(`date(<local day>, 'weekday 1', '-7 days')` gives the Monday on or before a date,
except when the date already *is* a Monday; work out on paper why, and fix it) rather
than a `'%W'` week number, which restarts at 00 every January. Work a real transition
date out on paper before trusting the SQL; that's why it's a named test below rather
than an afterthought.

Concrete steps:

1. Create the folder `src/core/statistics/`.
2. The shapes:
   ```ts
   export type AggregateGranularity = 'daily' | 'weekly' | 'monthly' | 'yearly';

   export interface AggregateBucket {
     /** e.g. "2026-07-28" (day), "2026-07-27" (week, keyed by its Monday), "2026-07", "2026" */
     readonly bucket: string;
     readonly workoutCount: number;
     readonly totalDistanceMeters: number;
     readonly totalCalories: number;
     readonly totalDurationSeconds: number;
   }

   export interface StatisticsProvider {
     getAggregates(granularity: AggregateGranularity, fromUtc: string, toUtc: string): Promise<AggregateBucket[]>;
     getPersonalRecords(): Promise<PersonalRecords>;
     listWorkouts(limit: number, offset: number): Promise<WorkoutListItem[]>;
     getWorkoutSeries(workoutId: string, maxPoints: number): Promise<WorkoutSamplePoint[]>;
   }
   ```
   (`PersonalRecords`, `WorkoutListItem` and `WorkoutSamplePoint` are defined in tasks
   10.2 and 10.4; the interface is written once, up front, so later tasks aren't
   inventing a second provider abstraction.) Decide for yourself whether a plain
   string `bucket` is the right shape for the chart's x-axis labels, or whether you'd
   rather return something richer. Think about what task 10.3's hook actually does
   with it before committing.
3. Implement `SqliteStatisticsProvider` over the `Db` interface (constructor takes the
   `Db`), same as Phase 06's repository. `getAggregates` picks the bucket expression
   for the granularity (a `switch` with the exhaustive `never` default), binds
   `$fromUtc`/`$toUtc`, runs `db.all`, and maps each row. SQLite `SUM` over zero rows
   returns `NULL`, not `0`; decide where you turn that into a number.
4. Add it to `services.tsx` as `statistics`.
5. Write the daily-aggregate test first (`sqliteStatisticsProvider.test.ts`, against
   `nodeSqliteDb` with the real migrations): seed a handful of known workouts,
   hand-calculate the expected totals, assert. Get that green before writing
   weekly/monthly/yearly. They're the same shape with a different bucket expression,
   and a working daily test gives you a template to copy for each timezone/DST case.

---

### Task 10.2 — Personal records

**Logic: you write this.**

Three independent "biggest single workout" queries: longest distance, longest
duration, fastest average speed. Each is `SELECT ... ORDER BY <column> DESC LIMIT 1`
against `Workout WHERE Status = 1`, no `GROUP BY`. These aren't aggregates over
multiple rows, they're a single row each.

Concrete steps:

1. Define the shape:
   ```ts
   export interface PersonalRecord {
     readonly value: number;
     /** The GUID, not the integer Id: see the dual-key section in Phase 06. */
     readonly workoutId: string;
     readonly startedAtUtc: string;
   }

   export interface PersonalRecords {
     readonly longestDistance: PersonalRecord | null;
     readonly longestDuration: PersonalRecord | null;
     readonly fastestAverage: PersonalRecord | null;
   }
   ```
2. `getPersonalRecords`: three queries (or one with subqueries; your choice). Handle
   the empty-database case explicitly: no rows means `null` records, not an exception.
   That's one of the phase's tests below.
3. Test against a seeded dataset where you know the answer by inspection, plus the
   empty-dataset case.

---

### Task 10.3 — Cross-workout trend chart

**UI: full code, written for you. The hook is yours.**

First, the chart component everything in this phase draws with,
`src/features/shared/charts/LineChart.tsx`:

```tsx
import { useState } from 'react';
import { View } from 'react-native';
import Svg, { Line, Path, Text as SvgText } from 'react-native-svg';

import { useTheme } from '@/features/shared/theme/useTheme';

export interface ChartSeries {
  readonly label: string;
  /** One value per x position. null = no data here: the line breaks. */
  readonly values: readonly (number | null)[];
  readonly axis: 'left' | 'right';
  /** Secondary series are dashed, never a second color. */
  readonly dashed?: boolean;
}

interface Props {
  readonly series: readonly ChartSeries[];
  readonly xLabels: readonly string[];
  readonly height?: number;
  readonly accessibilityLabel: string;
}

const TICKS = [0, 0.25, 0.5, 0.75, 1];

/** Round up to 1, 2 or 5 times a power of ten, so axis labels are readable. */
function niceCeil(v: number): number {
  if (v <= 0) return 1;
  const power = 10 ** Math.floor(Math.log10(v));
  const n = v / power;
  return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * power;
}

function formatTick(v: number): string {
  return Number.isInteger(v) ? String(v) : v.toFixed(1);
}

export function LineChart({ series, xLabels, height = 220, accessibilityLabel }: Props) {
  const { colors } = useTheme();
  const [width, setWidth] = useState(0);

  const hasRight = series.some((s) => s.axis === 'right');
  const pad = { left: 36, right: hasRight ? 36 : 12, top: 8, bottom: 22 };
  const plotW = Math.max(0, width - pad.left - pad.right);
  const plotH = height - pad.top - pad.bottom;
  const count = Math.max(xLabels.length, ...series.map((s) => s.values.length), 1);

  const maxFor = (axis: 'left' | 'right') =>
    niceCeil(
      Math.max(
        0,
        ...series
          .filter((s) => s.axis === axis)
          .flatMap((s) => s.values.filter((v): v is number => v !== null)),
      ),
    );
  const max = { left: maxFor('left'), right: maxFor('right') };

  const x = (i: number) => pad.left + (count === 1 ? plotW / 2 : (i / (count - 1)) * plotW);
  const y = (v: number, axisMax: number) => pad.top + plotH - (v / axisMax) * plotH;

  /** M starts a new segment, L continues it. A null lifts the pen: that's the gap. */
  function pathFor(s: ChartSeries): string {
    let d = '';
    let penDown = false;
    s.values.forEach((v, i) => {
      if (v === null) {
        penDown = false;
        return;
      }
      d += `${penDown ? 'L' : 'M'}${x(i).toFixed(1)},${y(v, max[s.axis]).toFixed(1)} `;
      penDown = true;
    });
    return d;
  }

  const labelStep = Math.max(1, Math.ceil(count / 5));

  return (
    <View
      style={{ height }}
      onLayout={(e) => setWidth(e.nativeEvent.layout.width)}
      accessible
      accessibilityLabel={accessibilityLabel}
    >
      {width > 0 && (
        <Svg width={width} height={height}>
          {TICKS.map((t) => {
            const gy = pad.top + plotH - t * plotH;
            return (
              <Line key={`grid-${t}`} x1={pad.left} x2={pad.left + plotW} y1={gy} y2={gy} stroke={colors.border} strokeWidth={1} />
            );
          })}
          {TICKS.map((t) => (
            <SvgText key={`l-${t}`} x={pad.left - 6} y={pad.top + plotH - t * plotH + 4} fontSize={11} fill={colors.textSecondary} textAnchor="end">
              {formatTick(max.left * t)}
            </SvgText>
          ))}
          {hasRight &&
            TICKS.map((t) => (
              <SvgText key={`r-${t}`} x={pad.left + plotW + 6} y={pad.top + plotH - t * plotH + 4} fontSize={11} fill={colors.textSecondary} textAnchor="start">
                {formatTick(max.right * t)}
              </SvgText>
            ))}
          {xLabels.map((label, i) =>
            i % labelStep === 0 ? (
              <SvgText key={`x-${i}`} x={x(i)} y={height - 6} fontSize={11} fill={colors.textSecondary} textAnchor="middle">
                {label}
              </SvgText>
            ) : null,
          )}
          {series.map((s) => (
            <Path
              key={s.label}
              d={pathFor(s)}
              fill="none"
              stroke={s.dashed ? colors.textSecondary : colors.textPrimary}
              strokeWidth={2}
              strokeDasharray={s.dashed ? '6 4' : undefined}
              strokeLinejoin="round"
              strokeLinecap="round"
            />
          ))}
        </Svg>
      )}
    </View>
  );
}
```

Things worth understanding, not just pasting:
- **`pathFor` is the whole gap mechanism.** An SVG path is a list of pen commands: `M`
  moves the pen without drawing, `L` draws a line to a point. Every `null` sets
  `penDown = false`, so the next real value starts with `M`: a jump, not a line. If a
  gap row ever reached this component as `0` instead of `null`, the line would dive to
  the bottom and back, which is exactly the "fabricated segment" the requirement
  forbids. Making sure gaps arrive as `null` is task 10.4's job.
- **`onLayout`** measures the width the chart was actually given, so the same
  component fills a full-width screen and a 33% split screen (Phase 11).
- A point with `null` on both sides draws nothing (a lone `M`). At a 5-second cadence
  that's a single isolated sample between two gaps, which is noise worth not drawing.
- `npx expo install react-native-svg` adds the native part; it needs a dev build
  rebuild once.

Then the hook (yours), `src/features/statistics/useStatistics.ts`:

```ts
export interface StatisticsState {
  readonly loading: boolean;
  readonly weekly: AggregateBucket[];
  readonly records: PersonalRecords | null;
}

export function useStatistics(): StatisticsState {
  // TODO: read `statistics` from useServices(); in one effect (with the cancelled-flag
  // pattern from Phase 03), load getAggregates('weekly', <12 weeks ago, UTC ISO>,
  // <now, UTC ISO>) and getPersonalRecords() together (Promise.all), then set state.
  // Re-load when the screen comes back into focus is a nice touch
  // (useFocusEffect from expo-router), not a requirement.
}
```

And the screen, `src/features/statistics/StatisticsScreen.tsx` (UI, in full):

```tsx
import { ScrollView } from 'react-native';

import { useStatistics } from './useStatistics';
import { LineChart } from '@/features/shared/charts/LineChart';
import { formatClock, formatKm, formatOneDecimal } from '@/features/shared/format';
import { MetricTile } from '@/features/shared/theme/MetricTile';
import { Screen } from '@/features/shared/theme/Screen';
import { Surface } from '@/features/shared/theme/Surface';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';

export function StatisticsScreen() {
  const s = useStatistics();
  const labels = s.weekly.map((b) => b.bucket.slice(5)); // "07-27"

  return (
    <Screen>
      <ScrollView contentContainerStyle={{ padding: spacing.lg, gap: spacing.lg }}>
        <Text variant="subheadline">Weekly distance</Text>
        <Surface style={{ padding: spacing.sm }}>
          <LineChart
            series={[{ label: 'Distance (km)', axis: 'left', values: s.weekly.map((b) => b.totalDistanceMeters / 1000) }]}
            xLabels={labels}
            accessibilityLabel={`Weekly distance, last ${s.weekly.length} weeks`}
          />
        </Surface>

        {/* Repeat the same Surface/LineChart block for calories, duration and average
            speed once distance is working end to end: same shape, a different
            AggregateBucket field. Not duplicated here since it's identical code four
            times; get the first one reviewed, then copy the pattern. */}

        <Text variant="subheadline">Personal records</Text>
        {s.records?.longestDistance && (
          <MetricTile value={formatKm(s.records.longestDistance.value)} unit="km, longest distance" />
        )}
        {s.records?.longestDuration && (
          <MetricTile value={formatClock(s.records.longestDuration.value)} unit="longest workout" />
        )}
        {s.records?.fastestAverage && (
          <MetricTile value={formatOneDecimal(s.records.fastestAverage.value)} unit="km/h, fastest average" />
        )}
      </ScrollView>
    </Screen>
  );
}
```

`src/app/statistics.tsx` is the usual one-line route. Add a "Statistics" link on the
dashboard next to "Settings".

Run against seeded fake data (task 10.5) and confirm the chart renders before wiring
the real `SqliteStatisticsProvider`.

---

### Task 10.4 — History list, and per-workout speed/heart-rate curves

**`listWorkouts`, downsampling and gap handling are logic: you write those. The two
screens are UI, written for you below.**

**The list query.** `listWorkouts(limit, offset)`: completed and abandoned workouts,
newest first (`ORDER BY StartedAtUtc DESC LIMIT $limit OFFSET $offset`), selecting
just what a row shows (`WorkoutId`, `StartedAtUtc`, `StartOffsetMinutes`,
`DurationSeconds`, `DistanceMeters`). Phase 12 times this at 5,000 workouts.

```ts
export interface WorkoutListItem {
  readonly workoutId: string;
  readonly startedAtUtc: string;
  readonly startOffsetMinutes: number;
  readonly durationSeconds: number;
  readonly distanceMeters: number;
}
```

**Downsampling.** `../phase-06-recording-schema/README.md` gives the pattern
`WHERE ElapsedSec % $n = 0`. `$n` has to be derived from the workout's actual length
and the target point count, not hardcoded. A 10-minute workout and a 2-hour workout
need very different `$n` to both land near `maxPoints`. Careful with the 5-second
cadence: samples sit at `ElapsedSec` 0, 5, 10, ..., so `$n` must be a multiple of 5 or
the modulo matches almost nothing.

**Gaps.** `WorkoutSample.Flags` bit 0 marks a connection-gap sample. The chart lifts
the pen at every `null` (task 10.3), so the conversion from "a row with
`Flags & 1 === 1`" to "a `null` in the values array" is where this phase's gap-marking
requirement actually gets enforced. Get it wrong (e.g. passing the stored value through,
or defaulting to `0`) and the chart silently draws a fabricated segment through the
dropout, exactly what the requirement says not to do. One more subtlety: a gap row
might not land on a multiple of `$n` and get filtered out by the downsample. Include
gap rows regardless: `WHERE (ElapsedSec % $n = 0 OR Flags & 1 = 1)`.

Concrete steps (logic):

1. The shape, nullable on the value fields specifically so a gap row carries `null`
   all the way through to the chart without a separate "is this a gap" flag the hook
   has to remember to check:
   ```ts
   export interface WorkoutSamplePoint {
     readonly elapsedSec: number;
     readonly speedKph: number | null;
     readonly heartRate: number | null;
   }
   ```
2. Implement `getWorkoutSeries(workoutId, maxPoints)`:
   ```ts
   async getWorkoutSeries(workoutId: string, maxPoints: number): Promise<WorkoutSamplePoint[]> {
     // TODO:
     // 1. MAX(ElapsedSec) for this workout (JOIN Workout ON WorkoutId = $workoutId):
     //    the series length, so you can compute $n (a multiple of 5, at least 5).
     // 2. SELECT ElapsedSec, SpeedKph, HeartRate, Flags FROM WorkoutSample
     //    WHERE WorkoutRowId = (...) AND (ElapsedSec % $n = 0 OR Flags & 1 = 1)
     //    ORDER BY ElapsedSec
     // 3. Map each row: where (Flags & 1) === 1, speedKph and heartRate become null
     //    regardless of what's stored in those columns.
   }
   ```
3. Test: a synthetic sample set with a deliberate gap in the middle (a few
   consecutive `Flags = 1` rows, some not on a multiple of `$n`). Expect `null` at
   exactly those positions, not the raw stored values, and every gap row present.
4. The two hooks: `useWorkoutList()` (first 50 from `listWorkouts`) and
   `useWorkoutDetail(workoutId)` (`getWorkoutSeries(workoutId, 200)`), same
   cancelled-flag effect shape as `useStatistics`.

UI (written for you). The list, `src/features/history/HistoryScreen.tsx`:

```tsx
import { Link } from 'expo-router';
import { FlatList, Pressable, View } from 'react-native';

import { useWorkoutList } from './useWorkoutList';
import { formatClock, formatKm } from '@/features/shared/format';
import { Screen } from '@/features/shared/theme/Screen';
import { Surface } from '@/features/shared/theme/Surface';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';

/** The workout's local date, from UTC + its own stored offset (not the phone's current one). */
function localDate(startedAtUtc: string, offsetMinutes: number): string {
  const local = new Date(Date.parse(startedAtUtc) + offsetMinutes * 60_000);
  return local.toISOString().slice(0, 16).replace('T', ' ');
}

export function HistoryScreen() {
  const { workouts } = useWorkoutList();
  return (
    <Screen>
      <FlatList
        data={workouts}
        keyExtractor={(w) => w.workoutId}
        contentContainerStyle={{ padding: spacing.lg, gap: spacing.sm }}
        renderItem={({ item }) => (
          <Link href={`/history/${item.workoutId}`} asChild>
            <Pressable accessibilityRole="button">
              <Surface style={{ padding: 14, flexDirection: 'row', justifyContent: 'space-between' }}>
                <Text>{localDate(item.startedAtUtc, item.startOffsetMinutes)}</Text>
                <View style={{ flexDirection: 'row', gap: spacing.md }}>
                  <Text variant="caption">{formatKm(item.distanceMeters)} km</Text>
                  <Text variant="caption">{formatClock(item.durationSeconds)}</Text>
                </View>
              </Surface>
            </Pressable>
          </Link>
        )}
      />
    </Screen>
  );
}
```

The detail screen, `src/features/history/WorkoutDetailScreen.tsx`:

```tsx
import { ScrollView } from 'react-native';

import { useWorkoutDetail } from './useWorkoutDetail';
import { HEART_RATE_VERDICT } from '@/features/dashboard/useDashboard';
import { LineChart, type ChartSeries } from '@/features/shared/charts/LineChart';
import { formatClock } from '@/features/shared/format';
import { Screen } from '@/features/shared/theme/Screen';
import { Surface } from '@/features/shared/theme/Surface';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';

export function WorkoutDetailScreen({ workoutId }: { workoutId: string }) {
  const { points } = useWorkoutDetail(workoutId);

  const series: ChartSeries[] = [
    { label: 'Speed (km/h)', axis: 'left', values: points.map((p) => p.speedKph) },
  ];
  // Hidden, not shown empty, if V3 said heart rate isn't usable.
  if (HEART_RATE_VERDICT === 'usable') {
    series.push({ label: 'Heart rate (bpm)', axis: 'right', dashed: true, values: points.map((p) => p.heartRate) });
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={{ padding: spacing.lg, gap: spacing.md }}>
        <Text variant="subheadline">Speed{series.length > 1 ? ' and heart rate' : ''}</Text>
        <Surface style={{ padding: spacing.sm }}>
          <LineChart
            series={series}
            xLabels={points.map((p) => formatClock(p.elapsedSec))}
            height={240}
            accessibilityLabel="Speed over the workout"
          />
        </Surface>
        <Text variant="caption">Breaks in the line are moments the treadmill connection dropped.</Text>
      </ScrollView>
    </Screen>
  );
}
```

And the routes: `src/app/history/index.tsx` (the list, one line) and
`src/app/history/[workoutId].tsx`:

```tsx
import { useLocalSearchParams } from 'expo-router';

import { WorkoutDetailScreen } from '@/features/history/WorkoutDetailScreen';

export default function WorkoutDetailRoute() {
  const { workoutId } = useLocalSearchParams<{ workoutId: string }>();
  return <WorkoutDetailScreen workoutId={workoutId} />;
}
```

`FlatList` (not `.map()`) for the history, because unlike Phase 05's six preset buttons
this list grows by one every day: it only renders the rows on screen.

Concrete steps:

1. Write and test `listWorkouts` and `getWorkoutSeries` first, against fixtures with a
   known gap.
2. Paste the screens and routes, add a "History" link on the dashboard.
3. Not hardware-gated, but worth doing before moving on: seed a workout with a
   deliberate gap via the fake provider (task 10.5) and confirm by eye that the line
   actually breaks rather than sagging through the gap.

---

### Task 10.5 — `FakeStatisticsProvider`

**Logic: you write this, short.**

Creates: `src/core/statistics/fakeStatisticsProvider.ts`.

Same seam pattern as `FakeTreadmillService` (01b) and `FakeWorkoutHistoryProvider`
(03): implement `StatisticsProvider` by generating plausible synthetic buckets,
records, list items and sample points (including one workout with a gap) instead of
querying SQLite. Put it in `services.tsx` first, build the screens in tasks 10.3/10.4
against it, then swap to `SqliteStatisticsProvider` once the queries are written and
reviewed. One line changes in `services.tsx`; nothing in the hooks or screens does.

---

## Tests

- Hand-calculate weekly totals for a seeded dataset and compare
- Timezone: a workout at 23:30 local buckets into the correct **local** day, and a
  workout with a negative offset doesn't vanish from its bucket
- DST boundary week aggregates correctly; a week spanning New Year is one bucket
- Empty dataset: aggregates return `[]`, records are all `null`, and both screens
  render without crashing (an empty `values` array draws an empty chart)
- 5,000-workout dataset: aggregate queries under 200 ms (measured on the phone in
  Phase 12)
- `getWorkoutSeries` on a fixture with a deliberate gap returns `null` at exactly the
  flagged positions, and keeps gap rows that aren't on a multiple of `$n`

## Acceptance

- [ ] Manual calculations match for daily, weekly, monthly
- [ ] No stored derived values anywhere in the schema
- [ ] Cross-workout trend chart renders against `FakeStatisticsProvider`, then against
      real seeded data, matching the hand-calculated totals
- [ ] A workout with a mid-session connection gap renders as a visible break in the
      per-workout chart, never a sagging or flat interpolated line
- [ ] Heart rate curve is absent entirely (not shown empty) if Phase 00's V3 finding
      marked heart rate unusable
