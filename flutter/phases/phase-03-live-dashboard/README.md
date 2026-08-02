# Phase 03 — Live Dashboard + Contribution Graph (Flutter track)

**Hardware:** none for development (`FakeTreadmillService`), yes to verify at the end
**Size:** M · **Blocked by:** Phase 01b only (not 01a)

## Goal

The app's real home/dashboard screen: live treadmill metrics, built entirely
against `TreadmillService` and developed against `FakeTreadmillService`, plus
a GitHub-style workout "contribution graph" at the top, fed by a fake data
source until Phase 06 provides real history. Same seam/fake pattern as Phase
01b, applied to a second, unrelated problem — the point is to notice it's a
repeatable technique, not a one-off.

## Part 1 — Live metrics

### The concept

The treadmill can push samples faster than a screen full of tiles can
usefully redraw. `DashboardNotifier` throttles: remember the last UI-update
timestamp, and if less than ~250 ms have passed, skip the update — the next
sample a quarter-second later carries the fresher number anyway. This is
**throttle**, not **debounce**: throttle fires at a bounded rate *while* the
stream keeps producing, always carrying the latest value, which is what a
continuously-changing number like speed needs. Debounce (wait for the stream
to go quiet) would barely update the UI at all during a steady workout.

### `DashboardNotifier`

```dart
class DashboardState {
  const DashboardState({
    this.connectionState = ConnectionState.disconnected,
    this.speedKph,
    this.distanceMeters,
    this.calories,
    this.elapsedSeconds,
    this.heartRate,
    this.showsHeartRate = false,
  });

  final ConnectionState connectionState;
  final double? speedKph;
  final double? distanceMeters;
  final int? calories;
  final int? elapsedSeconds;
  final int? heartRate;
  final bool showsHeartRate; // manual override for the V3 heart-rate verdict

  DashboardState copyWith({...}) => DashboardState(...);
}

class DashboardNotifier extends Notifier<DashboardState> {
  DateTime _lastUiUpdate = DateTime.fromMillisecondsSinceEpoch(0);

  @override
  DashboardState build() {
    final treadmill = ref.watch(treadmillServiceProvider);
    treadmill.samples.listen(_onSample);
    treadmill.connectionStateChanges.listen(
      (c) => state = state.copyWith(connectionState: c.state),
    );
    return const DashboardState();
  }

  void _onSample(TreadmillSample sample) {
    final now = DateTime.now();
    if (now.difference(_lastUiUpdate) < const Duration(milliseconds: 250)) return;
    _lastUiUpdate = now;

    state = state.copyWith(
      speedKph: sample.speedKph,
      distanceMeters: sample.distanceMeters,
      calories: sample.calories,
      elapsedSeconds: sample.elapsedSeconds,
      heartRate: sample.heartRate,
    );
  }
}

final dashboardProvider = NotifierProvider<DashboardNotifier, DashboardState>(
  DashboardNotifier.new,
);
```

Fields the device doesn't actually send are **hidden, not shown as `--`**: a
tile only renders if its value is non-null, same rule as the original track.
Until Phase 01a's capability tracker exists, "shown if the latest sample
carries a non-null value" is the honest interim rule.

### Dashboard widgets

```dart
class MetricTile extends StatelessWidget {
  const MetricTile({super.key, required this.value, required this.unit});
  final String? value;
  final String unit;

  @override
  Widget build(BuildContext context) {
    if (value == null) return const SizedBox.shrink(); // hidden, not "--"
    return Card(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Text(value!, style: Theme.of(context).textTheme.headlineMedium),
          Text(unit, style: Theme.of(context).textTheme.bodySmall),
        ]),
      ),
    );
  }
}

class DashboardScreen extends ConsumerWidget {
  const DashboardScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = ref.watch(dashboardProvider);
    return Scaffold(
      body: SafeArea(
        child: ListView(padding: const EdgeInsets.all(16), children: [
          const ContributionGraph(), // Part 2
          const SizedBox(height: 12),
          const ConnectionIndicator(), // Phase 02
          const SizedBox(height: 16),
          GridView.count(
            crossAxisCount: 2,
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            mainAxisSpacing: 12,
            crossAxisSpacing: 12,
            children: [
              MetricTile(value: s.speedKph?.toStringAsFixed(1), unit: 'km/h'),
              MetricTile(
                value: s.distanceMeters == null ? null : (s.distanceMeters! / 1000).toStringAsFixed(2),
                unit: 'km',
              ),
              MetricTile(value: s.calories?.toString(), unit: 'kcal'),
              MetricTile(
                value: s.elapsedSeconds == null ? null
                    : Duration(seconds: s.elapsedSeconds!).toString().substring(2, 7),
                unit: 'elapsed',
              ),
            ],
          ),
          if (s.showsHeartRate) MetricTile(value: s.heartRate?.toString(), unit: 'bpm'),
        ]),
      ),
    );
  }
}
```

`GridView.count` inside a `ListView` (with `shrinkWrap` + non-scrollable
physics) is this track's `GridItemsLayout`/`CollectionView` equivalent for a
small, non-virtualized 2×2 tile grid — virtualization only matters at list
sizes this dashboard never reaches.

## Part 2 — Contribution graph

### The seam

Real workout history doesn't exist until Phase 06. Define the shape now,
fake it, swap the fake for the real thing later with a one-line provider
change — the same pattern as `TreadmillService`/`FakeTreadmillService`:

```dart
// myhi_companion_core/lib/history/workout_history_provider.dart
typedef DailyWorkoutCount = ({DateTime day, int count}); // local calendar day

abstract interface class WorkoutHistoryProvider {
  Future<List<DailyWorkoutCount>> getDailyCounts(DateTime fromLocal, DateTime toLocal);
}

class FakeWorkoutHistoryProvider implements WorkoutHistoryProvider {
  final _random = Random();

  @override
  Future<List<DailyWorkoutCount>> getDailyCounts(DateTime fromLocal, DateTime toLocal) async {
    final days = <DailyWorkoutCount>[];
    for (var d = fromLocal; d.isBefore(toLocal); d = d.add(const Duration(days: 1))) {
      final count = _random.nextDouble() < 0.4 ? _random.nextInt(2) + 1 : 0;
      days.add((day: d, count: count));
    }
    return days;
  }
}
```

**Simplified from GitHub's original**: lit (≥1 workout) or unlit, no
gradient by count — nothing has asked for the gradient yet (YAGNI), and the
door stays open at zero cost since `count` is already a real `int`, not just
a bool.

### The widget

```dart
class ContributionGraph extends ConsumerWidget {
  const ContributionGraph({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final days = ref.watch(contributionDaysProvider); // last ~3-6 months
    return GridView.count(
      crossAxisCount: 7, // GitHub-style: 7 consecutive days per row
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      mainAxisSpacing: 4,
      crossAxisSpacing: 4,
      children: days.map((d) => Container(
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(3),
          color: d.count > 0
              ? Theme.of(context).colorScheme.onSurface
              : Theme.of(context).colorScheme.surfaceContainerHighest,
        ),
      )).toList(),
    );
  }
}
```

`crossAxisCount: 7` is `GridItemsLayout`'s `Span="7"` — feeding days in
chronological order produces rows of 7 consecutive days. A true GitHub layout
(columns = weeks, scrolling horizontally) is a fair follow-up once this
version is reviewed, not required now.

## Reference docs

- [`treadmill_service.dart`](../../packages/myhi_companion_core/lib/treadmill/treadmill_service.dart) — the seam this screen builds against.
- [`../../../phases/phase-01-protocol-decode/README.md`](../../../phases/phase-01-protocol-decode/README.md) — Treadmill Data fields, Heart Rate Service section.
- [`../../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](../../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md) — notification rate, V3 heart-rate verdict.

## Acceptance

- [ ] Smooth updates for 10 minutes with no UI stall (fake service)
- [ ] `[HUMAN]` Displayed values match the console at three different speeds
- [ ] No field shows a placeholder for data the device never sends
- [ ] Contribution graph renders against `FakeWorkoutHistoryProvider`, lit
      days visibly distinct from unlit
- [ ] `WorkoutHistoryProvider` lives in `myhi_companion_core`, zero Flutter
      dependency, registered the same way `TreadmillService` is
