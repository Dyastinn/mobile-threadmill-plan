# Phase 11 — Split Screen

> One of the two reasons this app exists at all. Treat it as a feature, not a layout
> chore.

**Hardware:** required to verify · **Size:** M · **Blocked by:** Phase 10

> See `../README.md` for the collaboration model. This phase is almost entirely
> UI/layout work, so the agent writes the screen code and the config plugin in full.
> But two of its three tasks are verification against things that already exist, not
> new logic, and are called out as such below.

---

## Goal

Usable alongside YouTube.

### Understanding what you're building (read this before the tasks)

**The problem.** By default, a "big" Android configuration event like a window
resize tears the whole activity down and rebuilds it from scratch, the same as
force-closing and reopening the screen. Left unchecked, that default would apply
directly to the app's activity during exactly the moment this phase exists to
support: treadmill still running, screen still on, user mid-workout.

**Why split screen doesn't force a Bluetooth reconnect.** Two things prevent it, and
this phase adds no new machinery to get there; task 11.3 exists to verify the
machinery already works, not to build it.

First, the activity Expo generates already declares the `configChanges` a
split-screen resize triggers (`screenSize`, `smallestScreenSize`, `screenLayout`,
`orientation`; checked in SDK 58's template). That's what stops Android's own
tear-down-and-rebuild reflex on the activity at all.

Second, and the one worth sitting with: even if the activity *were* rebuilt, the
connection wouldn't go with it. In React Native, your JavaScript runtime belongs to
the app's **process**, not to the activity. The BLE client, the workout engine and the
recorder live in `services.tsx` at module scope (Phase 00's composition root), and
Phase 07's foreground service keeps the process alive. A rebuilt activity just
re-mounts the React tree; the screens call `useServices()` and get the very same
objects back, still connected. A window resize, traced all the way through, is nothing
more than a layout event: `useWindowDimensions()` returns a new height, the dashboard
switches to its compact layout, and there's no path to the connection at all.

**The pattern, named plainly.** This is the Single Responsibility Principle showing up
as a concrete, already-banked payoff rather than a rule to recite: screens change for
UI/display reasons (window size, orientation, theme); the services change for
connection-lifecycle reasons (BLE state, buffered-sample flushes). Because those are
different reasons to change, Phase 00 already put them in different places, not in
anticipation of split screen, but because "a screen's lifetime" and "the connection's
lifetime" were already worth separating on their own terms. The cost was real at the
time: a composition root to set up and a rule to keep ("nothing that must outlive a
screen lives in a component"). The payoff shows up here, for free, eleven phases
later. Worth naming honestly: this payoff isn't automatic just from "using React
Native." It required deliberately keeping the BLE connection out of component state
in the first place. A project that let a screen hold the connection in a
`useRef`/`useState`, a perfectly reasonable choice for an app with no
background-survival or split-screen requirement, would be paying for this phase's
constraint the hard way, right now, instead of collecting it as a free line in a
changelog.

## Features

- Responsive layout at 33% / 50% / 75% / full
- Compact dashboard variant below a height threshold
- `android:resizeableActivity="true"` declared explicitly, `configChanges` verified

## Implementation requirements

- Handle configuration changes **without recreating the BLE connection**. The
  services own it, so this should already hold. Verify that it does rather than
  assuming it.
- **Speed controls must remain reachable at 33% height.** This is the real constraint;
  everything else follows from it.
- No horizontal scrolling at any width.

---

## Learning goals

- **`useWindowDimensions()`**: the React Native hook that returns the app's current
  **window** size and re-renders when it changes. That's exactly the thing that
  changes when Android resizes the app for split screen, which is why it's the right
  tool here, not a screen-local `onLayout`.
- **Layout as a function of size**: one component that renders differently below a
  threshold, rather than two screens. The decision is one boolean (`compact`) computed
  from the window and the Phase 08 "dashboard layout" setting.
- **Flexbox for "this part must never be pushed off screen"**: a scrolling region
  that takes the leftover space (`flex: 1`) above a fixed region that doesn't.
- **Config plugins**: how a native setting gets into a generated `AndroidManifest.xml`
  under Continuous Native Generation, without ever editing `src/android/`.
- Android's multi-window `configChanges` model, and why React Native makes "the
  activity got recreated" much less dangerous than it is in a native app.

## Reference docs

- **`useWindowDimensions`**: https://reactnative.dev/docs/usewindowdimensions
- **Config plugins**: https://docs.expo.dev/config-plugins/introduction/. Read
  "Creating a plugin" before task 11.1; `withAndroidManifest` is the one mod it uses.
- **Support multi-window mode (Android)**: https://developer.android.com/develop/adaptive-apps/guides/support-multi-window-mode.
  `resizeableActivity`, the `configChanges` values Android recommends declaring.
- **Continuous Native Generation**: https://docs.expo.dev/workflow/continuous-native-generation/,
  for why task 11.1 is a plugin and not an edit.
- `docs/learning/04-Monochrome-Theme.md`: the base `Button` already enforces a 48 dp
  minimum touch target, which this phase's 33%-height constraint depends on; nothing
  new to add there, just worth knowing it's already covered.

---

## Walkthrough

### Task 11.1 — `resizeableActivity` via a config plugin, and verify `configChanges`

**Small, and mostly verification. The actual change is one attribute, delivered by a
fifteen-line plugin.**

Android has two independent knobs here:

1. **`resizeableActivity`**: whether the activity is *allowed* to run in split
   screen / multi-window at all. Defaults to `true` on API 24+ if unset, but this
   project should say so explicitly rather than rely on a version-dependent default.
2. **`configChanges`**: which configuration changes the activity handles itself
   instead of being destroyed and recreated for. A split-screen resize is a
   `screenSize`/`smallestScreenSize`/`screenLayout` change.

Expo's generated manifest already carries a `configChanges` list. Confirm it rather
than trusting this sentence:

```powershell
cd src
npx expo prebuild --platform android
Select-String -Path android/app/src/main/AndroidManifest.xml -Pattern "configChanges"
```

In SDK 58 that line reads
`keyboard|keyboardHidden|orientation|screenSize|screenLayout|uiMode|smallestScreenSize|assetsPaths`.
Compare it against what Android's multi-window guide recommends,
`screenSize | smallestScreenSize | screenLayout | orientation`, and every one is
already present. **This is the "verify it already holds" case the Implementation
requirements warned about.**

What's genuinely missing is `resizeableActivity` itself. Add it with a local config
plugin, written in full here because it's configuration, not app logic:

`src/plugins/withResizeableActivity.js`:

```js
const { AndroidConfig, withAndroidManifest } = require('expo/config-plugins');

/** Declares android:resizeableActivity="true" on the app's main activity. */
module.exports = function withResizeableActivity(config) {
  return withAndroidManifest(config, (cfg) => {
    const main = AndroidConfig.Manifest.getMainActivityOrThrow(cfg.modResults);
    main.$['android:resizeableActivity'] = 'true';
    return cfg;
  });
};
```

and register it in `app.json`'s `plugins` array: `"./plugins/withResizeableActivity"`.
The plugin runs every time `prebuild` regenerates `src/android/`, so the setting can't
be lost the way a hand edit would be. `getMainActivityOrThrow` fails the build loudly,
rather than silently doing nothing, if the manifest ever stops having a main activity
where it expects one.

Concrete steps:

1. Add the plugin, run `npx expo prebuild --platform android --clean`, and confirm
   the attribute is in the generated manifest.
2. Build and install. `[HUMAN]`: drag the app into split screen from the Recents
   screen. The split-screen option being available at all is itself part of the
   pass/fail signal.
3. `[HUMAN]`: with the app connected to the treadmill and mid-workout, drag the
   split-screen divider. Confirm the activity is **not** recreated: add a temporary
   `useEffect(() => log('root layout mounted'), [])` in `src/app/_layout.tsx` and check
   `adb logcat -s ReactNativeJS` shows it once, not once per resize. And per the
   reasoning above, even if it did appear twice, the connection indicator should never
   leave `ready`; that's task 11.3.

---

### Task 11.2 — Responsive layout: compact dashboard below a height threshold

**UI: full code, written for you.**

`useWindowDimensions()` reacts to the app's **window** size, which is exactly what
shrinks when the split-screen divider moves. The plan: the dashboard computes one
boolean, `compact`, from the window height (and Phase 08's "dashboard layout" setting,
so "Compact" can be forced), and renders accordingly. Compact hides the contribution
graph and the secondary metrics, and shrinks the speed readout, so the **speed
controls stay reachable at 33% height**, the one hard constraint this phase exists to
satisfy. The controls live outside the scrolling area, pinned to the bottom, so no
amount of content above can push them off screen.

Pick the breakpoint by *measuring*, not guessing. `[HUMAN]`: with the app in split
screen at roughly 33% of the phone's height, read the height the hook reports (the
Diagnostics screen can show `useWindowDimensions().height`, or log it) and use that
number in place of the `420` placeholder below. `420` is an unverified arithmetic
estimate, not a measured value; treat it as something to replace, not trust.

`src/features/dashboard/useLayoutMode.ts` (UI support, written for you):

```ts
import { useWindowDimensions } from 'react-native';

import { useSettings } from '@/features/settings/useSettings';

/** Replace with the height measured at ~33% split screen on the Poco X6 Pro. */
export const COMPACT_BELOW_HEIGHT_DP = 420;

export function useLayoutMode(): { compact: boolean; height: number } {
  const { height } = useWindowDimensions();
  const { snapshot } = useSettings();
  const compact = snapshot.dashboardLayout === 'compact' || height < COMPACT_BELOW_HEIGHT_DP;
  return { compact, height };
}
```

`src/features/dashboard/DashboardScreen.tsx`, restructured (merge with what Phases
03–10 added; the section names below match those phases):

```tsx
import { Link } from 'expo-router';
import { ScrollView, View } from 'react-native';

import { ContributionGraph } from './ContributionGraph';
import { useDashboard } from './useDashboard';
import { useLayoutMode } from './useLayoutMode';
import { TreadmillControls } from '@/features/treadmill/TreadmillControls';
import { WorkoutButton } from '@/features/workout/WorkoutButton';
import { useWorkoutState } from '@/features/workout/useWorkoutState';
import { formatClock, formatKm, formatOneDecimal } from '@/features/shared/format';
import { ConnectionDot } from '@/features/shared/theme/ConnectionDot';
import { MetricTile } from '@/features/shared/theme/MetricTile';
import { Screen } from '@/features/shared/theme/Screen';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';
import { useTheme } from '@/features/shared/theme/useTheme';

export function DashboardScreen() {
  const d = useDashboard();
  const workoutState = useWorkoutState();
  const { compact } = useLayoutMode();
  const { colors } = useTheme();
  const gap = compact ? spacing.sm : spacing.xl;

  return (
    <Screen>
      {/* Everything that may be sacrificed scrolls, in the space left over. */}
      <ScrollView style={{ flex: 1 }} contentContainerStyle={{ padding: spacing.lg, gap }}>
        {!compact && <ContributionGraph days={d.contributionDays} />}

        <View
          style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.sm }}
          accessible
          accessibilityLabel={`Treadmill ${d.connectionState}, workout ${workoutState}`}
        >
          <ConnectionDot connected={d.connectionState === 'ready'} />
          <Text variant="caption">{d.connectionState} · {workoutState}</Text>
        </View>

        {d.showsSpeed && (
          <MetricTile value={formatOneDecimal(d.speedKph)} unit="km/h" size={compact ? 'compact' : 'large'} />
        )}

        {!compact && (
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md }}>
            {d.showsDistance && <MetricTile value={formatKm(d.distanceMeters)} unit="km" style={{ flexBasis: '47%', flexGrow: 1 }} />}
            {d.showsCalories && <MetricTile value={String(d.calories ?? '')} unit="kcal" style={{ flexBasis: '47%', flexGrow: 1 }} />}
            {d.showsElapsedTime && <MetricTile value={formatClock(d.elapsedSeconds)} unit="elapsed" style={{ flexBasis: '47%', flexGrow: 1 }} />}
            {d.showsHeartRate && <MetricTile value={String(d.heartRate ?? '')} unit="bpm" style={{ flexBasis: '47%', flexGrow: 1 }} />}
          </View>
        )}

        {!compact && (
          <View style={{ flexDirection: 'row', justifyContent: 'center', flexWrap: 'wrap', gap: spacing.lg }}>
            <Link href="/history"><Text variant="caption">History</Text></Link>
            <Link href="/statistics"><Text variant="caption">Statistics</Text></Link>
            <Link href="/backup"><Text variant="caption">Backup</Text></Link>
            <Link href="/settings"><Text variant="caption">Settings</Text></Link>
            <Link href="/diagnostics"><Text variant="caption">Diagnostics</Text></Link>
          </View>
        )}
      </ScrollView>

      {/* Never scrolls, never hidden. This region is the entire reason compact exists:
          everything above it can be sacrificed, this can't. */}
      <View
        style={{
          paddingHorizontal: spacing.lg,
          paddingVertical: compact ? spacing.sm : spacing.md,
          gap: spacing.sm,
          borderTopWidth: 1,
          borderTopColor: colors.border,
        }}
      >
        <WorkoutButton />
        <TreadmillControls compact={compact} />
      </View>
    </Screen>
  );
}
```

`TreadmillControls` (Phase 05) gains one prop, `compact`, which hides the target-speed
tile, the "Presets" caption and the safety caption, and puts −, +, and Stop on one row.
In full, the parts that change:

```tsx
export function TreadmillControls({ compact = false }: { compact?: boolean }) {
  // ...unchanged hook calls, confirmStop...

  if (compact) {
    return (
      <View style={{ flexDirection: 'row', gap: spacing.sm, opacity: disabled ? 0.5 : 1 }}>
        <Button label={'−'} onPress={() => controller.decrease()} disabled={disabled} accessibilityLabel="Decrease speed" style={{ flex: 1 }} />
        <Button label="+" onPress={() => controller.increase()} disabled={disabled} accessibilityLabel="Increase speed" style={{ flex: 1 }} />
        <SecondaryButton label="Stop" onPress={confirmStop} disabled={disabled} style={{ flex: 1 }} />
      </View>
    );
  }

  // ...the full layout from Phase 05, unchanged
}
```

The Stop confirmation and its safety-key message are unchanged in compact mode: the
dialog is where the warning lives, so shrinking the screen doesn't remove it.

`MetricTile` gains a `size?: 'large' | 'compact'` prop in the theme (`compact` uses the
`subheadline` text size instead of `metric`); see `04-Monochrome-Theme.md`.

Concrete steps:

1. `[HUMAN]`: measure the real window height at ~33% split on the phone (see above)
   and replace the `420` placeholder with that number.
2. Merge the layout above into your actual `DashboardScreen.tsx`. Keep whatever names
   your hooks really return; adjust the TSX to fit them rather than renaming your hooks
   to match this sample.
3. Quick check before the phone: force `compact` with the Phase 08 "Compact" layout
   setting and confirm the layout switches. (The emulator can't do real split screen;
   see `docs/learning/01-Device-And-Emulator-Setup.md`.)
4. `[HUMAN]`: on the phone, drag into split screen from 100% down through 75% / 50% /
   33%, both portrait and landscape. At every size, confirm: no horizontal scrolling,
   and the −/+/Stop row is fully visible and tappable. The 48 dp minimum touch target
   is already built into the theme's `Button`, so this should hold automatically.
   Confirm rather than assume.

---

### Task 11.3 — Verify: no BLE reconnect on resize

**Verification, not new code. A fix (if one turns out to be needed) belongs in task
11.1 or in the services, not here.**

Concrete steps:

1. `[HUMAN]`: start a workout, confirm connected and recording.
2. `[HUMAN]`: resize the split-screen divider through all four sizes, pausing a few
   seconds at each.
3. Check that the connection state never leaves `ready` during any resize. Watch the
   connection dot (it should never flicker to an outline), or grep the log for state
   changes. Then check the recorded workout afterwards for gap markers
   (`WorkoutSample.Flags` bit 0) that line up with a resize. None should be present.
4. If a disconnect or gap **does** show up correlated with a resize, the fix is not in
   this task's layout code. Find out which layer actually dropped it: a missing
   `configChanges` flag (task 11.1), a connection accidentally held in component state
   (move it to `services.tsx`), or the process being killed (Phase 07's service and
   the HyperOS checklist). Don't paper over a real reconnect with UI state.

---

## Tests

| Test | | |
|------|---|---|
| Each of 4 sizes, portrait and landscape | Controls reachable and tappable | `[HUMAN]` |
| Resize mid-workout | No disconnect, no data gap | `[HUMAN]` |
| YouTube / Messenger / Chrome in the other pane | Dashboard keeps updating | `[HUMAN]` |
| Root layout survives a resize | "root layout mounted" logged once, not per resize | `[HUMAN]` |
| `resizeableActivity` present | In the generated manifest; split-screen option available from Recents | `[HUMAN]` |

## Acceptance

- [ ] Fully usable at 33% with no disconnect on resize
- [ ] `android:resizeableActivity="true"` confirmed in the generated manifest, via the
      config plugin
- [ ] The compact breakpoint is a measured number from the real device, not an
      unverified guess
- [ ] No horizontal scrolling at any of the 4 sizes, either orientation
