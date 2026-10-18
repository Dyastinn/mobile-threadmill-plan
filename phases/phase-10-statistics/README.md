# Phase 10 — Statistics and History

**Treadmill needed:** no · **Size:** M · **Waits for:** Phase 09

> Mostly screens and charts, which the agent writes. The SQL underneath every chart is
> yours: the totals, the records, the history list, and picking fewer points for charts.

---

## Goal

Totals, personal records, a list of past workouts, and charts, drawn with a small chart
component of our own.

### The idea

**Add things up in SQL, not in TypeScript.** SQLite is built for "sum this, grouped by
that" (`GROUP BY`, `SUM`, `COUNT`). The alternative, loading every workout and adding
them up in a loop, is more code for the same answer. With a few thousand workouts it
wouldn't even be slow; SQL is just the simpler tool.

**Where it really matters: a workout's chart.** A 2-hour workout has 1,440 samples. A
phone-sized chart needs about 200 points. So pick every n-th sample *in SQL*
(`WHERE ElapsedSec % $n = 0`): about 200 rows come back, not 1,440.

**No saved totals.** Every chart runs its query fresh. A saved copy of the totals could
drift out of sync and quietly show wrong numbers. Only add one if Phase 12 measures a
query as too slow (the target is under 200 ms with 5,000 workouts).

## Features

- Daily / weekly / monthly / yearly totals
- Personal records: longest distance, longest time, fastest average speed
- A history list, and a page for each workout
- Charts over time: distance, calories, time, average speed
- **A chart per workout** of speed and heart rate. This is why samples are saved, and
  the main thing FitShow does badly.

If V3 said heart rate is marginal or unusable, the heart rate line is left out here too.
It's still saved, so it can be turned on later.

## Rules

- **Totals and records are always calculated, never saved.**
- **Add up in SQL.** Use the local-day query from Phase 06, with
  `printf('%+d minutes', ...)` (Phase 06 explains why).
- Every total filters on `StartedAtUtc`, which has an index.
- **Fewer points for charts, picked in SQL.**
- Gaps (lost connection) show as breaks in the line, never as a joined-up line.

---

## What you'll learn

- SQL totals (`GROUP BY`, `SUM`, `COUNT`) on real tables.
- Picking fewer points in SQL instead of in memory.
- How a chart shows a gap: the line "lifts the pen" at every `null`.
- The fake-data trick once more: `StatisticsProvider` gets a fake so the charts can be
  built before the SQL is reviewed.
- **Routes with a parameter**: `src/app/history/[workoutId].tsx` is one file for
  `/history/<any id>`, and `useLocalSearchParams()` reads the id.

## Technology decision: our own charts, drawn with `react-native-svg`

**Problem.** Two kinds of chart: trends over time, and one workout's speed and heart
rate, with two different scales (km/h and bpm) and visible gaps where the connection
dropped.

**Why this.** What we need is small: a line or two, up to a couple of hundred points,
labels, and gaps. No zooming, no tapping. A `LineChart` component on `react-native-svg`
(a widely used library for drawing shapes) is about 150 lines, written once by the
agent, using the theme's colors directly. Gaps are a three-line `if`. It updates when
light/dark mode changes, like every other component. No chart library to keep working
across upgrades.

**Alternatives.**

1. **`react-native-gifted-charts`.** Good: SVG-based, maintained, two scales, handles
   missing values. Bad: lots of options to learn and restyle, for features (animations,
   tooltips) we don't use. Better if charts ever need tapping or more types.
2. **`victory-native`.** Good: fast, flexible. Bad: needs three heavy native libraries
   (Skia, Reanimated, Gesture Handler) for two line charts. Better for animated charts
   with thousands of points.
3. **`react-native-chart-kit`.** Good: very simple. Bad: weak on gaps and two scales,
   and little recent maintenance. Better for a quick one-line prototype.

**Why not those.** They're all bigger than the problem. Gifted Charts is the one to
switch to if the charts ever need tapping. The chart's inputs (`series`, `xLabels`) are
simple, so a swap would only touch the chart file and two hooks.

**Long term.** `react-native-svg` is one of the most used React Native libraries, and
Expo keeps it compatible. The chart code is ours, but it's small and won't need changes
unless the requirements do.

**In the code:** `src/features/shared/charts/LineChart.tsx`.

---

## Read first

- Phase 06: the tables, the local-day query, picking points, the speed targets. Read it
  fully before 10.1.
- **SQLite date functions**: https://www.sqlite.org/lang_datefunc.html (`'weekday 1'`,
  `'-7 days'`, `strftime`).
- **`react-native-svg`**: https://github.com/software-mansion/react-native-svg (only if
  you want to see how the chart draws).
- **Routes with parameters**: https://docs.expo.dev/router/introduction/
- [`04-Monochrome-Theme.md`](../../docs/learning/04-Monochrome-Theme.md)

## Chart colors

The chart uses the same theme colors as everything else:

| Use | Color |
|---|---|
| The main line (speed) | `textPrimary`, solid |
| The second line (heart rate) | `textSecondary`, **dashed** |
| Labels | `textSecondary` |
| Grid lines | `border` |

The second line is dashed, not a second color, following the theme's "no colors for
meaning" rule.

---

## Tasks

### 10.1 — Totals: daily, weekly, monthly, yearly (yours)

Create `src/core/statistics/statisticsProvider.ts` and
`src/core/statistics/sqliteStatisticsProvider.ts`.

Phase 06 already gives the *daily* query:

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

Weekly, monthly and yearly are the same query with a different grouping. Weeks are the
easy one to get wrong. One idea: name each week by its Monday's date, rather than a
week number (which restarts at 0 every January).
`date(<local day>, 'weekday 1', '-7 days')` gives the Monday before a date, *except*
when the date is already a Monday. Work out on paper why, and fix it. Try a real
daylight-saving date and a New Year's week too; that's why they're tests below.

1. Create `src/core/statistics/`.
2. The shapes:
   ```ts
   export type AggregateGranularity = 'daily' | 'weekly' | 'monthly' | 'yearly';

   export interface AggregateBucket {
     /** "2026-07-28" (day), "2026-07-27" (week, by its Monday), "2026-07", "2026" */
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
   (`PersonalRecords`, `WorkoutListItem` and `WorkoutSamplePoint` come in 10.2 and 10.4.
   The interface is written once, up front.) Decide for yourself whether a plain string
   `bucket` is right for the chart labels.
3. Write `SqliteStatisticsProvider` using the `Db` interface, like Phase 06's
   repository. `getAggregates` picks the grouping for the granularity (a `switch` with
   the `never` check), runs the query, and maps each row. Note: `SUM` over zero rows
   gives `NULL`, not `0`; decide where to turn that into a number.
4. Add it to the services file as `statistics`.
5. Write the daily test first (`sqliteStatisticsProvider.test.ts`, test database, real
   migrations): add a few known workouts, work out the totals by hand, compare. Get it
   passing before weekly/monthly/yearly; they follow the same pattern.

### 10.2 — Personal records (yours)

Three "best single workout" queries: longest distance, longest time, fastest average.
Each is `SELECT ... ORDER BY <column> DESC LIMIT 1` on `Workout WHERE Status = 1`. No
grouping.

1. The shape:
   ```ts
   export interface PersonalRecord {
     readonly value: number;
     readonly workoutId: string; // the GUID, not the number Id
     readonly startedAtUtc: string;
   }

   export interface PersonalRecords {
     readonly longestDistance: PersonalRecord | null;
     readonly longestDuration: PersonalRecord | null;
     readonly fastestAverage: PersonalRecord | null;
   }
   ```
2. `getPersonalRecords`: three queries, or one, your choice. No workouts → all `null`,
   not an error.
3. Test with data where you know the answer, and with no data.

### 10.3 — The trend chart (chart and screen given, hook yours)

The chart component, `src/features/shared/charts/LineChart.tsx`:

```tsx
import { useState } from 'react';
import { View } from 'react-native';
import Svg, { Line, Path, Text as SvgText } from 'react-native-svg';

import { useTheme } from '@/features/shared/theme/useTheme';

export interface ChartSeries {
  readonly label: string;
  /** One value per x position. null = no data: the line breaks there. */
  readonly values: readonly (number | null)[];
  readonly axis: 'left' | 'right';
  /** The second line is dashed, never a second color. */
  readonly dashed?: boolean;
}

interface Props {
  readonly series: readonly ChartSeries[];
  readonly xLabels: readonly string[];
  readonly height?: number;
  readonly accessibilityLabel: string;
}

const TICKS = [0, 0.25, 0.5, 0.75, 1];

/** Rounds up to 1, 2 or 5 × a power of ten, so labels are tidy. */
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

  // "M" moves the pen without drawing, "L" draws. A null lifts the pen: that's the gap.
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
            return <Line key={`grid-${t}`} x1={pad.left} x2={pad.left + plotW} y1={gy} y2={gy} stroke={colors.border} strokeWidth={1} />;
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

Worth knowing:
- **`pathFor` is the whole gap feature.** A `null` lifts the pen, so the next point
  starts a new line instead of joining. If a gap arrived as `0` instead of `null`, the
  line would dive to the bottom: a made-up line. Making gaps arrive as `null` is task
  10.4's job.
- **`onLayout`** measures the width the chart actually got, so it fits both full screen
  and split screen.
- A single point between two gaps draws nothing. At one sample per 5 seconds, that's
  noise anyway.
- Install with `npx expo install react-native-svg`, and rebuild the app once.

The hook (yours), `src/features/statistics/useStatistics.ts`:

```ts
export interface StatisticsState {
  readonly loading: boolean;
  readonly weekly: AggregateBucket[];
  readonly records: PersonalRecords | null;
}

export function useStatistics(): StatisticsState {
  // TODO: get `statistics` from useServices(). In one effect (with the `cancelled`
  // flag from Phase 03), load the last 12 weeks with getAggregates('weekly', ...)
  // and getPersonalRecords() together (Promise.all), then save them in state.
  // Optional: reload when the screen comes back into view (useFocusEffect from expo-router).
}
```

The screen (given), `src/features/statistics/StatisticsScreen.tsx`:

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

        {/* Once this works, copy the block above for calories, time and average speed. */}

        <Text variant="subheadline">Personal records</Text>
        {s.records?.longestDistance && <MetricTile value={formatKm(s.records.longestDistance.value)} unit="km, longest distance" />}
        {s.records?.longestDuration && <MetricTile value={formatClock(s.records.longestDuration.value)} unit="longest workout" />}
        {s.records?.fastestAverage && <MetricTile value={formatOneDecimal(s.records.fastestAverage.value)} unit="km/h, fastest average" />}
      </ScrollView>
    </Screen>
  );
}
```

`src/app/statistics.tsx` is the usual one-line route. Add a "Statistics" link on the
dashboard.

Try it with the fake provider (10.5) before the real SQL.

### 10.4 — History, and each workout's chart (queries yours, screens given)

**The list.** `listWorkouts(limit, offset)`: finished and abandoned workouts, newest
first (`ORDER BY StartedAtUtc DESC LIMIT $limit OFFSET $offset`), with only what a row
shows. Phase 12 times this with 5,000 workouts.

```ts
export interface WorkoutListItem {
  readonly workoutId: string;
  readonly startedAtUtc: string;
  readonly startOffsetMinutes: number;
  readonly durationSeconds: number;
  readonly distanceMeters: number;
}
```

**Fewer points.** Pick every n-th sample with `WHERE ElapsedSec % $n = 0`. Work `$n` out
from the workout's length and the number of points you want; don't hardcode it. Careful:
samples are at 0, 5, 10, ... seconds, so `$n` must be a multiple of 5 or almost nothing
matches.

**Gaps.** A sample with `Flags & 1` is a gap. The chart breaks the line at every `null`
(10.3), so turning gap rows into `null` is where "gaps show as breaks" actually happens.
Pass the saved value through instead, or use `0`, and the chart draws a made-up line.
Also: a gap row might not land on a multiple of `$n` and get skipped. Keep gap rows
anyway: `WHERE (ElapsedSec % $n = 0 OR Flags & 1 = 1)`.

1. The shape. Values can be `null`, so a gap stays `null` all the way to the chart:
   ```ts
   export interface WorkoutSamplePoint {
     readonly elapsedSec: number;
     readonly speedKph: number | null;
     readonly heartRate: number | null;
   }
   ```
2. `getWorkoutSeries(workoutId, maxPoints)`:
   ```ts
   async getWorkoutSeries(workoutId: string, maxPoints: number): Promise<WorkoutSamplePoint[]> {
     // TODO: 1. find the workout's last ElapsedSec, and work out $n (a multiple of 5, at least 5)
     // TODO: 2. SELECT ElapsedSec, SpeedKph, HeartRate, Flags FROM WorkoutSample
     //          WHERE WorkoutRowId = (...) AND (ElapsedSec % $n = 0 OR Flags & 1 = 1)
     //          ORDER BY ElapsedSec
     // TODO: 3. for gap rows (Flags & 1), set speedKph and heartRate to null
   }
   ```
3. Test with a few gap rows in the middle (some not on a multiple of `$n`): `null` at
   exactly those points, and every gap row kept.
4. Two hooks: `useWorkoutList()` (the first 50) and `useWorkoutDetail(workoutId)`
   (`getWorkoutSeries(workoutId, 200)`), same effect pattern as `useStatistics`.

The list (given), `src/features/history/HistoryScreen.tsx`:

```tsx
import { Link } from 'expo-router';
import { FlatList, Pressable, View } from 'react-native';

import { useWorkoutList } from './useWorkoutList';
import { formatClock, formatKm } from '@/features/shared/format';
import { Screen } from '@/features/shared/theme/Screen';
import { Surface } from '@/features/shared/theme/Surface';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';

/** The workout's local date and time, using its own saved offset. */
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

The workout page (given), `src/features/history/WorkoutDetailScreen.tsx`:

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
  // Left out entirely (not shown empty) unless V3 said heart rate is usable.
  if (HEART_RATE_VERDICT === 'usable') {
    series.push({ label: 'Heart rate (bpm)', axis: 'right', dashed: true, values: points.map((p) => p.heartRate) });
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={{ padding: spacing.lg, gap: spacing.md }}>
        <Text variant="subheadline">Speed{series.length > 1 ? ' and heart rate' : ''}</Text>
        <Surface style={{ padding: spacing.sm }}>
          <LineChart series={series} xLabels={points.map((p) => formatClock(p.elapsedSec))} height={240} accessibilityLabel="Speed over the workout" />
        </Surface>
        <Text variant="caption">Breaks in the line are moments the connection dropped.</Text>
      </ScrollView>
    </Screen>
  );
}
```

The routes: `src/app/history/index.tsx` (one line, the list) and
`src/app/history/[workoutId].tsx`:

```tsx
import { useLocalSearchParams } from 'expo-router';

import { WorkoutDetailScreen } from '@/features/history/WorkoutDetailScreen';

export default function WorkoutDetailRoute() {
  const { workoutId } = useLocalSearchParams<{ workoutId: string }>();
  return <WorkoutDetailScreen workoutId={workoutId} />;
}
```

The history uses `FlatList`, not `.map()`, because it grows every day: `FlatList` only
draws the rows on screen.

1. Write and test `listWorkouts` and `getWorkoutSeries` first.
2. Paste the screens and routes, and add a "History" link on the dashboard.
3. Using the fake (10.5), make a workout with a gap and check by eye that the line
   breaks.

### 10.5 — `FakeStatisticsProvider` (yours, short)

Create `src/core/statistics/fakeStatisticsProvider.ts`. Like the other fakes: return
believable made-up totals, records, list items and points (including one workout with a
gap). Put it in the services file first, build the screens against it, then switch to
`SqliteStatisticsProvider` once the queries are reviewed. One line changes.

---

## Tests

- Weekly totals for known data match your hand calculation
- A workout at 23:30 local lands on the right **local** day, and one with a negative
  offset doesn't disappear
- A daylight-saving week and a New Year's week each add up correctly as one week
- No data: totals are `[]`, records are `null`, and both screens show without crashing
- 5,000 workouts: totals under 200 ms (measured on the phone in Phase 12)
- `getWorkoutSeries` with gaps: `null` at exactly the gap points, and every gap row kept

## Done when

- [ ] Daily, weekly and monthly totals match hand calculations
- [ ] Nothing calculated is saved in the database
- [ ] The trend chart works with the fake, then with real data
- [ ] A workout with a dropped connection shows a visible break, never a joined-up line
- [ ] The heart rate line is missing entirely (not empty) if V3 said unusable
