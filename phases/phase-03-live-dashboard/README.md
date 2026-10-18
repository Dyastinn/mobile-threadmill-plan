# Phase 03 — Live Dashboard + Contribution Graph

> See `../README.md` for the collaboration model: you write the code, the agent
> explains concepts up front and reviews after.

**Hardware:** none for development (`FakeTreadmillService`), yes to verify at the end
**Size:** M · **Blocked by:** Phase 01b only (not 01a)

---

## Goal

Build the app's real home/dashboard screen: live treadmill metrics (built entirely
against `TreadmillService`, developed and tested with `FakeTreadmillService`, real
hardware only to verify at the end) **and** a GitHub-style workout "contribution
graph" at the top of the screen, using a fake data source until Phase 06 provides
real workout history.

This screen replaces Phase 00's home list as the app's actual front door. Phase 00's
six diagnostic screens don't disappear (they're still useful for debugging), but
they stop being the first thing the app shows: the old home list moves to
`src/app/diagnostics/index.tsx`, reachable from a small "Diagnostics" link on the
dashboard for now. Phase 13 hides it behind a developer toggle.

## Learning goals

- Building the **same seam/fake pattern from Phase 01b**, applied to a second,
  unrelated problem (workout history instead of BLE). The point is to notice it's a
  repeatable technique, not a one-off trick.
- **A hook as a screen's "ViewModel"**: `useDashboard()` subscribes to the service in
  a `useEffect`, holds the screen's state, and cleans up when the screen goes away.
  The screen component calls it and renders what it returns; it never touches the
  service itself.
- **Effect cleanup** as the thing that prevents leaks: every `onSample(...)` returns an
  unsubscribe function, and the effect must return it. Forget, and a listener keeps
  calling `setState` on a screen that no longer exists.
- Throttling a high-frequency event stream for rendering, with a `useRef` (a value
  that persists across renders *without* causing one), so updates don't outpace what a
  screen can usefully draw.
- Deciding what belongs in `core/` vs. `features/`. You'll make this call yourself this
  time, using the same test the agent applied in Phase 01b: does this code import
  React, React Native or a native library?

## Reference docs

- `src/core/treadmill/treadmillService.ts` (moved here in Phase 01b)
- `../phase-01-protocol-decode/README.md`'s FTMS protocol reference: "Treadmill Data"
  (fields) and "Heart Rate Service" sections
- `../phase-00-probe-app/PHASE-00-FINDINGS.md`: notification rate, flags observed,
  V3 heart rate verdict
- `docs/learning/00a-React-And-TypeScript-Essentials.md`: the effects, refs and
  re-render sections. Read them before task 3.2 if they're hazy.
- `docs/learning/04-Monochrome-Theme.md`: every token and component this phase's UI
  uses is defined there
- **`useEffect`**: https://react.dev/reference/react/useEffect. The "Connecting to an
  external system" example is this phase's exact shape.
- **`useRef`**: https://react.dev/reference/react/useRef. "Referencing a value with a
  ref" is the throttle's timestamp.
- **Synchronizing with Effects**: https://react.dev/learn/synchronizing-with-effects.
  Especially "How to handle the Effect firing twice in development": React's Strict
  Mode deliberately subscribes, unsubscribes and subscribes again in development, to
  prove your cleanup works.

---

## Technology decision: plain services + hooks (no state library)

**What problem does it solve?** Screens need to show state that lives outside React
(the treadmill connection, the workout engine, settings) and update when it changes,
without every screen re-implementing subscribe/unsubscribe plumbing, and without a
silent bug mode where the screen just stops updating.

**Why are we using it?** The services already *are* the stores: `TreadmillService`
has synchronous getters plus subscribe-returns-unsubscribe events, which is precisely
the contract React's built-in `useSyncExternalStore` was designed for. A screen hook
is then 10–30 lines of plain React (`useSyncExternalStore` for "current value",
`useEffect` + `useState` for streams you transform, like the throttled samples). No
library to learn, nothing hidden: go-to-definition on anything lands in your own code
or React's documented API.

**Alternatives considered:**

1. **Zustand.** Pros: tiny (a few KB), very popular, a store is one function call, and
   components subscribe to slices without extra boilerplate. Cons: a second place
   state can live (the Zustand store *and* the service), so you'd either mirror
   service state into the store (two sources of truth, the bug this project keeps
   avoiding) or make the store the service (a rewrite of the seam). Learning curve:
   low. The better choice for state that genuinely only exists in the UI and is
   shared across many screens, which this app barely has.
2. **Redux Toolkit.** Pros: the most structured option, excellent devtools, every
   change is an explicit, logged action. Cons: actions, reducers, slices and a store
   for an app whose state already lives in about five service objects; a lot of
   ceremony for no added correctness here. Learning curve: medium. The better choice
   for large apps with many developers and complex cross-cutting state.
3. **MobX / Legend-State** (observable-based). Pros: automatic fine-grained
   re-rendering, very little code once set up. Cons: an entirely different mental
   model (observables, implicit tracking), and "why did this re-render?" becomes
   harder to answer, which matters most to someone learning React's own model.
   Learning curve: medium. The better choice for very dynamic, deeply nested state.

**Why not the alternatives?** Every one of them adds a second layer over state that
already has a clean home in the services. Zustand is the only genuinely competitive
option, and it's the one to reach for if a real piece of UI-only shared state shows up
later (none has so far).

**Long-term considerations.** Nothing to maintain beyond React itself.
`useSyncExternalStore` is part of React's stable API. If the app ever outgrows plain
hooks, adding Zustand for UI-only state is cheap and doesn't touch the services.

**Practical example:** `useConnectionState()` from Phase 02, and this phase's
`useDashboard()`.

---

## Part 1 — Live metrics

### Understanding what you're building (read this before the tasks)

**The problem.** The treadmill can push samples faster than a screen full of metric
tiles can usefully redraw (Phase 00's findings recorded the actual notification rate;
see `PHASE-00-FINDINGS.md`). It's the same mismatch as a car's speedometer needle: the
sensor samples hundreds of times a second, but the needle only moves smoothly 10-20
times a second, and a human eye can't use information faster than that anyway.
`useDashboard` sits between a fast, uneven data source and a screen where five values
(`speedKph`, `distanceMeters`, `calories`, `elapsedSeconds`, `heartRate`) all repaint
tiles. In React, every `setState` schedules a re-render of the component and its
children; every unnecessary one is wasted work on a phone that, in split screen, is
also playing a video.

**Why not just render every sample as it arrives?** The naive version (call
`setState(sample)` inside every `onSample` callback) is what task 3.2's skeleton
starts from, then explicitly guards against with one `if` check. The fix isn't a
timer, a queue, or a reactive pipeline; it's the plainest possible throttle: remember
when you last updated (in a ref), and if less than ~250 ms have passed, skip the
update and let the next sample (a quarter-second later, at most) carry the fresher
number instead. That single comparison is enough to guarantee both things this phase
actually needs: no more than 4 updates a second, and the *displayed* value is never
stale, because a skipped sample is simply superseded by the next one rather than
queued up behind it. Reaching for a dedicated timer or a library's `throttle` here
would solve a problem this app doesn't have: there's no backlog to drain, no
requirement to batch multiple samples together, just "don't repaint faster than
useful."

**The pattern, named plainly.** What's being applied here is specifically
**throttle**, not its cousin **debounce**, and the distinction matters for picking
the right one. Debounce waits for a stream to go *quiet* before firing (useful for
a search box: wait until the user stops typing). Throttle fires at a bounded,
regular rate *while* the stream keeps producing, always carrying the latest value.
That's what a continuously-changing number like treadmill speed needs; debouncing
it would mean the UI barely updates at all during a steady, ongoing workout, since
the stream never really goes quiet. The cost of throttling here is one ref and one
comparison per event, genuinely cheap. The payoff is a UI that stays responsive under
a notification rate the phone can't (and doesn't need to) fully render. The pattern
isn't needed everywhere, though: Phase 01b's `FakeTreadmillService` emits samples
roughly once a second by design, already under the 4 Hz ceiling. No throttle was
needed there because the *source*, not just the consumer, was already UI-safe. It only
earns its place here because the real device's notification rate isn't guaranteed to
be.

**Why a ref and not state for the timestamp?** Because changing it must *not* cause a
render. If `lastUpdateMs` were state, recording "I just updated" would itself trigger
another render, which is the opposite of throttling. A ref is a box React keeps for
you across renders and ignores for rendering purposes.

### Your tasks

- Speed, distance, calories, elapsed time, machine status, on a new dashboard screen
- Heart rate **only if V3 (Phase 00 findings) said usable**
- Connection indicator
- **Fields the device does not actually send are hidden, not shown as `--`.** A row
  of dashes is a promise the app can't keep.

### Walkthrough

**3.1: Where this lives**

1. Create the folder `src/features/dashboard/`.
2. Files you'll add in this part: `useDashboard.ts` (yours to write, spec below),
   `DashboardScreen.tsx` (UI, given in full below), and `src/features/shared/format.ts`
   (display formatting, given in full below).
3. Move Phase 00's home list from `src/app/index.tsx` to
   `src/app/diagnostics/index.tsx` (`git mv`), so `/` is free for the dashboard.
4. Confirm `treadmillService.ts` has actually moved to `src/core/treadmill/` (Phase
   01b's task 1b.1) and `services.tsx` exposes `treadmill`. Everything below assumes
   that seam already exists.

**3.2: `useDashboard`: the spec**

- Reads `treadmill` from `useServices()`; nothing else for Part 1.
- Subscribes to `onSample` and `onConnectionStateChange` in one `useEffect`, and
  returns a cleanup function that calls both unsubscribe functions.
- Returns one object with every value the screen renders: `speedKph`,
  `distanceMeters` (store metres; the screen converts to km for display only),
  `calories`, `elapsedSeconds`, `heartRate` (all `number | null`, matching
  `TreadmillSample`'s own nullability) plus `connectionState`.
- One boolean per metric for the "hidden not `--`" rule: `showsSpeed`,
  `showsDistance`, `showsCalories`, `showsElapsedTime`, `showsHeartRate`. Until Phase
  01a's `capabilityTracker` exists, the honest rule available today is "shown if the
  latest sample actually carries a non-null value for it": a field genuinely absent
  from every packet stays hidden; once 01a lands, swap this for the accumulated-flags
  check the Goal section already describes. The screen doesn't change either way.
- Heart rate additionally needs a manual override for the V3 verdict in
  `PHASE-00-FINDINGS.md`: "Unusable" or "Marginal" means `showsHeartRate` is
  hard-`false` regardless of what the sample contains; only "Usable" lets the
  presence-based rule above apply. A single exported constant in `useDashboard.ts` is
  enough:
  `export const HEART_RATE_VERDICT: 'usable' | 'marginal' | 'unusable' = 'unusable';`.
  The explicit type annotation matters: without it TypeScript infers the narrow type
  `'unusable'`, and a later `HEART_RATE_VERDICT === 'usable'` check (Phase 10 has one)
  fails to compile as "this comparison is always false".
- The throttle is **your design decision**, per the Implementation requirements
  below. Whatever mechanism you pick has to guarantee two things: no state update
  more than 4 times a second, and the *most recent* sample always wins (never hold on
  to a stale "last known good" speed while a fresher one waits behind it; that's
  actively misleading mid-workout, not just slow).

Skeleton, shape only:

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
      // TODO: throttle: if less than ~250 ms since lastUpdateMs.current, return
      // without updating (the next sample a quarter-second later carries fresher
      // data anyway; think about whether a plain "skip if too soon" check already
      // satisfies both guarantees above before reaching for a timer or queue)
      // TODO: setSample(next); lastUpdateMs.current = Date.now();
    });
    const offState = treadmill.onConnectionStateChange((change) => setConnectionState(change.state));
    return () => {
      // TODO: the cleanup. What happens if you forget one of these two calls?
    };
  }, [treadmill]);

  // TODO: derive and return DashboardState from `sample` and `connectionState`,
  // including the shows* flags per the design note above. Deriving during render
  // (not in another effect) is the React-idiomatic choice: see "You Might Not Need
  // an Effect" in docs/learning/03-Doc-Links.md.
}
```

**3.3: The dashboard screen (UI, written in full)**

Display formatting first, `src/features/shared/format.ts`. Pure formatting, not the
throttle/mapping logic above, so it's given complete:

```ts
/** Metres -> "1.23" km. Storage and everything upstream stays metres. */
export function formatKm(meters: number | null): string {
  return meters === null ? '' : (meters / 1000).toFixed(2);
}

/** Elapsed seconds -> "mm:ss", or "h:mm:ss" past an hour. */
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

`toFixed` is locale-independent in JavaScript: it always writes `6.5`, never `6,5`.
That matters later (Phase 15's CSV), and it's the opposite of the trap in most other
languages. The locale-sensitive one is `toLocaleString`.

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

        {/* Part 2 (task 3.8) replaces this comment with the contribution graph */}

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

And the route file, `src/app/index.tsx`, stays one line of real content. Route files
in this project only ever pick a screen; the screen lives in `features/`:

```tsx
import { DashboardScreen } from '@/features/dashboard/DashboardScreen';

export default DashboardScreen;
```

Two things worth understanding, even though you didn't write this file:
- Every metric is the theme's `MetricTile` (big number, small unit caption) from
  `docs/learning/04-Monochrome-Theme.md`, repeated; nothing here is a new style, and
  nothing here is a color.
- `{d.showsSpeed && <MetricTile ... />}` *is* the entire "hidden not `--`"
  implementation on the UI side: when the flag is false, the tile isn't rendered at
  all. No empty space, no dash.
- `flexBasis: '47%'` with `flexWrap` gives two tiles per row that reflow to one per
  row if the screen gets very narrow. That's the start of Phase 11's split-screen
  work, for free.

**3.4: Run it**

1. `npx expo start`, open the dev build. You should land on the dashboard on launch.
2. Connect the fake (a temporary "Connect fake" button, or call `connect` from
   `services.tsx` at startup; the review checkpoint decides which stays) and watch
   metrics move.

**Field visibility** comes from the union of observed `0x2ACD` flag bits (Phase 01a's
capability tracker, once it exists), **not** from `0x2ACC`. That bitmask on this
device over-claims (it advertises incline target setting on a machine with no
incline). Log it; never branch on it. Until Phase 01a lands, it's fine to show
whatever fields `FakeTreadmillService` populates.

**Heart rate source**: `0x2A37` from `180D` if usable; the FTMS field only if `180D`
is dead; **removed from the UI entirely if V3 said unusable.** If V3 said marginal,
keep recording it (Phase 06) and hide it on this screen.

### Implementation requirements

- Listeners are called on the one JavaScript thread, synchronously (see the design
  notes in `treadmillService.ts`). There is nothing to marshal; calling `setState`
  from inside a listener is fine.
- **Throttle UI updates to at most 4 Hz** even if notifications arrive faster. Above
  that, rendering competes with whatever else is on screen in split screen, for no
  visible benefit. (How you throttle is your design decision; think about what
  "throttle" actually needs to guarantee before picking a mechanism.)
- Format at display time only. **Never convert units before storage**: metric
  always, everywhere below the screen.
- The effect's dependency array lists `treadmill`. It never changes in practice (one
  instance for the app's lifetime), but listing it is honest and lint will insist.

### Tests

- Fake service: 10-minute stream renders continuously, no frozen UI
- Sparse-field scenario: absent fields are hidden, present ones render
- Unmount the dashboard (navigate to Diagnostics and back several times), then check
  the fake has no leftover listeners (expose a listener count on the fake, or log
  subscribe/unsubscribe). This is the effect-cleanup test.
- `[HUMAN]` Walk 5 minutes; every field updates and matches the treadmill console
- `[HUMAN]` Rotate the device mid-workout; no crash, no reset

---

## Part 2 — Contribution graph

### Understanding what you're building (read this before the tasks)

**The problem.** A contribution graph turns a list of workout dates into
something you can read at a glance instead of scrolling a log, the way a wall
calendar with a sticker on each workout day shows a pattern without you reading
any numbers. The catch is that the real list of workout dates doesn't exist yet.
Phase 06 (Recording & Schema) is what will actually write rows to the database
and let you query `Workout.StartedAtUtc` grouped by day. Part 2 is being built
now, ahead of that, using the same seam/fake technique this project already used
once in Phase 01b for the treadmill connection itself (`TreadmillService` /
`FakeTreadmillService`). `WorkoutHistoryProvider` and `FakeWorkoutHistoryProvider`
are that same idea applied to a second, unrelated problem. If Phase 01b's
rig-and-engine reasoning made sense there, it applies here unchanged: swap
"treadmill hardware" for "SQLite workout history," and everything else about
*why* holds.

**Why not wait for Phase 06, and why not build the full GitHub version?** Two
separate "why not simpler" questions sit inside this feature, pulling in opposite
directions. First: why not just leave this part of the dashboard blank until
Phase 06 ships real data? Because the interface costs almost nothing to define
now (`getDailyCounts(from, to)` returning a list of `DailyWorkoutCount`, task 3.5),
and doing so means the widget, the layout, and the "how do lit days look" decisions
all get built and reviewed *this* phase instead of blocking on Phase 06's database
work. It's a smaller win than Phase 01b's six-phase unblock, but the same shape of
win: one phase's UI work stops waiting on another phase's I/O work. Second, pulling
the other way: why not build GitHub's actual gradient-by-count coloring now, since
the data (`DailyWorkoutCount.count` is already a number, not just a boolean)
technically supports it? Because nothing in this project has asked for that yet.
Each day is either lit (≥1 workout) or unlit (none). There's no demonstrated need
for five shades of intensity yet, just "did a workout happen this day or not."

**The pattern, named plainly.** The gradient decision is a clean example of
**YAGNI** ("You Aren't Gonna Need It"): deliberately not building a feature until
something concrete actually calls for it, rather than speculatively supporting it
"in case." What makes it cheap to defer here specifically: `DailyWorkoutCount`
already carries the full `count`, not just a boolean, so the `count > 0` mapping
(task 3.6) is the only place that would need to change later. The seam and the data
shape underneath it don't need to be redesigned to add color intensity later, only
the one line that decides what "lit" means. That's what makes deferring it free
instead of risky: YAGNI is a bad trade when skipping a feature now means an expensive
redesign later, and a good one when, as here, the door is already left open at zero
cost.

### The feature

A GitHub-style grid showing which days had a workout, at the top of the dashboard,
above the connection status: one column per week, seven rows (Monday at the top),
the last 16 weeks, newest week on the right. **Simplified from GitHub's original**:
no gradient by count; each day is either lit or unlit. If you want the gradient
later, it's a good follow-up once the simple version works and is reviewed, not a
requirement now.

### The seam (same pattern as `TreadmillService`/`FakeTreadmillService`)

Real workout history doesn't exist until Phase 06. Rather than wait, define the shape
you need and fake it:

- **Interface**, in `core/` (it's plain data, no React Native): a function that
  returns daily workout counts over a date range. A starting shape is given in task
  3.5; adjust it if you see something Phase 06 can't implement.
- **Fake implementation**, also in `core/`: generates a plausible several-months of
  synthetic daily counts (some days with 0, some with 1+) so the widget has something
  real to render and look right immediately.
- **Real implementation** arrives in Phase 06, reading `Workout.StartedAtUtc` grouped
  by local day (there's a query pattern for exactly this in
  `../phase-06-recording-schema/README.md`). The dashboard's UI code does not change
  when this swap happens, only one line in `services.tsx` does.

### Walkthrough

**3.5: The seam, in `core/`**

1. Create `src/core/history/` (alongside `treadmill/`, `ftms/`, `capture/`, `data/`).
2. `workoutHistoryProvider.ts`, a starting shape:
   ```ts
   /**
    * A calendar day in the user's local time, as "YYYY-MM-DD".
    * A string rather than a Date on purpose: a contribution-graph cell is a calendar
    * day, not an instant, and a JavaScript Date is always an instant (it would
    * silently shift a day across a timezone). The string sorts correctly, compares
    * with ===, and is exactly what SQLite's date() returns, which is what Phase 06's
    * real implementation reads.
    */
   export type LocalDate = string;

   /** One local calendar day and how many workouts started on it. */
   export interface DailyWorkoutCount {
     readonly day: LocalDate;
     readonly count: number;
   }

   export interface WorkoutHistoryProvider {
     getDailyCounts(fromLocal: LocalDate, toLocal: LocalDate): Promise<DailyWorkoutCount[]>;
   }
   ```
   Also write two small pure helpers you'll need for the walk: `addDays(day:
   LocalDate, n: number): LocalDate` and `todayLocal(): LocalDate`. Test `addDays`
   across a month end, a year end, and the end of February in a leap year.
3. `fakeWorkoutHistoryProvider.ts` implements the interface, but "make it look like a
   real training history" is still real logic, not a one-liner, so it gets a skeleton
   rather than a full body:
   ```ts
   export class FakeWorkoutHistoryProvider implements WorkoutHistoryProvider {
     async getDailyCounts(fromLocal: LocalDate, toLocal: LocalDate): Promise<DailyWorkoutCount[]> {
       // TODO: walk fromLocal..toLocal one day at a time (addDays), giving each day a
       // plausible chance of 0 vs. 1+ workouts (a few days a week reads as realistic;
       // every day or no days doesn't) and return the list. Bonus: make it
       // deterministic (a tiny seeded random) so the graph doesn't reshuffle on every
       // reload.
       throw new Error('not implemented');
     }
   }
   ```
4. Add it to `services.tsx` as `history: WorkoutHistoryProvider`, the same
   one-slot pattern as `treadmill`, so swapping in Phase 06's real SQLite-backed
   implementation later touches only this one line.

**3.6: Wire it into the hook**

1. In `useDashboard`, also read `history` from `useServices()`.
2. Add `contributionDays: ContributionDay[]` to `DashboardState`, where
   `ContributionDay` is `{ day: LocalDate; isLit: boolean }`. It's a tiny UI-facing
   type, kept separate from `DailyWorkoutCount` so `core/` never has to know what
   "lit" means on screen. Define it next to the hook.
3. Load it in a second `useEffect` with an empty-ish dependency list: call
   `history.getDailyCounts(...)` for the last 16 weeks, map each `DailyWorkoutCount`
   to `{ day, isLit: count > 0 }`, and store it in state. One wrinkle worth getting
   right: if the screen unmounts before the promise resolves, don't call `setState`
   afterwards. A `let cancelled = false` in the effect, set to `true` in its cleanup,
   checked before `setState`, is the standard shape.

**3.7: The widget (UI, written in full)**

`src/features/dashboard/ContributionGraph.tsx`:

```tsx
import { View } from 'react-native';

import type { ContributionDay } from './useDashboard';
import { useTheme } from '@/features/shared/theme/useTheme';

const CELL = 14;
const GAP = 4;

/** Monday = 0 ... Sunday = 6, for a "YYYY-MM-DD" string. */
function weekdayIndex(day: string): number {
  // Noon UTC so no timezone can push it onto the neighbouring date.
  const jsDay = new Date(`${day}T12:00:00Z`).getUTCDay(); // Sunday = 0
  return (jsDay + 6) % 7;
}

/** Oldest-first days -> columns of 7 (one per week), padding the first column. */
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

A few things worth understanding, not just pasting:
- Each column is one week, top to bottom Monday to Sunday, which is the real GitHub
  layout. 16 weeks × 18 dp = 288 dp wide, which fits a phone in portrait with room to
  spare. If you widen it to a year later, wrap it in a horizontal `ScrollView`.
- The first column is padded with empty cells so that day 1 lands on its correct
  weekday row. That's the one piece of date arithmetic in this file, and it lives here
  rather than in your hook because it's purely about layout.
- `key` on each item is how React tells list items apart between renders. The day
  string is unique, which makes it the right key; an array index is only used for the
  padding cells, which never move.
- The whole graph is one accessibility element with a summary label. A screen reader
  announcing 112 individual squares would be useless.

**3.8: Drop it into the dashboard**

1. In `DashboardScreen.tsx` (task 3.3), replace the
   `{/* Part 2 (task 3.8) replaces this comment... */}` comment with:
   ```tsx
   <ContributionGraph days={d.contributionDays} />
   ```
   placed above the connection indicator, per "at the very top of the dashboard"
   above, and add `import { ContributionGraph } from './ContributionGraph';`.
2. Run it: the graph should render a scattering of lit/unlit cells against
   `FakeWorkoutHistoryProvider` immediately, before any real workout has ever been
   recorded.

### Review checkpoint

Before wiring Phase 06's real data source in later: agent reviews the
`WorkoutHistoryProvider` shape (is it something Phase 06 can actually implement
without redesigning it?), the fake's realism, and the hook's two effects (cleanup on
both, the cancelled flag on the async one).

---

## Acceptance

- [ ] Smooth updates for 10 minutes with no UI stall (Part 1)
- [ ] `[HUMAN]` Displayed values match the console at three different speeds
- [ ] No field shows a placeholder for data the device never sends
- [ ] No listener survives leaving the dashboard (the cleanup test above)
- [ ] Contribution graph renders against `FakeWorkoutHistoryProvider`, lit days
      visibly distinct from unlit ones
- [ ] `WorkoutHistoryProvider` lives in `core/`, imports nothing from React Native,
      and is wired in `services.tsx` the same way `treadmill` is
