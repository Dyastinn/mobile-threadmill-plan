# Phase 13 — UI Polish (Flutter track)

**Hardware:** none · **Size:** M · **Blocked by:** Phase 12

## Goal

A polish pass across every screen built in Phases 03/05/08/10/11, not a new
screen of its own — same framing as the original track. This phase delivers
a small set of reusable widgets (empty state, loading overlay, haptics,
accessibility examples) that get dropped into each existing screen once
those phases' real code exists.

## The concept

Building each screen's empty/loading UI inline produces four slightly
different versions of the same thing; a fix to one doesn't fix the others.
Same "wait for the second or third occurrence before extracting" reasoning
as the original — Phases 03/08/09/10 each independently needed "there's
nothing here yet" and "this is working on it," and only now, with four real
examples in hand, is it worth building shared widgets for both.

**A genuine Flutter simplicity win worth naming.** The original track needed
`BindableProperty` — roughly five lines of ceremony per property (a static
field, a getter/setter, sometimes a change callback) — to make a custom
`ContentView` expose bindable parameters. In Flutter, a widget's constructor
parameters *are* that mechanism: `const EmptyStateView({required this.title, required this.message, this.actionText, this.onAction})` is the whole "bindable property" story, no separate ceremony
type needed. This isn't a workaround, it's how Flutter widgets are meant to
be built — composition over a widget's own constructor, rather than a
framework-provided property-registration API.

## Reusable widgets

### `EmptyStateView`

```dart
class EmptyStateView extends StatelessWidget {
  const EmptyStateView({
    super.key,
    this.icon = Icons.circle_outlined,
    required this.title,
    required this.message,
    this.actionText,
    this.onAction,
  });

  final IconData icon;
  final String title;
  final String message;
  final String? actionText;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 320),
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Icon(icon, size: 40, color: Theme.of(context).colorScheme.onSurfaceVariant),
            const SizedBox(height: 12),
            Text(title, style: Theme.of(context).textTheme.titleLarge, textAlign: TextAlign.center),
            const SizedBox(height: 8),
            Text(
              message,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            if (actionText != null && onAction != null) ...[
              const SizedBox(height: 12),
              OutlinedButton(onPressed: onAction, child: Text(actionText!)),
            ],
          ]),
        ),
      ),
    );
  }
}
```

Usage (e.g. an empty workout history list):

```dart
const EmptyStateView(
  title: 'No workouts yet',
  message: 'Connect to the treadmill and start a workout to see it here.',
)
```

### `LoadingOverlay`

Layered over normal content in a `Stack` — the direct equivalent of the
original's "later `Grid` child paints on top" trick, since Flutter stacks
`Stack` children the same way:

```dart
class LoadingOverlay extends StatelessWidget {
  const LoadingOverlay({super.key, required this.isRunning, this.message});
  final bool isRunning;
  final String? message;

  @override
  Widget build(BuildContext context) {
    if (!isRunning) return const SizedBox.shrink();
    return Container(
      color: Theme.of(context).colorScheme.surface.withValues(alpha: 0.9),
      child: Center(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          const SizedBox(
            width: 36, height: 36,
            child: CircularProgressIndicator(semanticsLabel: 'Loading'),
          ),
          if (message != null) ...[
            const SizedBox(height: 8),
            Text(message!, style: Theme.of(context).textTheme.bodySmall),
          ],
        ]),
      ),
    );
  }
}
```

Usage — normal content, then the overlay, as later children of a `Stack`:

```dart
Stack(children: [
  const DashboardContent(),
  EmptyStateView(
    title: 'No workouts yet',
    message: '...',
  ), // only if you want it always mounted; more commonly gated by an `if`
  LoadingOverlay(isRunning: state.isBusy, message: 'Connecting...'),
])
```

### Haptics — built into Flutter, no package needed

`HapticFeedback` ships in `package:flutter/services.dart` — unlike the
original track, which needed a MAUI Essentials API surface
(`HapticFeedback.Default.Perform(...)`), this is core Flutter, zero extra
dependency:

```dart
import 'package:flutter/services.dart';

Future<void> confirmSpeedChange(TreadmillControlNotifier notifier, double newSpeedKph) async {
  final result = await notifier.setSpeed(newSpeedKph);
  if (result.success) {
    HapticFeedback.selectionClick(); // short tap — confirmation, not a warning
  }
  // On failure: surface the plain-language error, no haptic — a vibration
  // on a failed command would read as confirmation of the wrong thing.
}
```

No manifest permission needed for standard haptic feedback on Android via
Flutter's abstraction (unlike a raw `VIBRATE` permission some native
implementations require) — `HapticFeedback` routes through the platform's
own accessibility/haptic service.

### Accessibility — `Semantics`

Flutter's accessibility API is a single widget, `Semantics`, wrapping
anything that needs a screen-reader label — the equivalent of
`SemanticProperties.Description`/`.Hint`:

```dart
Semantics(
  label: 'Stop treadmill',
  hint: 'Requests the treadmill to stop the belt. The physical safety key is the emergency stop.',
  child: IconButton(icon: const Icon(Icons.stop), onPressed: notifier.stop),
)
```

Two things worth knowing, mirroring the original track's own caveats:

- Don't wrap a `Text` widget in a redundant `Semantics(label: ...)` that
  duplicates its own visible text — TalkBack already reads a `Text`
  widget's content; an extra label without `excludeSemantics: true` just
  makes it announce the text twice.
- `TextField` (Flutter's `Entry`/`Editor` equivalent) already exposes its
  `decoration.labelText`/`hintText` to TalkBack correctly; don't add a
  competing `Semantics` wrapper around it.

### Touch target size — no discrepancy to fix

The original track had to flag and defer a 44pt-vs-48dp mismatch (a MAUI
template default carried over from Apple's HIG figure, wrong for an
Android-only app). **Flutter doesn't inherit that mismatch**: Flutter's own
`kMinInteractiveDimension` constant is `48.0` logical pixels, matching
Android's own accessibility guidance directly, and Material widgets
(`IconButton`, `TextButton`, etc.) already respect it by default. Nothing to
flag or fix here — confirm it during the TalkBack pass below rather than
assuming, but there's no known-wrong default to correct.

## Tasks

Same checklist as the original track, applied per-screen once Phases
03/05/08/10/11 exist:

1. Identify each screen's "nothing to show yet" condition and its existing
   `isBusy`-equivalent state.
2. Wrap the screen's root content in a `Stack` if it isn't already.
3. Add `EmptyStateView`/`LoadingOverlay` as later `Stack` children, gated on
   that screen's actual empty/busy condition.
4. Audit every user-facing error surface (Phases 02/05/07): replace raw
   exception text with a plain-language sentence describing the next
   action, log the original error alongside it, never instead of it.
5. Apply `Semantics` to every icon-only control.
6. Contrast check: spot-check any color pairing not already validated once
   this track has its own theme documentation (a Flutter equivalent of
   `04-Monochrome-Theme.md` is a reasonable follow-up, not required to
   start this phase).
7. Keep the Phase 00 diagnostic screens reachable behind a developer
   toggle in Settings (Phase 08), off by default — do not delete them, they
   are how the next firmware surprise gets diagnosed.

## Tests

- Every screen reviewed in light and dark, at 33% and full width (Phase 11).
- TalkBack pass over the dashboard and controls: **Settings → Accessibility
  → TalkBack**, navigate using only swipe-to-move-focus and
  double-tap-to-activate; anything silent or announcing only "Button" is
  missing a `Semantics` label.
- System font scale at 200% (**Settings → Display → Font size**) does not
  break layouts — `Column`/`ListView`-based layouts (already this project's
  convention) should reflow; a layout that clips or overlaps has a
  hardcoded `SizedBox`/`height` somewhere that needs to become flexible.

## Acceptance

- [ ] Release candidate
