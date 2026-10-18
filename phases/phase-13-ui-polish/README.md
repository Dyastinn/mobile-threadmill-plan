# Phase 13 — UI Polish

**Hardware:** none · **Size:** M · **Blocked by:** Phase 12

---

## Goal

This phase is a **polish pass across every screen built in Phases 03/05/08/09/10/11**,
not a new screen of its own. It doesn't rewrite any of those screens. Instead it
delivers a small set of **reusable building blocks** (an empty-state view, a loading
overlay, a body text style, a haptic on speed confirmation, accessibility fixes) that
get dropped into each existing screen at the review checkpoint, once those phases'
real code exists to drop them into.

Per `../README.md`'s collaboration model, **almost this entire phase is UI**. The
agent writes the actual code below, you paste it in and wire it to your hooks' real
return values. See `../../docs/learning/04-Monochrome-Theme.md` for the token
vocabulary everything below is built from. Every snippet here uses only tokens and
components that already exist in `src/features/shared/theme/`, or is explicitly
flagged as something to add there. The two small pieces of logic (the haptic hook-up
and the developer toggle) are yours.

### Understanding what you're building (read this before the tasks)

**Why one shared component instead of four separate ones.** Building each screen's
empty/loading UI inline, one screen at a time, produces four slightly different
versions of the same thing. A fix to one doesn't fix the others.

Phases 03, 09, and 10 each built a real screen, and each of those screens
independently needs to say "there's nothing here yet" (no workouts, no history yet,
not connected) and "this is working on it" (busy while exporting, while loading
history). Phase 13 doesn't go back and redesign those screens. It notices that the
same two needs showed up several separate times, and only now, with real examples in
hand, builds `EmptyState` and `LoadingOverlay` as reusable components with props
(`glyph`, `title`, `message`, `actionLabel`/`onAction`; `visible`, `message`) that get
dropped into each of those screens at the review checkpoint.

**Why not just polish each screen as you build it.** The simpler-sounding plan (and
the one this project actually followed through Phase 12) is to write each screen's
empty/loading markup inline, right there in that screen's TSX, when that screen gets
built. That's not wrong; it's what happened, and it's why this phase exists now rather
than in Phase 03. The cost of continuing that way past this point is concrete: several
separate copies of near-identical `ActivityIndicator` + caption markup, each free to
drift. One screen's spinner ends up a different size, another is missing the
`accessibilityLabel` the others have. When a contrast fix needs to land, it has to
land N times instead of once, and nothing stops the Nth copy from being subtly wrong.
Building `EmptyState`/`LoadingOverlay` *before* Phase 03, on the other hand, would
have been guessing: the actual set of props it needs (`onAction` for a retry button,
`glyphLabel` for accessibility) only became knowable by having real screens with real
"nothing to show" reasons to look at. Phase 13 sits at exactly the point where the
repetition is proven and the shape is known, not before.

**The pattern, named plainly.** This is "Don't Repeat Yourself" (DRY) via
extract-shared-component. In React the mechanism is just a function component with
props, which is cheap: no registration, no boilerplate per property. The cost that
remains is the *decision* of what the props are, and getting that wrong means every
caller works around it. That cost buys a specific payoff for this project: a fix to
how "no workouts" looks (spacing, wording tone, the glyph) happens in one file and
appears correctly on every screen that uses it, including screens Phase 14+ hasn't
built yet. It would *not* be worth it for a screen with a genuinely one-off empty
condition unlike any other screen's. The win here comes specifically from the same
glyph/title/message/action shape recurring across several real screens, which is the
concrete, already-observed repetition Metz's "wait for the second or third
occurrence" rule asks for before you extract anything.

## Learning goals

- **Composition with `children` and props**: a reusable component is a function that
  takes props; layering one over another is just rendering it later in the same
  parent with `position: 'absolute'`.
- **Absolute positioning for overlays**: `StyleSheet.absoluteFill` makes the loading
  overlay cover exactly its parent, without a modal or a portal.
- **React Native's accessibility props**: `accessibilityLabel` (what a screen reader
  says), `accessibilityHint` (what happens if you activate it), `accessibilityRole`
  (button, header, image...), `accessibilityState` (disabled, selected), and when
  *not* to set a label.
- **`expo-haptics`**: the platform vibration API, and why only one moment in this app
  earns a haptic.
- **48 dp touch targets**: Android's accessibility guidance (48×48 dp minimum) and
  `hitSlop` for a small element that should still be easy to hit.
- **Font scaling**: React Native text grows with the system font size by default,
  and what that does to a 40-point metric number at 200%.

## Reference docs

| Topic | URL |
|---|---|
| App icons (Expo, incl. Android adaptive and monochrome icons) | https://docs.expo.dev/develop/user-interface/app-icons/ |
| `expo-splash-screen` (config plugin, light and dark backgrounds) | https://docs.expo.dev/versions/latest/sdk/splash-screen/ |
| React Native accessibility | https://reactnative.dev/docs/accessibility |
| `expo-haptics` | https://docs.expo.dev/versions/latest/sdk/haptics/ |
| `expo-keep-awake` | https://docs.expo.dev/versions/latest/sdk/keep-awake/ |
| Android: make apps more accessible (48 dp touch target guidance) | https://developer.android.com/guide/topics/ui/accessibility/apps |
| Android: test your app's accessibility (TalkBack pass) | https://developer.android.com/guide/topics/ui/accessibility/testing |
| `StyleSheet` (`absoluteFill`) | https://reactnative.dev/docs/stylesheet |

---

## Reusable building blocks

These live under `src/features/shared/theme/` alongside the components from Phase 00.
Full working code: paste it in as-is, then wire it into each real screen at the review
checkpoint.

### 1. Typography scale

The theme's `Text` component already has the variants `caption` (12), `metricLabel`
(13), `default` (15), `subheadline` (22), `headline` (28) and `metric` (40). One gap:
there's no variant for ordinary paragraph text sized a step above the default: an
error-message body, an empty-state message. Add `body` to the `typography` token in
`tokens.ts`:

```ts
body: { fontSize: 17, lineHeight: 24 },
```

and to the `variant` union in `Text.tsx`. The resulting scale, smallest to largest:
`caption` (12) → `metricLabel` (13) → `default` (15) → `body` (17) → `subheadline` (22)
→ `headline` (28) → `metric` (40).

**Consistent spacing** is already a token: `spacing.xs` through `spacing.xxl` (4, 8, 12,
16, 24, 32) from Phase 00. The convention going forward is the same as it's been: pick
from those, and treat any raw number in a `style` as something to justify at review.
The one standing exception is `14` horizontal padding inside `Surface` rows, already
used since Phase 08.

### 2. Empty state — `EmptyState`

For any screen that can legitimately have nothing to show yet (no workouts, no
history, not connected): a glyph, title, message, and an optional action button.

**`src/features/shared/theme/EmptyState.tsx`:**

```tsx
import { View } from 'react-native';

import { SecondaryButton } from './SecondaryButton';
import { Text } from './Text';
import { spacing } from './tokens';
import { useTheme } from './useTheme';

interface Props {
  /** A plain text glyph. No icon font in this project yet; see the note below. */
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

The glyph is a plain character (`○`) rather than an icon, because this project has no
icon set yet. Swapping in real icons (e.g. `@expo/vector-icons`, which ships with
Expo) is a reasonable follow-up; flag that decision at a review checkpoint rather than
pulling a library in now for one glyph. A purely decorative glyph is hidden from the
screen reader (`no-hide-descendants`); pass `glyphLabel` only if it carries meaning.

**Usage example** (Phase 10's history list when there are zero workouts), passed to
`FlatList` so it shows only when the list is empty:

```tsx
<FlatList
  // ...data, renderItem as before
  ListEmptyComponent={
    <EmptyState
      title="No workouts yet"
      message="Connect to the treadmill and start a workout to see it here."
    />
  }
/>
```

### 3. Loading overlay — `LoadingOverlay`

A dimmed `ActivityIndicator` that covers a screen's normal content while something
runs, instead of every screen writing its own busy markup from scratch.

**`src/features/shared/theme/LoadingOverlay.tsx`:**

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

**Usage, showing the layering pattern:** the overlay is rendered *last* inside the
screen's root, so it paints on top of everything before it. `StyleSheet.absoluteFill`
pins it to all four edges of that parent. The parent's touches are blocked while it's
visible, because the overlay is a real view sitting in front of them.

```tsx
<Screen>
  <ScrollView>{/* normal screen content */}</ScrollView>
  <LoadingOverlay visible={b.busy} message={b.progressStage ?? undefined} />
</Screen>
```

`accessibilityLiveRegion="polite"` makes TalkBack announce the message when it
appears, without interrupting.

### 4. Haptic on speed-change confirmation (logic: yours)

`expo-haptics` (`npx expo install expo-haptics`) gives `Haptics.selectionAsync()` (a
light tick) and `Haptics.impactAsync(...)`.

The haptic belongs to the moment the treadmill **confirms** a new speed, not to every
debounced tap. That moment lives inside `SpeedController` (Phase 05), which is in
`core/` and can't import `expo-haptics`. So inject it, the same way Phase 06 injected
the UUID generator:

1. Give `SpeedController`'s constructor an optional
   `onSpeedConfirmed?: () => void` and call it right after `setSpeed()` resolves with
   `success: true`. Never on failure: a vibration on a *failed* command would read as
   confirmation of the wrong thing.
2. In `services.tsx`, pass `() => void Haptics.selectionAsync()`.
3. A test: with a fake treadmill that accepts the command, the callback runs once;
   with one that rejects it, zero times.

Nothing else in this app gets a haptic, per the monochrome theme's "convey state
without a second signal channel unless it earns it" philosophy. Resist the urge to add
one to every button tap.

### 5. Accessibility — concrete examples

**A control whose visible text doesn't say enough** (the `−`/`+` buttons from Phase
05 already have this):

```tsx
<Button label={'−'} onPress={...} accessibilityLabel="Decrease speed" />
```

**A control that needs more context than its label** (the Stop button):

```tsx
<Button
  label="Stop"
  onPress={confirmStop}
  accessibilityHint="Asks for confirmation, then sends a Bluetooth stop command. The physical safety key is the emergency stop."
/>
```

**A group read as one unit** (the connection row: a dot plus a word):

```tsx
<View accessible accessibilityLabel={`Treadmill ${state}`}>
  <ConnectionDot connected={state === 'ready'} />
  <Text variant="caption">{state}</Text>
</View>
```

Two rules worth knowing before applying this everywhere:

- **Don't put an `accessibilityLabel` on a `Text` that already says the right thing.**
  The label *replaces* the spoken text, it doesn't add to it, so a label that's less
  precise than the text makes things worse.
- **A `TextInput` should keep its visible placeholder or a visible label next to it**,
  and use `accessibilityHint` for extra context; don't hide what the field is behind
  a label only the screen reader hears.

### 6. Touch targets — verify, and fix the small links

The theme's `Button`, `SecondaryButton` and `SegmentedControl` already enforce a
**48 dp** minimum height (Android's own accessibility guidance; Phase 00 built them
that way). What doesn't: the small text links on the dashboard ("History",
"Settings", ...) are caption-sized text, maybe 16 dp tall. Give each one
`hitSlop={{ top: 16, bottom: 16, left: 8, right: 8 }}` (on the `Link`'s child via
`asChild` and a `Pressable`), or padding, so the touchable area reaches 48 dp without
changing how it looks.

---

## Tasks

### App icon, splash

Concrete steps:
1. Create the artwork: a 1024×1024 `icon.png`, an adaptive-icon foreground
   (`adaptive-icon.png`, with the subject inside the central safe zone), and a
   single-color `monochrome-icon.png` for Android 13+'s themed icons. See the app
   icons doc above for exactly what "adaptive" layering means and why there are
   separate files.
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
   and the splash screen through the `expo-splash-screen` plugin, which accepts a
   separate dark-mode background (unlike a single static color):
   ```json
   ["expo-splash-screen", {
     "image": "./assets/images/splash-icon.png",
     "imageWidth": 160,
     "backgroundColor": "#F2F2F3",
     "dark": { "backgroundColor": "#212123" }
   }]
   ```
   `#F2F2F3` and `#212123` are the theme's lightest and darkest steps (`Gray050`,
   `Gray950`), so the first frame matches the app.
3. `npx expo prebuild --clean` and rebuild: icons and splash are native resources, so
   they only change with a native rebuild.
4. Check the launcher icon (including with the phone's themed icons turned on) and the
   first-frame splash in both light and dark mode, on the device. This is one of the
   few things you genuinely cannot verify from the code alone.

### Typography scale, consistent spacing

Covered in full above under "Reusable building blocks → 1." Apply `body` to any text
currently relying on the default size for actual paragraph copy: settings
descriptions, empty-state messages, error explanations.

### Loading and empty states for **every** screen

Concrete steps, repeated once per screen from Phases 03/05/08/09/10/11:
1. Identify the screen's "nothing to show yet" condition (no workouts, not yet
   connected, empty history) and its busy flag, if it has one.
2. Add `EmptyState` (as a `ListEmptyComponent`, or conditionally rendered) and
   `LoadingOverlay` (last child of the screen root) per the usage examples above.
3. Confirm at the review checkpoint: does the empty state read correctly for *that*
   screen's specific nothing-to-show reason (a fresh install has no workouts; a
   disconnected dashboard isn't "empty", it's "not connected yet")?

### Error messages that say what to do, not what failed

**"Couldn't reach the treadmill. Check it's powered on and try again"** beats
"GATT error 133," though the code still belongs in the log. Concrete steps:
1. Audit every user-facing error surface across Phases 02/05/07/09 (connection
   failures, control-point rejections, foreground-service and permission issues,
   backup errors); list the raw codes or exception messages currently shown, if any.
2. For each, write a short plain-language sentence describing the *next action* the
   user can take, not the failure mechanism.
3. Log the original code/exception alongside the friendly message (both, always: the
   friendly message is for the user, the code is for you debugging later).
4. `EmptyState` doubles as an error surface. With `actionLabel="Try again"` and an
   `onAction` that retries, it's a natural fit for a connection failure, not just a
   genuinely empty list.

### Accessibility: labels, touch targets, contrast check

Concrete steps:
1. Apply labels, hints and roles per "Accessibility — concrete examples" above to
   every control whose visible text isn't enough (dashboard connection row, control
   buttons, the contribution graph, chart containers).
2. Apply the `hitSlop` fix from "Touch targets" to every small link.
3. Contrast check: the monochrome ramp's text-on-background pairings
   (`textPrimary` on `background`/`surface`) were chosen to clear WCAG AA at normal
   text size. Spot-check with a contrast checker if a screen introduces a *new*
   pairing not already in `04-Monochrome-Theme.md`'s token table (e.g.
   `textSecondary` on `surface`, used for captions inside cards, is the pairing most
   worth double-checking since it's the smallest gap in the ramp).

### Haptics on speed change confirmation

Covered in full above under "Reusable building blocks → 4."

### Keep the screen awake during a workout

Phase 08 stores a "Keep screen awake" setting that nothing reads yet. Make it real:
`npx expo install expo-keep-awake`, then a tiny component rendered in the dashboard
while a workout is `active` and the setting is on:

```tsx
import { useKeepAwake } from 'expo-keep-awake';

/** Mounted only while it should apply; unmounting releases the keep-awake. */
export function KeepAwakeWhileWorkout() {
  useKeepAwake();
  return null;
}
```

```tsx
// in DashboardScreen, next to the other hooks: const { snapshot: settings } = useSettings();
{settings.keepScreenAwake && workoutState === 'active' && <KeepAwakeWhileWorkout />}
```

(The "Voice announcements" setting stays unused for now. `expo-speech` is the obvious
tool when you want it; it's a feature, not polish, so it's not in this phase.)

### The diagnostic screens from Phase 00 stay, behind a developer toggle (logic: yours)

They are how the next firmware surprise gets diagnosed. Do not delete them. Concrete
steps:
1. Add one more setting to Phase 08's `AppSettings`: `showDeveloperOptions`
   (default `false`), same shape as the other booleans, with a test for its default.
2. On the dashboard, render the "Diagnostics" link only when it's on.
3. Leave the routes themselves in place (`src/app/diagnostics/*`). Only the link is
   gated, so a developer can still reach them with `adb shell am start -a
   android.intent.action.VIEW -d "<your-scheme>://diagnostics"` (the scheme is in
   `app.json`) even with the toggle off.
4. Add the toggle to the Settings screen from Phase 08, off by default, at the bottom,
   under a "Developer" heading.

---

## Tests

- Every screen reviewed in light and dark, at 33% and full width
- TalkBack pass over the dashboard and controls
- Font scale at 200% does not break layouts

### How to actually run each of these

1. **Light/dark:** toggle the phone's system theme (**Settings → Display → Dark
   theme**) and revisit every screen, then set the app's own Dark mode setting to
   Light and Dark explicitly. Every color comes from `useTheme()`, so this should be
   automatic with zero code changes; if a screen looks wrong in one mode, it's almost
   always a raw hex value that snuck in instead of a token (see
   `04-Monochrome-Theme.md`'s "never invent a hex color" rule).
2. **33% / full width:** use Phase 11's split-screen support to resize the app to its
   narrowest supported size and check every screen (particularly `EmptyState`'s
   `maxWidth: 320`: confirm it doesn't overflow at 33% on a small phone) and again at
   full width.
3. **TalkBack pass:** enable TalkBack (**Settings → Accessibility → TalkBack**, per
   the [Android accessibility testing guide](https://developer.android.com/guide/topics/ui/accessibility/testing)),
   then navigate the dashboard and controls using only swipe-to-move-focus and
   double-tap-to-activate, no direct touch. Every control that does something should
   announce something meaningful; anything silent, or announcing only "button", is
   missing an `accessibilityLabel`.
4. **Font scale 200%:** **Settings → Display → Font size**, set to maximum, and
   revisit the dashboard and any screen with dense text (settings, statistics).
   Layouts built from flexbox rows and columns reflow; a layout that clips or overlaps
   at 200% has a fixed `height` somewhere that should go. One deliberate exception:
   the big `metric` numbers can grow wider than their tile at 200%. Capping them with
   `maxFontSizeMultiplier={1.5}` on that one `Text` variant is a reasonable fix (the
   number stays large either way), rather than letting a speed readout wrap.

## Acceptance

- [ ] Release candidate
