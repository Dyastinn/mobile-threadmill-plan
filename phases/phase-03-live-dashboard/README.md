# Phase 03 — Dashboard + Workout Calendar

> You write the logic; the agent explains first and reviews after (see `../README.md`).

**Treadmill needed:** no for building (uses the fake), yes to check at the end
**Size:** M · **Waits for:** Phase 01b only (not 01a)

---

## Goal

The app's real home screen:

1. **Live numbers**: speed, distance, calories, time, built against `TreadmillService`
   and tested with the fake treadmill.
2. **A workout calendar** at the top, like GitHub's contribution graph: one square per
   day, filled if you worked out. It uses fake history until Phase 06 saves real
   workouts.

Phase 00's home list (the test screens) moves to `src/app/diagnostics/index.tsx`,
reachable from a small "Diagnostics" link. Phase 13 hides it behind a setting.

## What you'll learn

- **The fake-data trick again**, for a different problem (workout history). It's a
  reusable technique, not a one-off.
- **A hook as the screen's brain**: `useDashboard()` listens to the treadmill, keeps
  the screen's state, and cleans up when the screen closes. The screen just shows what
  the hook returns.
- **Effect cleanup**: every `onSample(...)` returns a function to stop listening. The
  effect must return it, or the listener keeps running after the screen closes.
- **Slowing down updates** with `useRef`, so a fast data stream doesn't redraw the
  screen too often.
- **Deciding where code goes** (`core/` or `features/`) yourself: does it import React,
  React Native, or a native library?

## Read first

- `src/core/treadmill/treadmillService.ts`
- Phase 01's FTMS reference: "Treadmill Data" and "Heart Rate"
- `PHASE-00-FINDINGS.md`: the notification rate and the heart rate verdict (V3)
- [`00a-React-And-TypeScript-Essentials.md`](../../docs/learning/00a-React-And-TypeScript-Essentials.md):
  the sections on effects, refs and re-rendering
- [`04-Monochrome-Theme.md`](../../docs/learning/04-Monochrome-Theme.md): the
  components the screen uses
- **`useEffect`**: https://react.dev/reference/react/useEffect ("Connecting to an
  external system" is exactly this phase)
- **`useRef`**: https://react.dev/reference/react/useRef
- **Synchronizing with Effects**: https://react.dev/learn/synchronizing-with-effects.
  Explains why Strict Mode runs your effect twice in development: to prove your
  cleanup works.

---

## Technology decision: plain services + hooks, no state library

**Problem.** Screens need to show values that live outside React (the connection, the
workout, settings) and update when they change, without every screen redoing the same
plumbing.

**Why this.** The services already work like stores: they have "get the current value"
methods and "tell me when it changes" events. React's built-in `useSyncExternalStore`
is made for exactly that. A screen's hook is then 10–30 lines of plain React. No
library to learn; everything leads back to your code or React's docs.

**Alternatives.**

1. **Zustand.** Good: tiny and popular. Bad: you'd keep state in two places (the store
   and the service), which drift apart. Better for state that only exists in the UI and
   is shared across many screens.
2. **Redux Toolkit.** Good: very structured, great dev tools. Bad: lots of ceremony for
   an app whose state already lives in a handful of services. Better for big apps with
   many developers.
3. **MobX / Legend-State.** Good: very little code once set up. Bad: a different way of
   thinking, and "why did this redraw?" gets harder to answer. Better for deeply nested,
   fast-changing state.

**Why not those.** They all add a second home for state that already has one. If
UI-only shared state ever shows up, Zustand is the one to add.

**Long term.** Nothing extra to maintain. `useSyncExternalStore` is stable React.

**In the code:** Phase 02's `useConnectionState()` and this phase's `useDashboard()`.

---

## Part 1 — Live numbers

### The idea

The treadmill might send data faster than the screen needs to redraw. Each
`setState` makes React redraw, and in split screen the phone is also playing a video.
More than about 4 redraws a second is wasted work: your eyes can't use it.

**The fix is one `if`.** Remember when you last updated. If it was less than 250 ms
ago, skip this sample. The next one arrives soon and carries a fresher number. That
gives you both things you need: at most 4 updates a second, and the number shown is
never stale (a skipped sample is replaced, not queued).

**The name for this:** *throttle*: update at a limited rate while data keeps coming.
Not to be confused with *debounce*: wait until the data stops (like waiting for
someone to stop typing). Debouncing treadmill speed would mean the screen barely
updates during a steady walk.

**Why `useRef` for the time, not `useState`?** Changing state redraws the screen.
Recording "I just updated" must *not* cause another redraw. A ref keeps a value
between redraws without causing one.

### What the screen shows

- Speed, distance, calories, time, and machine status
- Heart rate **only if V3 said "usable"**
- Connection status
- **Fields the treadmill doesn't send are hidden, not shown as `--`.**

### Tasks

**3.1: Set up**

1. Create `src/features/dashboard/`.
2. You'll add `useDashboard.ts` (yours), `DashboardScreen.tsx` (given below) and
   `src/features/shared/format.ts` (given below).
3. Move Phase 00's home list: `git mv src/app/index.tsx src/app/diagnostics/index.tsx`,
   so `/` is free for the dashboard.
4. Check that Phase 01b is done: `treadmillService.ts` is in `src/core/treadmill/` and
   the services file has `treadmill`.

**3.2: `useDashboard` (yours)**

- Get `treadmill` from `useServices()`.
- In one `useEffect`, listen to `onSample` and `onConnectionStateChange`. Return a
  cleanup that calls both unsubscribe functions.
- Return everything the screen shows: `speedKph`, `distanceMeters` (keep metres; the
  screen converts to km), `calories`, `elapsedSeconds`, `heartRate` (all
  `number | null`), and `connectionState`.
- One flag per number: `showsSpeed`, `showsDistance`, `showsCalories`,
  `showsElapsedTime`, `showsHeartRate`. For now, "shown if the latest sample has a
  value for it". Phase 01a will improve this; the screen won't change.
- Heart rate also depends on V3. Put this in `useDashboard.ts`:
  `export const HEART_RATE_VERDICT: 'usable' | 'marginal' | 'unusable' = 'unusable';`
  Only `'usable'` lets heart rate show. Keep the type written out like that: without
  it, TypeScript treats the value as exactly `'unusable'`, and a later check for
  `'usable'` (Phase 10 has one) won't compile.
- The throttle is **your design**. It must guarantee: at most 4 updates a second, and
  the newest sample always wins.

An outline:

```ts
export interface DashboardState {
  readonly connectionState: ConnectionState;
  readonly speedKph: number | null;
  readonly distanceMeters: number | null;
  readonly calories: number | null;
  readonly elapsedSeconds: number | null;
  readonly heartRate: number | null;
  readonly showsSpeed: boolean;
  readonly showsDistance: boolean;
  readonly showsCalories: boolean;
  readonly showsElapsedTime: boolean;
  readonly showsHeartRate: boolean;
}

export function useDashboard(): DashboardState {
  const { treadmill } = useServices();
  const [sample, setSample] = useState<TreadmillSample | null>(null);
  const [connectionState, setConnectionState] = useState(treadmill.getConnectionState());
  const lastUpdateMs = useRef(0);

  useEffect(() => {
    const offSample = treadmill.onSample((next) => {
      // TODO: if less than 250 ms since lastUpdateMs.current, return
      // TODO: setSample(next) and record the time
    });
    const offState = treadmill.onConnectionStateChange((change) => setConnectionState(change.state));
    return () => {
      // TODO: stop both listeners. What happens if you forget one?
    };
  }, [treadmill]);

  // TODO: work out the shows* flags from `sample`, and return everything.
  // Do this directly here, not in another effect.
}
```

**3.3: The screen (given)**

Display formatting, `src/features/shared/format.ts`:

```ts
/** Metres to "1.23" km. */
export function formatKm(meters: number | null): string {
  return meters === null ? '' : (meters / 1000).toFixed(2);
}

/** Seconds to "mm:ss", or "h:mm:ss" after an hour. */
export function formatClock(totalSeconds: number | null): string {
  if (totalSeconds === null) return '';
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = Math.floor(totalSeconds % 60);
  const mm = String(m).padStart(2, '0');
  const ss = String(s).padStart(2, '0');
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

/** One decimal place, e.g. speed. */
export function formatOneDecimal(value: number | null): string {
  return value === null ? '' : value.toFixed(1);
}
```

Good to know: `toFixed` always writes `6.5`, never `6,5`, whatever the phone's
language. That matters in Phase 15.

`src/features/dashboard/DashboardScreen.tsx`:

```tsx
import { Link } from 'expo-router';
import { ScrollView, View } from 'react-native';

import { useDashboard } from './useDashboard';
import { formatClock, formatKm, formatOneDecimal } from '@/features/shared/format';
import { ConnectionDot } from '@/features/shared/theme/ConnectionDot';
import { MetricTile } from '@/features/shared/theme/MetricTile';
import { Screen } from '@/features/shared/theme/Screen';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';

export function DashboardScreen() {
  const d = useDashboard();

  return (
    <Screen>
      <ScrollView contentContainerStyle={{ padding: spacing.lg, gap: spacing.xl }}>

        {/* Task 3.8 puts the workout calendar here */}

        <View
          style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.sm }}
          accessible
          accessibilityLabel={`Treadmill ${d.connectionState}`}
        >
          <ConnectionDot connected={d.connectionState === 'ready'} />
          <Text variant="caption">{d.connectionState}</Text>
        </View>

        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md }}>
          {d.showsSpeed && (
            <MetricTile value={formatOneDecimal(d.speedKph)} unit="km/h" style={{ flexBasis: '47%', flexGrow: 1 }} />
          )}
          {d.showsDistance && (
            <MetricTile value={formatKm(d.distanceMeters)} unit="km" style={{ flexBasis: '47%', flexGrow: 1 }} />
          )}
          {d.showsCalories && (
            <MetricTile value={String(d.calories ?? '')} unit="kcal" style={{ flexBasis: '47%', flexGrow: 1 }} />
          )}
          {d.showsElapsedTime && (
            <MetricTile value={formatClock(d.elapsedSeconds)} unit="elapsed" style={{ flexBasis: '47%', flexGrow: 1 }} />
          )}
        </View>

        {d.showsHeartRate && <MetricTile value={String(d.heartRate ?? '')} unit="bpm" />}

        <Link href="/diagnostics" style={{ alignSelf: 'center' }}>
          <Text variant="caption">Diagnostics</Text>
        </Link>

      </ScrollView>
    </Screen>
  );
}
```

And the route file, `src/app/index.tsx`. Route files only pick a screen:

```tsx
import { DashboardScreen } from '@/features/dashboard/DashboardScreen';

export default DashboardScreen;
```

Worth knowing about this screen:
- Every number uses the theme's `MetricTile`. No new styles, no colors.
- `{d.showsSpeed && <MetricTile ... />}` is the whole "hidden, not `--`" rule: if the
  flag is false, the tile isn't drawn at all.
- `flexBasis: '47%'` with wrapping gives two tiles per row, or one per row on a narrow
  screen. That's a head start on split screen (Phase 11).

**3.4: Run it**

1. `npx expo start`, open the app. You should land on the dashboard.
2. Connect the fake (a temporary "Connect fake" button, or connect it when the app
   starts; decide at the review) and watch the numbers move.

**Which fields show** will come from the fields that actually arrive (Phase 01a),
**never** from `0x2ACC`, which lies on this treadmill. Until then, show whatever the fake
sends.

**Heart rate source:** `0x2A37` if usable; the `0x2ACD` field only if `180D` sends
nothing; **not shown at all if V3 said unusable**. If V3 said marginal, it's still saved
(Phase 06) but not shown here.

### Rules

- Listeners run on the one JavaScript thread. Calling `setState` inside one is fine.
- **At most 4 updates a second**, however fast data arrives. How is your design.
- Convert units on screen only. Everything below the screen stays metric.
- List `treadmill` in the effect's dependency array. It never changes, but lint
  expects it, and it's honest.

### Tests

- Fake treadmill: 10 minutes of data, the screen keeps updating.
- Missing fields: absent ones are hidden, present ones show.
- Go to Diagnostics and back several times, then check the fake has no leftover
  listeners (give the fake a listener count, or log subscribe/unsubscribe). This tests
  your cleanup.
- Treadmill: walk 5 minutes; every number updates and matches the treadmill's display.
- Treadmill: rotate the phone mid-workout; no crash, nothing resets.

---

## Part 2 — Workout calendar

### The idea

A grid of days, filled if you worked out, so you can see your routine at a glance. But
real workout history doesn't exist until Phase 06. So, same trick as Phase 01b: define
the shape of the data you need (`WorkoutHistoryProvider`), make a fake that returns
made-up history (`FakeWorkoutHistoryProvider`), and build the calendar against it.
Phase 06 adds the real one; the calendar doesn't change.

**Why not wait for Phase 06?** The interface is tiny, and building against a fake means
the calendar is designed and reviewed now, not blocked.

**Why not color days by how many workouts?** Nothing needs that yet. Each day is filled
or empty. The data already includes the count, so adding shades later only changes one
line. The name for this: *YAGNI*, "you aren't gonna need it". Don't build it until
something needs it, especially when adding it later is cheap.

### What it looks like

Like GitHub: one column per week, seven rows (Monday at the top), the last 16 weeks,
newest on the right. Filled or empty, no shades.

### Tasks

**3.5: The interface and the fake, in `core/`**

1. Create `src/core/history/`.
2. `workoutHistoryProvider.ts`:
   ```ts
   /**
    * A calendar day, as "YYYY-MM-DD".
    * A string, not a Date: a JavaScript Date is a moment in time, and can shift to
    * the neighbouring day across time zones. The string sorts correctly and matches
    * what SQLite's date() returns.
    */
   export type LocalDate = string;

   export interface DailyWorkoutCount {
     readonly day: LocalDate;
     readonly count: number;
   }

   export interface WorkoutHistoryProvider {
     getDailyCounts(fromLocal: LocalDate, toLocal: LocalDate): Promise<DailyWorkoutCount[]>;
   }
   ```
   Also write two small helpers: `addDays(day, n)` and `todayLocal()`. Test `addDays`
   across a month end, a year end, and the end of February in a leap year.
3. `fakeWorkoutHistoryProvider.ts`:
   ```ts
   export class FakeWorkoutHistoryProvider implements WorkoutHistoryProvider {
     async getDailyCounts(fromLocal: LocalDate, toLocal: LocalDate): Promise<DailyWorkoutCount[]> {
       // TODO: go day by day from fromLocal to toLocal. Give each day a believable
       // chance of a workout (a few days a week). Return the list.
       // Bonus: make it the same every time, so the calendar doesn't reshuffle on reload.
       throw new Error('not implemented');
     }
   }
   ```
4. Add it to the services file as `history`, like `treadmill`.

**3.6: Add it to the hook**

1. In `useDashboard`, also get `history` from `useServices()`.
2. Add `contributionDays: ContributionDay[]` to `DashboardState`, where
   `ContributionDay` is `{ day: LocalDate; isLit: boolean }`. Define it next to the
   hook. (`core/` shouldn't know what "lit" means on screen.)
3. Load it in a second `useEffect`: call `history.getDailyCounts(...)` for the last 16
   weeks, turn each count into `{ day, isLit: count > 0 }`, and save it in state. If the
   screen closes before the data arrives, don't call `setState`. The standard way: a
   `let cancelled = false` in the effect, set to `true` in its cleanup, checked before
   `setState`.

**3.7: The calendar (given)**

`src/features/dashboard/ContributionGraph.tsx`:

```tsx
import { View } from 'react-native';

import type { ContributionDay } from './useDashboard';
import { useTheme } from '@/features/shared/theme/useTheme';

const CELL = 14;
const GAP = 4;

/** Monday = 0 ... Sunday = 6, for a "YYYY-MM-DD" day. */
function weekdayIndex(day: string): number {
  const jsDay = new Date(`${day}T12:00:00Z`).getUTCDay(); // noon UTC: no time zone can change the date
  return (jsDay + 6) % 7;
}

/** Splits days (oldest first) into weeks of 7, with blanks before the first day. */
function toWeeks(days: readonly ContributionDay[]): (ContributionDay | null)[][] {
  const weeks: (ContributionDay | null)[][] = [];
  let column: (ContributionDay | null)[] = [];
  const first = days[0];
  if (first) {
    for (let i = 0; i < weekdayIndex(first.day); i++) column.push(null);
  }
  for (const day of days) {
    column.push(day);
    if (column.length === 7) {
      weeks.push(column);
      column = [];
    }
  }
  if (column.length > 0) weeks.push(column);
  return weeks;
}

export function ContributionGraph({ days }: { days: readonly ContributionDay[] }) {
  const { colors } = useTheme();
  const weeks = toWeeks(days);
  const litCount = days.filter((d) => d.isLit).length;

  return (
    <View
      style={{ flexDirection: 'row', gap: GAP, alignSelf: 'center' }}
      accessible
      accessibilityLabel={`Workout days: ${litCount} in the last ${weeks.length} weeks`}
    >
      {weeks.map((week, w) => (
        <View key={w} style={{ gap: GAP }}>
          {week.map((cell, i) => (
            <View
              key={cell?.day ?? `pad-${i}`}
              style={{
                width: CELL,
                height: CELL,
                borderRadius: 3,
                backgroundColor:
                  cell === null ? 'transparent' : cell.isLit ? colors.contributionLit : colors.contributionUnlit,
              }}
            />
          ))}
        </View>
      ))}
    </View>
  );
}
```

Worth knowing:
- Each column is a week, Monday at the top. 16 weeks is 288 dp wide, which fits a
  phone. For a full year, wrap it in a horizontal `ScrollView`.
- The first column starts with blanks, so day 1 lands on the right weekday.
- `key` tells React which square is which between redraws. The day is unique, so it's
  a good key.
- Screen readers hear one summary ("Workout days: 23 in the last 16 weeks"), not 112
  separate squares.

**3.8: Put it on the dashboard**

1. In `DashboardScreen.tsx`, replace the `{/* Task 3.8 ... */}` comment with
   `<ContributionGraph days={d.contributionDays} />` and add
   `import { ContributionGraph } from './ContributionGraph';`.
2. Run it: you should see a scattering of filled and empty squares straight away.

### Review

The agent checks: can Phase 06 implement `WorkoutHistoryProvider` without changing it?
Is the fake believable? Do both effects clean up, and does the async one check the
`cancelled` flag?

---

## Done when

- [ ] The numbers update smoothly for 10 minutes
- [ ] Treadmill: the screen matches the treadmill's display at three speeds
- [ ] No field shows a placeholder for data the treadmill never sends
- [ ] No listener is left running after leaving the dashboard
- [ ] The calendar shows filled and empty days from the fake
- [ ] `WorkoutHistoryProvider` lives in `core/`, imports nothing phone-specific, and is
      in the services file like `treadmill`
