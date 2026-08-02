# Phase 11 — Split Screen (Flutter track)

> One of the two reasons this app exists at all.

**Hardware:** required to verify · **Size:** M · **Blocked by:** Phase 10

## Goal

Usable alongside YouTube — responsive layout at 33%/50%/75%/full, speed
controls reachable at 33% height, no horizontal scrolling at any width.

## The concept

By default, a "big" Android configuration event tears the activity down and
rebuilds it, unless the manifest declares the app handles that
configuration change itself. Left unchecked, that default would drop the
Bluetooth connection on every split-screen resize, mid-workout.

**Flutter's advantage here, worth stating plainly:** the manifest Flutter's
own project template generates already declares a broad
`android:configChanges` list on `MainActivity` by default —
`orientation|keyboardHidden|keyboard|screenSize|smallestScreenSize|locale|layoutDirection|fontScale|screenLayout|density|uiMode`
— which already covers what a split-screen resize triggers
(`screenSize`/`smallestScreenSize`/`screenLayout`), without this project
having added anything. This is the same "verify it already holds" situation
the original track hit with its own hand-declared `ConfigurationChanges`
list, except here it's Flutter's own scaffold that did the work, not a
manual edit from an earlier phase. **Task 11.1 is confirming this, not
writing it.**

The second reason a resize doesn't need to touch the connection at all:
Phase 07's foreground service owns the BLE connection, not the UI. The UI
only relays data to/from it. So even in the hypothetical worst case of
`MainActivity` being torn down mid-resize, the connection wouldn't go down
with it — it was never the activity's to lose. Same Single Responsibility
payoff the original track named: this wasn't built *for* split screen, it
was already the right architecture for surviving screen-lock (Phase 07), and
split screen collects it for free.

## Tasks

### 11.1 — Manifest: `resizeableActivity` + verify `configChanges`

Mostly verification. What's genuinely missing by default: `resizeableActivity`
itself. Add to `android/app/src/main/AndroidManifest.xml`'s `<activity>` entry:

```xml
<activity
    android:name=".MainActivity"
    android:resizeableActivity="true"
    android:configChanges="orientation|keyboardHidden|keyboard|screenSize|smallestScreenSize|locale|layoutDirection|fontScale|screenLayout|density|uiMode"
    ...>
```

`[HUMAN]`: drag the app into split screen from Recents (confirms the
affordance is actually available); with the app connected and mid-workout,
trigger a resize and confirm in `adb logcat` that `MainActivity` is **not**
destroyed and recreated (no second `onCreate`/Flutter engine restart in the
log) — the test that proves the existing `configChanges` list is actually
sufficient, not just plausible-looking.

### 11.2 — Responsive layout: `LayoutBuilder`, not `AdaptiveTrigger`

Flutter's idiom for "lay out differently based on available space" is
`LayoutBuilder`, reacting to `BoxConstraints` from its parent — this
project's equivalent of `AdaptiveTrigger.MinWindowHeight`, but a plain
widget rather than a XAML-only trigger mechanism, so the same threshold
value can be reasoned about directly in Dart:

```dart
class DashboardScreen extends ConsumerWidget {
  const DashboardScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Scaffold(
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, constraints) {
            // TODO [HUMAN]: replace 420 with the measured window height at
            // ~33% split on the target device (Poco X6 Pro 5G) — an
            // unverified guess is not acceptable here, same rule as the
            // original track's own placeholder value.
            final compact = constraints.maxHeight < 420;

            return Column(children: [
              if (!compact) ...[
                const ContributionGraph(), // Phase 03
                const SizedBox(height: 12),
              ],
              const ConnectionIndicator(), // Phase 02
              const SizedBox(height: 8),
              Text(
                speedText,
                style: compact
                    ? Theme.of(context).textTheme.titleLarge
                    : Theme.of(context).textTheme.displayMedium,
              ),
              if (!compact) const SecondaryMetricsRow(), // distance/calories/elapsed
              const Spacer(),
              // Speed controls — ALWAYS visible in both branches. This row
              // is the entire reason `compact` exists: everything above it
              // can be sacrificed, this can't.
              const TreadmillControlPanel(), // Phase 05
            ]);
          },
        ),
      ),
    );
  }
}
```

`LayoutBuilder` rebuilds its `builder` callback whenever the constraints it's
given actually change — including a split-screen resize — with no separate
state-trigger mechanism to wire up, unlike XAML's `VisualStateManager` +
named `VisualState`s. The tradeoff: layout logic lives in Dart conditionals
(`if (!compact) ...`) rather than declarative XML setters, which is more
idiomatic Flutter but means the two layout variants are read as one
`build()` method rather than two named states — worth knowing as a real
difference in how the same requirement gets expressed, not a strictly
better-or-worse one.

Concrete steps:
1. `[HUMAN]`: measure the real window height at ~33% split on the target
   device and replace the `420` placeholder.
2. Confirm the speed +/−/Stop row is fully visible and tappable at every
   split size, both orientations. Flutter's default minimum interactive
   size (`kMinInteractiveDimension`, 48.0 logical pixels) already matches
   Android's own 48dp accessibility guidance out of the box for standard
   Material widgets like `IconButton` — worth noting since the original
   track had to flag a 44-vs-48 discrepancy carried over from a
   Apple-sized MAUI template default; Flutter's Material widgets don't
   inherit that mismatch.
3. Build and run in a resized desktop/emulator window first to sanity-check
   the `compact` threshold logic, then verify for real on-device — same
   caveat as the original: a resized emulator/desktop window approximates
   this but isn't real Android split screen.

### 11.3 — Verify: no BLE reconnect on resize

Verification, not new code — a fix, if one turns out to be needed, belongs
in 11.1 or in Phase 07, not here.

`[HUMAN]`: start a workout, confirm connected and recording; resize through
all four split sizes, pausing a few seconds at each; confirm
`connectionStateChanges` never leaves `ready` during any resize (watch the
`ConnectionIndicator`, or log state transitions); check the recorded
workout afterward for sample gaps (`WorkoutSample.flags` bit 0) correlated
with a resize timestamp — none should be present.

## Tests

Same manual matrix as the original: each of 4 sizes in both orientations
(controls reachable/tappable), resize mid-workout (no disconnect, no data
gap), another app in the split pane (dashboard keeps updating), no second
`onCreate`/engine restart during a resize, `resizeableActivity` confirmed
present. All `[HUMAN]`.

## Acceptance

- [ ] Fully usable at 33% with no disconnect on resize
- [ ] `resizeableActivity="true"` confirmed present in the manifest
- [ ] `LayoutBuilder` breakpoint value is a measured number from the real
      device, not an unverified guess
- [ ] No horizontal scrolling at any of the 4 sizes, either orientation
