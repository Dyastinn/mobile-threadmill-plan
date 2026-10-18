# Phase 11 — Split Screen

> One of the two reasons this app exists. Treat it as a feature, not a layout chore.

**Treadmill needed:** to check it · **Size:** M · **Waits for:** Phase 10

> Mostly layout, so the agent writes the screen code and the small config file. Two of
> the three tasks are checking that things already work.

---

## Goal

Usable next to YouTube.

### The idea

**The worry:** when a window changes size, Android normally destroys the screen and
builds it again. Mid-workout, that could drop the Bluetooth connection.

**Why it won't, for two reasons:**

1. Expo's generated Android settings already tell Android "I'll handle size changes
   myself" (the `configChanges` list: screen size, layout, orientation). Checked in
   Expo 58. So Android doesn't rebuild the screen at all.
2. Even if it did, the connection wouldn't notice. Your code's Bluetooth, engine and
   recorder live in the services file, which belongs to the app, not to any screen. A
   rebuilt screen just calls `useServices()` and gets the same objects back, still
   connected.

So a resize is just a layout change: `useWindowDimensions()` reports a new height, and
the dashboard switches to a compact layout.

**The name for this:** the *single responsibility* idea, paying off. Screens change for
display reasons; services change for connection reasons. They were kept apart since
Phase 00, and that's why split screen needs no connection code now. It only works
because the connection was never kept inside a component.

## Features

- Works at 33%, 50%, 75% and full size
- A compact dashboard below a certain height
- `android:resizeableActivity="true"` stated explicitly; the size-change settings checked

## Rules

- A resize must **not** reconnect Bluetooth. It shouldn't; check it.
- **The speed buttons must be reachable at 33% height.** That's the real requirement.
- No sideways scrolling at any size.

---

## What you'll learn

- **`useWindowDimensions()`**: gives the app window's current size, and redraws when it
  changes. That's exactly what changes in split screen.
- **Layout from size**: one screen that looks different below a threshold, decided by
  one `compact` flag.
- **Flexbox for "this part must never be pushed off screen"**: a scrolling area that
  takes the leftover space, above a fixed area that doesn't scroll.
- **Config plugins**: how to change a setting in Expo's generated Android files without
  editing them.

## Read first

- **`useWindowDimensions`**: https://reactnative.dev/docs/usewindowdimensions
- **Config plugins**: https://docs.expo.dev/config-plugins/introduction/ ("Creating a
  plugin"; task 11.1 uses `withAndroidManifest`)
- **Multi-window on Android**: https://developer.android.com/develop/adaptive-apps/guides/support-multi-window-mode
- **Why a plugin, not an edit**: https://docs.expo.dev/workflow/continuous-native-generation/
- [`04-Monochrome-Theme.md`](../../docs/learning/04-Monochrome-Theme.md): the theme's
  buttons are already at least 48 dp tall, which the 33% rule depends on

---

## Tasks

### 11.1 — `resizeableActivity`, and check the size settings

**Small: mostly checking. The one change is a 10-line plugin.**

Two separate Android settings:

1. **`resizeableActivity`**: is the app *allowed* in split screen? It defaults to yes,
   but we'll say so explicitly instead of relying on a default.
2. **`configChanges`**: which changes the app handles itself instead of being rebuilt.
   A split-screen resize is a screen-size change.

Check the second one is already there:

```powershell
cd src
npx expo prebuild --platform android
Select-String -Path android/app/src/main/AndroidManifest.xml -Pattern "configChanges"
```

In Expo 58 it reads
`keyboard|keyboardHidden|orientation|screenSize|screenLayout|uiMode|smallestScreenSize|assetsPaths`.
Android's guide asks for `screenSize`, `smallestScreenSize`, `screenLayout` and
`orientation`. All there. Nothing to change.

The first one is missing, so add it with a small plugin. It's configuration, so here it
is in full. `src/plugins/withResizeableActivity.js`:

```js
const { AndroidConfig, withAndroidManifest } = require('expo/config-plugins');

/** Adds android:resizeableActivity="true" to the main screen. */
module.exports = function withResizeableActivity(config) {
  return withAndroidManifest(config, (cfg) => {
    const main = AndroidConfig.Manifest.getMainActivityOrThrow(cfg.modResults);
    main.$['android:resizeableActivity'] = 'true';
    return cfg;
  });
};
```

Add `"./plugins/withResizeableActivity"` to `app.json`'s `plugins`. It runs every time
Expo regenerates the Android files, so the setting can't get lost. If it can't find the
main screen, the build fails loudly instead of silently skipping it.

1. Add the plugin, run `npx expo prebuild --platform android --clean`, and check the
   attribute is in the generated manifest.
2. Build and install. On the phone: put the app in split screen from Recents. The option
   being there at all is part of the test.
3. On the phone, connected and mid-workout: drag the split-screen divider. To check the
   screen isn't rebuilt, add a temporary
   `useEffect(() => log('root layout mounted'), [])` to `src/app/_layout.tsx`; it should
   appear once in `adb logcat -s ReactNativeJS`, not once per resize. (Even if it
   appeared twice, the connection should stay `ready`; that's task 11.3.)

### 11.2 — Compact dashboard below a certain height

**Given in full.**

The dashboard works out one flag, `compact`, from the window height (or from Phase 08's
"Compact" layout setting). Compact hides the calendar and the extra numbers and shrinks
the speed, so **the speed buttons stay reachable at 33% height**. The buttons sit
outside the scrolling area, pinned to the bottom, so nothing above can push them off
screen.

**Measure the breakpoint; don't guess it.** On the phone, in split screen at about 33%
of the height, read the height `useWindowDimensions()` reports (show it on the
Diagnostics screen, or log it) and use it instead of the `420` below. `420` is a rough
guess, not a measurement.

`src/features/dashboard/useLayoutMode.ts`:

```ts
import { useWindowDimensions } from 'react-native';

import { useSettings } from '@/features/settings/useSettings';

/** Replace with the height measured at ~33% split screen on the phone. */
export const COMPACT_BELOW_HEIGHT_DP = 420;

export function useLayoutMode(): { compact: boolean; height: number } {
  const { height } = useWindowDimensions();
  const { snapshot } = useSettings();
  const compact = snapshot.dashboardLayout === 'compact' || height < COMPACT_BELOW_HEIGHT_DP;
  return { compact, height };
}
```

`src/features/dashboard/DashboardScreen.tsx`, rearranged (merge with what Phases 03–10
added):

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
      {/* Everything that can be hidden scrolls, in the space left over. */}
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

        {d.showsSpeed && <MetricTile value={formatOneDecimal(d.speedKph)} unit="km/h" size={compact ? 'compact' : 'large'} />}

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

      {/* Never scrolls, never hidden. This is why compact mode exists. */}
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

`TreadmillControls` (Phase 05) gets a `compact` prop. In compact mode it hides the
target tile, the "Presets" row and the safety caption, and puts −, + and Stop on one
row:

```tsx
export function TreadmillControls({ compact = false }: { compact?: boolean }) {
  // ...same hook calls and confirmStop as before...

  if (compact) {
    return (
      <View style={{ flexDirection: 'row', gap: spacing.sm, opacity: disabled ? 0.5 : 1 }}>
        <Button label={'−'} onPress={() => controller.decrease()} disabled={disabled} accessibilityLabel="Decrease speed" style={{ flex: 1 }} />
        <Button label="+" onPress={() => controller.increase()} disabled={disabled} accessibilityLabel="Increase speed" style={{ flex: 1 }} />
        <SecondaryButton label="Stop" onPress={confirmStop} disabled={disabled} style={{ flex: 1 }} />
      </View>
    );
  }

  // ...the full layout from Phase 05...
}
```

Stop still asks for confirmation in compact mode, and the dialog still mentions the
safety key.

`MetricTile` gets a `size?: 'large' | 'compact'` prop in the theme (compact uses the
`subheadline` size instead of `metric`).

1. On the phone, measure the real height at ~33% and replace `420`.
2. Merge this into your `DashboardScreen.tsx`, keeping the names your hooks actually
   use.
3. Quick check before using the phone: set Phase 08's layout to "Compact" and check the
   screen switches. (The emulator can't do real split screen.)
4. On the phone: split screen at 100%, 75%, 50% and 33%, portrait and landscape. At
   every size: no sideways scrolling, and −/+/Stop fully visible and tappable.

### 11.3 — Check: no reconnect on resize

**Only checking. If something's wrong, the fix is in 11.1 or the services, not here.**

1. On the phone: start a workout, connected and recording.
2. Drag the divider through all four sizes, pausing a few seconds at each.
3. The connection state never leaves `ready` (the dot never empties). Afterwards, the
   saved workout has no gap markers lining up with a resize.
4. If a drop or gap *does* line up with a resize, find the real cause: a missing size
   setting (11.1), a connection kept in a component's state (move it to the services
   file), or the app being closed (Phase 07 and the HyperOS settings). Don't hide it
   with screen tricks.

---

## Tests

| Test | | |
|------|---|---|
| All 4 sizes, portrait and landscape | Buttons reachable and tappable | treadmill |
| Resize mid-workout | No disconnect, no gap | treadmill |
| YouTube / Messenger / Chrome in the other half | Dashboard keeps updating | treadmill |
| The screen isn't rebuilt on resize | "root layout mounted" logged once | treadmill |
| `resizeableActivity` is set | In the generated manifest; split screen offered in Recents | treadmill |

## Done when

- [ ] Fully usable at 33%, with no disconnect on resize
- [ ] `android:resizeableActivity="true"` is in the generated manifest
- [ ] The compact breakpoint is a measured number, not a guess
- [ ] No sideways scrolling at any of the 4 sizes, either way up
