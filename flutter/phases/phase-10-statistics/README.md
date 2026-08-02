# Phase 10 — Statistics (Flutter track)

**Hardware:** none · **Size:** M · **Blocked by:** Phase 09

## Goal

Aggregates and charts — daily/weekly/monthly/yearly totals, personal
records, cross-workout trend charts, and per-workout speed/heart-rate curves
with visible gaps where the connection dropped.

## The concept

Aggregate in SQL (`GROUP BY`/`SUM`/`COUNT`), not by loading rows into Dart
and summing in a loop — SQLite already has the right engine for this, and
it's simply less code, not a performance-driven choice at this table's real
size. The one place raw row counts actually matter is the per-workout sample
series: a two-hour workout is 7,200 rows, a chart needs ~200 points, and
downsampling in SQL (`WHERE elapsedSec % $n = 0`) means roughly 200 rows
cross into Dart instead of 7,200. Statistics and personal records are always
**computed, never stored** — same rule as the original, for the same reason
(a cache that drifts out of sync silently lies).

## Technology decision: `fl_chart`, not a LiveCharts2-equivalent risk

**What problem does it solve?** Two chart shapes: cross-workout trend lines,
and a per-workout speed+heart-rate overlay with visible gaps where a `null`
value should render as a break, not an interpolated line.

**Why are we using it?** `fl_chart` is MIT-licensed, the most widely used
Flutter charting library, and — worth stating plainly since the original
track had to flag real pre-1.0 risk for its charting pick — **actively
maintained with a stable, non-beta release history**, not carrying the same
maintenance-risk caveat LiveCharts2 needed in the MAUI track. It supports
dual-Y-axis line charts and renders a gap wherever a series value is `null`,
which is exactly the mechanism this phase's connection-gap requirement needs.

**Alternatives considered:**

1. **`syncfusion_flutter_charts`** — richer feature set, genuinely
   production-grade, but a commercial license for anything beyond
   Syncfusion's free-community tier eligibility. Not worth the licensing
   question for this app's actual chart needs (two line-chart shapes).
2. **Hand-rolled `CustomPainter`** — same fallback option the original track
   named for its own SkiaSharp equivalent. Real code to own (axis ticks,
   label layout), worth it only if `fl_chart` turns out inadequate for the
   dual-axis gap-rendering requirement specifically.

**Why not the alternatives?** Syncfusion's licensing question isn't worth
raising for a two-chart-shape need `fl_chart` already covers for free.
Hand-rolled is the fallback, not the default, same reasoning as the original.

## `StatisticsProvider`

```dart
// myhi_companion_core/lib/statistics/statistics_provider.dart
enum AggregateGranularity { daily, weekly, monthly, yearly }
typedef AggregateBucket = ({String bucketLabel, int workoutCount, double totalDistanceMeters, int totalCalories, int totalDurationSeconds});
typedef WorkoutSamplePoint = ({int elapsedSec, double? speedKph, int? heartRate});

abstract interface class StatisticsProvider {
  Future<List<AggregateBucket>> getAggregates(AggregateGranularity granularity, DateTime fromUtc, DateTime toUtc);
  Future<PersonalRecords> getPersonalRecords();
  Future<List<WorkoutSamplePoint>> getWorkoutSeries(int workoutRowId, int maxPoints);
}

class SqfliteStatisticsProvider implements StatisticsProvider {
  final Database _db;
  SqfliteStatisticsProvider(this._db);

  @override
  Future<List<AggregateBucket>> getAggregates(AggregateGranularity granularity, DateTime fromUtc, DateTime toUtc) async {
    // TODO: pick the date()/strftime() GROUP BY expression for `granularity`
    // (weekly/monthly/yearly are the same query as Phase 06's daily example
    // with a different bucket expression — work a real DST-boundary date
    // out on paper before trusting the SQL, same caution as the original).
    throw UnimplementedError();
  }

  @override
  Future<PersonalRecords> getPersonalRecords() async {
    // TODO: three independent `SELECT ... ORDER BY <column> DESC LIMIT 1`
    // queries against Workout WHERE status = 1. Handle zero rows explicitly
    // — no records, not an exception.
    throw UnimplementedError();
  }

  @override
  Future<List<WorkoutSamplePoint>> getWorkoutSeries(int workoutRowId, int maxPoints) async {
    // TODO:
    // 1. Get the row count for this workout, compute n = max(1, count / maxPoints).
    // 2. SELECT elapsedSec, speedKph, heartRate, flags FROM WorkoutSample
    //    WHERE workoutRowId = ? AND elapsedSec % ? = 0 ORDER BY elapsedSec.
    // 3. Map each row to a WorkoutSamplePoint — where flags & 1 == 1 (the
    //    gap bit), speedKph/heartRate become null regardless of the stored
    //    value, not passed through as-is.
    throw UnimplementedError();
  }
}
```

A `FakeStatisticsProvider` (same seam pattern as every other provider in
this track) generates plausible synthetic buckets/records/points so the
chart widgets below can be built and reviewed before the real queries are
written.

## Cross-workout trend chart

```dart
class TrendChart extends ConsumerWidget {
  const TrendChart({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final buckets = ref.watch(weeklyDistanceProvider); // AsyncValue<List<AggregateBucket>>
    return buckets.when(
      loading: () => const CircularProgressIndicator(),
      error: (e, _) => Text('$e'),
      data: (data) => SizedBox(
        height: 220,
        child: LineChart(LineChartData(
          lineBarsData: [
            LineChartBarData(
              spots: [
                for (var i = 0; i < data.length; i++)
                  FlSpot(i.toDouble(), data[i].totalDistanceMeters / 1000.0),
              ],
              color: Theme.of(context).colorScheme.onSurface,
              barWidth: 2,
              dotData: const FlDotData(show: false),
            ),
          ],
          titlesData: FlTitlesData(
            bottomTitles: AxisTitles(sideTitles: SideTitles(
              showTitles: true,
              getTitlesWidget: (value, meta) => Text(
                data[value.toInt().clamp(0, data.length - 1)].bucketLabel,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            )),
          ),
        )),
      ),
    );
  }
}
```

## Per-workout speed/heart-rate curve, with gaps

```dart
LineChartBarData buildSpeedLine(List<WorkoutSamplePoint> points, Color color) => LineChartBarData(
  spots: [
    for (var i = 0; i < points.length; i++)
      if (points[i].speedKph != null) FlSpot(i.toDouble(), points[i].speedKph!),
      // a point simply omitted from `spots` is how fl_chart renders a gap —
      // the analogue of a `null` entry in a LiveCharts2 LineSeries<double?>
  ],
  color: color,
  barWidth: 2,
  dotData: const FlDotData(show: false),
);

LineChartBarData buildHeartRateLine(List<WorkoutSamplePoint> points, Color color) => LineChartBarData(
  spots: [
    for (var i = 0; i < points.length; i++)
      if (points[i].heartRate != null) FlSpot(i.toDouble(), points[i].heartRate!.toDouble()),
  ],
  color: color,
  barWidth: 2,
  dashArray: const [6, 4], // dashed, not a second hue — same "convey a
                           // second series without a second color" rule as
                           // the original track's monochrome theme
  dotData: const FlDotData(show: false),
);
```

If `PHASE-00-FINDINGS.md` V3 says heart rate is unusable, leave
`buildHeartRateLine`'s result out of the chart's `lineBarsData` entirely —
same "hidden, not shown empty" rule as the dashboard.

## Reference docs

- [`../../../phases/phase-06-recording-schema/README.md`](../../../phases/phase-06-recording-schema/README.md) — the local-day query pattern and downsampling pattern, unchanged by framework.
- [fl_chart documentation](https://github.com/imaNNeoFighT/fl_chart)

## Tests

- Hand-calculate weekly totals for a seeded dataset and compare.
- Timezone: a workout at 23:30 local buckets into the correct local day;
  DST boundary week aggregates correctly.
- Empty dataset renders without crashing.
- 5,000-workout dataset: aggregate queries under 200ms.
- `getWorkoutSeries` on a fixture with a deliberate gap returns `null`
  (omitted from `spots`) at exactly the flagged positions.

## Acceptance

- [ ] Manual calculations match for daily, weekly, monthly
- [ ] No stored derived values anywhere in the schema
- [ ] Cross-workout trend chart renders against the fake provider, then real seeded data
- [ ] A workout with a mid-session connection gap renders as a visible break
- [ ] Heart rate curve absent entirely, not shown empty, if V3 marked it unusable
