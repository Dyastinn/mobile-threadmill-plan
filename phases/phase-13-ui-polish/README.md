# Phase 13 — Polish

**Treadmill needed:** no · **Size:** M · **Waits for:** Phase 12

---

## Goal

A **polish pass over every screen** from Phases 03–11. No new screens, and no rewrites.
Instead, a few **shared pieces** (an empty-state message, a loading cover, a body text
size, one vibration, accessibility fixes) that get added to each existing screen at the
review.

**Almost all of this phase is UI, which the agent writes.** You paste it in and connect
it to your hooks. Everything uses the existing theme
([`04-Monochrome-Theme.md`](../../docs/learning/04-Monochrome-Theme.md)), or says
clearly what to add to it. Two small pieces are logic, and yours: the vibration hook-up
and the developer setting.

### The idea

**One shared component instead of several copies.** Several screens each need "nothing
here yet" (no workouts, not connected) and "working on it" (exporting, loading). Writing
those separately on each screen gives slightly different versions, and a fix to one
doesn't fix the others. So now they become two shared components, `EmptyState` and
`LoadingOverlay`.

**Why now, and not back in Phase 03?** Because only now are there several real screens
to learn from. Building it earlier would have meant guessing which options it needs (a
retry button? a screen-reader label?). Now the repetition is proven and the shape is
clear.

**The name for this:** *DRY*, "don't repeat yourself", by pulling out a shared
component. Only worth it once the same thing has actually shown up several times.

## What you'll learn

- **Components with props**: a reusable piece is just a function that takes options.
- **Covering a screen**: `StyleSheet.absoluteFill` makes the loading cover fill its
  parent exactly, without a popup.
- **Accessibility props**: `accessibilityLabel` (what a screen reader says),
  `accessibilityHint` (what happens if you activate it), `accessibilityRole` (button,
  header...), `accessibilityState` (disabled, selected), and when *not* to add a label.
- **`expo-haptics`**: vibration, and why only one moment in this app gets it.
- **48 dp touch targets**: Android's minimum size for anything tappable, and `hitSlop`
  for small things that should still be easy to hit.
- **Font scaling**: text grows with the phone's font size setting, and what that does
  to a big number at 200%.

## Read first

| Topic | Link |
|---|---|
| App icons (incl. Android adaptive and monochrome) | https://docs.expo.dev/develop/user-interface/app-icons/ |
| `expo-splash-screen` (light and dark) | https://docs.expo.dev/versions/latest/sdk/splash-screen/ |
| React Native accessibility | https://reactnative.dev/docs/accessibility |
| `expo-haptics` | https://docs.expo.dev/versions/latest/sdk/haptics/ |
| `expo-keep-awake` | https://docs.expo.dev/versions/latest/sdk/keep-awake/ |
| Android: 48 dp touch targets | https://developer.android.com/guide/topics/ui/accessibility/apps |
| Android: testing with TalkBack | https://developer.android.com/guide/topics/ui/accessibility/testing |
| `StyleSheet` (`absoluteFill`) | https://reactnative.dev/docs/stylesheet |

---

## Shared pieces

These go in `src/features/shared/theme/`, next to Phase 00's components. Paste them in
as they are, then add them to each screen at the review.

### 1. Text sizes

The theme's `Text` already has `caption` (12), `metricLabel` (13), `default` (15),
`subheadline` (22), `headline` (28) and `metric` (40). Missing: a size for normal
paragraphs (error messages, empty-state messages). Add `body` to `typography` in
`tokens.ts`:

```ts
body: { fontSize: 17, lineHeight: 24 },
```

and to the `variant` list in `Text.tsx`. Smallest to largest: `caption` (12) →
`metricLabel` (13) → `default` (15) → `body` (17) → `subheadline` (22) → `headline`
(28) → `metric` (40).

**Spacing** already comes from `spacing.xs` to `spacing.xxl` (4, 8, 12, 16, 24, 32). Keep
using those. Any other number in a `style` should have a reason. (The one existing
exception: `14` padding inside settings rows.)

### 2. "Nothing here yet" — `EmptyState`

For screens that can have nothing to show yet: a symbol, a title, a message, and an
optional button.

`src/features/shared/theme/EmptyState.tsx`:

```tsx
import { View } from 'react-native';

import { SecondaryButton } from './SecondaryButton';
import { Text } from './Text';
import { spacing } from './tokens';
import { useTheme } from './useTheme';

interface Props {
  /** A plain text symbol (there's no icon set yet). */
  readonly glyph?: string;
  readonly glyphLabel?: string;
  readonly title: string;
  readonly message: string;
  readonly actionLabel?: string;
  readonly onAction?: () => void;
}

export function EmptyState({ glyph = '○', glyphLabel, title, message, actionLabel, onAction }: Props) {
  const { colors } = useTheme();
  return (
    <View style={{ alignItems: 'center', gap: spacing.md, padding: spacing.xxl, maxWidth: 320, alignSelf: 'center' }}>
      <Text
        style={{ fontSize: 40, color: colors.textSecondary }}
        accessibilityLabel={glyphLabel}
        importantForAccessibility={glyphLabel ? 'yes' : 'no-hide-descendants'}
      >
        {glyph}
      </Text>
      <Text variant="subheadline" accessibilityRole="header" style={{ textAlign: 'center' }}>
        {title}
      </Text>
      <Text variant="body" style={{ textAlign: 'center', color: colors.textSecondary }}>
        {message}
      </Text>
      {actionLabel !== undefined && onAction !== undefined && (
        <SecondaryButton label={actionLabel} onPress={onAction} style={{ marginTop: spacing.sm }} />
      )}
    </View>
  );
}
```

The symbol is a plain `○`, because there's no icon set yet. Real icons
(`@expo/vector-icons` comes with Expo) are a possible later step; decide at a review
rather than adding a library for one symbol. A decorative symbol is hidden from screen
readers; pass `glyphLabel` only if it means something.

**Example** (the history list with no workouts), using `FlatList`'s empty slot:

```tsx
<FlatList
  // ...data and renderItem as before
  ListEmptyComponent={
    <EmptyState title="No workouts yet" message="Connect to the treadmill and start a workout to see it here." />
  }
/>
```

### 3. "Working on it" — `LoadingOverlay`

A dimmed cover with a spinner, on top of the screen while something runs.

`src/features/shared/theme/LoadingOverlay.tsx`:

```tsx
import { ActivityIndicator, StyleSheet, View } from 'react-native';

import { Text } from './Text';
import { spacing } from './tokens';
import { useTheme } from './useTheme';

export function LoadingOverlay({ visible, message }: { visible: boolean; message?: string }) {
  const { colors } = useTheme();
  if (!visible) return null;
  return (
    <View
      style={[StyleSheet.absoluteFill, { backgroundColor: colors.background, opacity: 0.9, alignItems: 'center', justifyContent: 'center', gap: spacing.sm }]}
      accessibilityViewIsModal
      accessibilityLiveRegion="polite"
    >
      <ActivityIndicator size="large" color={colors.textPrimary} accessibilityLabel="Loading" />
      {message !== undefined && message !== '' && <Text variant="caption">{message}</Text>}
    </View>
  );
}
```

**How to use it:** put it *last* inside the screen, so it's drawn on top.
`StyleSheet.absoluteFill` stretches it to the screen's edges, and while it's showing, taps
can't reach what's underneath.

```tsx
<Screen>
  <ScrollView>{/* the normal screen */}</ScrollView>
  <LoadingOverlay visible={b.busy} message={b.progressStage ?? undefined} />
</Screen>
```

`accessibilityLiveRegion="polite"` makes TalkBack read the message when it appears,
without interrupting.

### 4. A vibration when a speed change is confirmed (yours)

`expo-haptics` (`npx expo install expo-haptics`) gives `Haptics.selectionAsync()`, a
light tick.

It belongs to the moment the treadmill **confirms** a new speed, not every tap. That
moment is inside `SpeedController` (Phase 05), in `core/`, which can't import
`expo-haptics`. So pass it in, like Phase 06's GUID maker:

1. Give `SpeedController` an optional `onSpeedConfirmed?: () => void`, and call it when
   `setSpeed()` succeeds. Never on failure: a vibration after a failed command would feel
   like a confirmation.
2. In the services file, pass `() => void Haptics.selectionAsync()`.
3. Test: with a fake that accepts the command, the callback runs once; with one that
   rejects it, never.

Nothing else in the app vibrates. Don't add it to every button.

### 5. Accessibility examples

**A button whose text doesn't say enough** (Phase 05's − and + already do this):

```tsx
<Button label={'−'} onPress={...} accessibilityLabel="Decrease speed" />
```

**A button that needs extra context** (Stop):

```tsx
<Button
  label="Stop"
  onPress={confirmStop}
  accessibilityHint="Asks first, then sends a Bluetooth stop command. The physical safety key is the emergency stop."
/>
```

**Several things read as one** (the dot plus the word):

```tsx
<View accessible accessibilityLabel={`Treadmill ${state}`}>
  <ConnectionDot connected={state === 'ready'} />
  <Text variant="caption">{state}</Text>
</View>
```

Two rules:

- **Don't add `accessibilityLabel` to text that already says the right thing.** The label
  *replaces* the text for screen readers; it doesn't add to it.
- **Text inputs keep a visible placeholder or label.** Use `accessibilityHint` for extra
  detail; don't hide what the field is for.

### 6. Touch targets: check, and fix the small links

The theme's `Button`, `SecondaryButton` and `SegmentedControl` are already at least
**48 dp** tall (Android's guidance). The small text links on the dashboard ("History",
"Settings"...) aren't: they're maybe 16 dp tall. Give each one
`hitSlop={{ top: 16, bottom: 16, left: 8, right: 8 }}` (via the `Link`'s `asChild` and a
`Pressable`), or some padding, so the tappable area reaches 48 dp without changing how it
looks.

---

## Tasks

### App icon and splash screen

1. Make the images: a 1024×1024 `icon.png`, a foreground layer for Android's adaptive
   icon (`adaptive-icon.png`, with the picture inside the central safe area), and a
   one-color `monochrome-icon.png` for Android's themed icons. The app icons doc explains
   why there are several files.
2. In `app.json`:
   ```json
   "icon": "./assets/images/icon.png",
   "android": {
     "adaptiveIcon": {
       "foregroundImage": "./assets/images/adaptive-icon.png",
       "monochromeImage": "./assets/images/monochrome-icon.png",
       "backgroundColor": "#F2F2F3"
     }
   }
   ```
   and the splash screen through its plugin, which allows a separate dark background:
   ```json
   ["expo-splash-screen", {
     "image": "./assets/images/splash-icon.png",
     "imageWidth": 160,
     "backgroundColor": "#F2F2F3",
     "dark": { "backgroundColor": "#212123" }
   }]
   ```
   Those are the theme's lightest and darkest grays, so the splash matches the app.
3. `npx expo prebuild --clean` and rebuild: icons and splash are native files.
4. Check the icon (also with themed icons on) and the splash in light and dark mode, on
   the phone. You can't check this from the code.

### Text sizes and spacing

Covered above. Use `body` for any paragraph text that currently uses the default size:
setting descriptions, empty-state messages, error explanations.

### Loading and empty states on **every** screen

For each screen from Phases 03–11:
1. What does "nothing to show yet" mean on this screen (no workouts, not connected,
   empty history)? Does it have a busy flag?
2. Add `EmptyState` (as `ListEmptyComponent`, or shown conditionally) and
   `LoadingOverlay` (last thing in the screen), as in the examples.
3. At the review: does the message fit *this* screen? (A fresh install has no workouts;
   a disconnected dashboard isn't "empty", it's "not connected yet".)

### Error messages that say what to do

**"Couldn't reach the treadmill. Check it's switched on and try again"** beats
"GATT error 133" (the code still goes in the log).
1. List every error the user can see in Phases 02, 05, 07 and 09 (connection failures,
   rejected commands, background service and permission problems, backup errors).
2. For each, write one plain sentence saying what the user can *do next*.
3. Log the original code next to the friendly message. Always both.
4. `EmptyState` works for errors too: `actionLabel="Try again"` with a retry is a good fit
   for a connection failure.

### Accessibility: labels, touch targets, contrast

1. Add labels, hints and roles (examples above) wherever the visible text isn't enough:
   the connection row, the control buttons, the calendar, the charts.
2. Apply the `hitSlop` fix to every small link.
3. Contrast: the theme's main text colors were chosen to be readable (WCAG AA). If a
   screen uses a *new* pairing, check it with a contrast checker. `textSecondary` on
   `surface` (captions inside cards) is the closest pair, so check that one.

### Vibration on speed confirmation

Covered above, in "Shared pieces → 4".

### Keep the screen on during a workout

Phase 08 saves a "Keep screen awake" setting that nothing uses yet. Make it work:
`npx expo install expo-keep-awake`, then a tiny component shown on the dashboard while a
workout is `active` and the setting is on:

```tsx
import { useKeepAwake } from 'expo-keep-awake';

/** Keeps the screen on while it's shown. Removing it lets the screen sleep again. */
export function KeepAwakeWhileWorkout() {
  useKeepAwake();
  return null;
}
```

```tsx
// in DashboardScreen, with the other hooks: const { snapshot: settings } = useSettings();
{settings.keepScreenAwake && workoutState === 'active' && <KeepAwakeWhileWorkout />}
```

(The "Voice announcements" setting stays unused for now. `expo-speech` is the tool for it
when you want it; it's a feature, not polish.)

### Keep the test screens, behind a developer setting (yours)

They're how the next firmware surprise gets investigated. Don't delete them.
1. Add `showDeveloperOptions` (default `false`) to Phase 08's `AppSettings`, like the
   other switches, with a test for its default.
2. Show the dashboard's "Diagnostics" link only when it's on.
3. Leave the screens themselves in `src/app/diagnostics/`. Only the link is hidden, so
   you can still open them directly
   (`adb shell am start -a android.intent.action.VIEW -d "<your-scheme>://diagnostics"`;
   the scheme is in `app.json`).
4. Add the switch at the bottom of the Settings screen, under "Developer", off by default.

---

## Tests

- Every screen in light and dark mode, at 33% and full width
- A TalkBack pass over the dashboard and controls
- Font size at maximum doesn't break any layout

### How to run each one

1. **Light/dark:** switch the phone's dark theme (**Settings → Display → Dark theme**)
   and look at every screen, then set the app's own Dark mode to Light and to Dark. All
   colors come from `useTheme()`, so it should just work. If a screen looks wrong in one
   mode, there's almost always a raw color code in it that should be a theme color.
2. **33% / full width:** use split screen to make the app as narrow as possible, check
   every screen (especially that `EmptyState` fits at 33%), then check full width.
3. **TalkBack:** turn it on (**Settings → Accessibility → TalkBack**), then use the
   dashboard and controls with only swipe-to-move and double-tap-to-press. Everything
   that does something should say something useful. Silent, or just "button", means a
   missing `accessibilityLabel`.
4. **Font size at maximum:** **Settings → Display → Font size**, all the way up, then
   check the dashboard, settings and statistics. Flexible layouts adjust; anything that
   gets cut off or overlaps has a fixed `height` that should go. One exception: the big
   numbers can get wider than their tile. Limit just those with
   `maxFontSizeMultiplier={1.5}` (still large, just not huge).

## Done when

- [ ] Ready to release
