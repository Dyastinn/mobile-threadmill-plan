# Phase 10 — Statistics

**Hardware:** none · **Size:** M · **Blocked by:** Phase 09

> See `../README.md` for the collaboration model. This phase leans UI-heavy: three
> of its five tasks end in a chart. But the SQL underneath every chart is still yours
> to write. The agent writes the LiveCharts2 wiring and XAML, you write the aggregate
> queries, the PR queries, and the downsampling.

---

## Goal

Aggregates and charts. LiveCharts2 (MIT).

### Understanding what you're building (read this before the tasks)

**Why aggregate in SQL, not a C# loop over a loaded table.** `Workout` rows live in
a SQLite database that already has a purpose-built engine for "sum this column,
grouped by that column" (`GROUP BY`/`SUM`/`COUNT`): the query
`../phase-06-recording-schema/README.md`'s "Query patterns" section already gives
for daily totals, which `SqliteStatisticsProvider.GetAggregatesAsync`
extends to weekly/monthly/yearly. The naive alternative pulls every row into a C#
`List<Workout>` (`SELECT * FROM Workout`) and sums it with `.Sum()`/`.GroupBy()` in
memory. That's more code to do the same job.

For the `Workout`-level aggregates (10.1/10.2), be honest about the actual stakes.
This table, even after years of daily use, tops out around a few thousand rows, and
a naive `.Sum()` over that wouldn't be slow enough to notice today. So this isn't
"SQL because performance demands it," it's "SQL because it's already the simplest
correct tool for the job": a `GROUP BY date(...)` query is fewer lines and less code
to own than a hand-rolled dictionary-keyed accumulator loop in C#. Not a more
complex alternative earning its keep.

Where the stakes get real and quantified is task 10.4's per-workout sample series.
A two-hour workout is 7,200 rows in `WorkoutSample` (one per second), and a
400px-wide chart needs on the order of 200 points to render meaningfully. Loading
all 7,200 rows just to thin them to 200 in a C# loop means shipping 36× more data
across the SQLite driver than the chart will ever draw, every time its detail page
opens. The `WHERE ElapsedSec % $n = 0` downsampling query in `GetWorkoutSeriesAsync`
does that thinning inside the database instead, so roughly 200 rows cross into the
app, not 7,200. That's the one place in this phase where "compute it in SQL" earns
its complexity over the loop-in-C# alternative, with a real, quantified number
behind it. Everywhere else in this phase it's simply the shorter, more idiomatic
way to ask the question.

**The pattern, named plainly.** `../phase-06-recording-schema/README.md`'s rule
that a cache table is allowed "if a query becomes slow" but "do not start there"
is an application of
**YAGNI** (you aren't gonna need it yet): resist adding a structure to solve a
problem you haven't actually measured. The cost of *not* pre-building a cache
table is that every chart re-runs its aggregate query from scratch, with no
memoization. The payoff is zero synchronization bugs. A cache table that ever
drifts out of sync with `Workout`/`WorkoutSample` is worse than no cache at all,
because it can silently lie, and there's one less thing to keep correct across
every future phase that writes a workout. This tradeoff would flip, a rebuildable
cache table becoming genuinely worth it, the moment this phase's own performance
test (5,000 workouts, aggregate queries under 200ms) actually fails, not before.
That's the concrete, stated condition, not "in case it's slow later."

**One thing to hold loosely.** The charts in this phase render through LiveCharts2,
and this specific choice isn't fully settled — see the technology decision below.

## Features

- Daily / weekly / monthly / yearly aggregates
- Personal records: longest distance, longest duration, fastest average
- Cross-workout charts: distance, calories, duration, average speed over time
- **Per-workout charts**: speed and heart rate curves from `WorkoutSample`. This is
  the payoff for storing telemetry and the main thing FitShow does badly.

If V3 said heart rate is marginal or unusable, HR curves are hidden here too. The
data keeps being recorded, so they can be switched on later without a gap in history.

## Implementation requirements

- **Statistics and PRs are always computed from `Workout` / `WorkoutSample`, never
  stored.** Not columns, not tables, not exported. If a query becomes slow, add a cache
  table that is *explicitly rebuildable* and rebuild it after any import, but do not
  start there.
- **Aggregate in SQL**, not in C# over a full table read. Query patterns and the
  local-day-via-stored-offset expression are in `../phase-06-recording-schema/README.md`.
- Index `Workout(StartedAtUtc)`. Every aggregate filters on it.
- **Downsample sample series for display.** A 2-hour workout is 1,440 points; a 400 px
  chart needs perhaps 200. Downsample in SQL (`WHERE ElapsedSec % $n = 0`), not by
  loading everything and thinning it in C#.
- Gap-marked samples render as breaks in the line, never as interpolated segments.

---

## Learning goals

- SQL aggregation (`GROUP BY`, `SUM`, `COUNT`) against a real schema, instead of
  pulling rows into C# and looping. This is the pattern
  `../phase-06-recording-schema/README.md` already documents for weekly totals;
  you'll extend it to daily/monthly/yearly and to
  personal records.
- Downsampling a large ordered series in SQL rather than in memory.
- LiveCharts2's `CartesianChart`: `ISeries`, `Axis`, `SolidColorPaint`, and how a
  nullable value in a series creates a visual gap. This is the mechanism that renders
  the connection-gap markers from Phase 06/07 as actual breaks in the line.
- Reusing the seam/fake pattern one more time: `IStatisticsProvider` gets a fake for
  chart-layout work before the real SQL is reviewed, same shape as
  `ITreadmillService`/`FakeTreadmillService` (01b) and `IWorkoutHistoryProvider` (03).

## Technology decision: LiveCharts2 — the one worth re-checking

**What problem does it solve?** This phase needs two chart shapes: cross-workout
trend lines (distance/duration/speed over time) and a per-workout speed+heart-rate
curve over a single workout's samples, with visible gaps where the connection
dropped (a `null` sample, not an interpolated line).

**Why are we using it?** MIT-licensed, SkiaSharp-rendered, targets MAUI directly
with native-feeling controls, and supports the dual-axis + gap-as-null rendering
this phase's speed/heart-rate overlay needs, out of the box.

**⚠ The open risk.** LiveCharts2 has been in beta/RC status for multiple years
without reaching a stable 1.0 release, and 2026 community discussions show real
maintenance-concern reports, including users who've moved to other libraries. For
a project maintained by one person over years, "the library might never reach
1.0, or might ship a breaking change with no real deprecation runway" is a
legitimate risk, not pedantry.

**Alternatives considered:**

1. **Microcharts** (+ `Microcharts.Maui`) — SkiaSharp-based like LiveCharts2,
   genuinely simple, easiest to learn of the three, but no built-in dual-Y-axis
   support. The speed/heart-rate overlay would need a workaround (normalize both
   to 0-1 and lose real axis labels, or split into two stacked charts). Worth
   revisiting once Phase 00's V3 heart-rate finding is confirmed — if HR gets cut
   from the dashboard entirely, Microcharts' single-axis simplicity stops being a
   limitation and becomes the obviously simpler choice.
2. **OxyPlot** (+ `oxyplot-maui`) — broadest .NET platform coverage of any free
   chart library, MIT, **longer track record than LiveCharts2** (predates it by
   years, without the "still not 1.0" issue), supports dual axes and gap
   rendering. But its MAUI bindings are a thinner, less MAUI-native integration
   than LiveCharts2's purpose-built MAUI controls — expect more manual styling to
   match the monochrome theme. The safer swap if the dual-axis requirement stays.
3. **Hand-rolled SkiaSharp canvas** — total control, no charting-library
   dependency. The actual requirement here (a scrollable line, a few dozen to a
   couple thousand downsampled points, no zoom/pan/legend interactivity needed)
   is simple enough that this isn't as extreme as it sounds — `SkiaSharp` itself
   is what all three library options render through anyway. Real code to write
   and own (axis ticks, label layout, downsampling this phase already needs
   regardless): realistically 200-400 lines. The fallback if maintenance risk
   turns out to matter more than development speed.

**Why not the alternatives (tentative — this is the one real open decision)**
Microcharts is the strongest alternative *if* the heart-rate overlay gets cut.
OxyPlot is the safer swap if dual-axis stays a requirement, at the cost of more
manual styling work. Hand-rolled SkiaSharp wasn't the starting recommendation
only because it's more total first-pass code — it's the fallback, not the
default.

**Long-term considerations.** Not a standard/stable technology by its own
project's admission. Pre-1.0 for years is a real signal. **The one genuine
mitigating factor:** chart configuration is isolated to this phase's
`StatisticsViewModel`/`WorkoutDetailViewModel`, not spread through the app. A
future swap wouldn't ripple into any other phase, which de-risks starting with
LiveCharts2 now and revisiting later if it doesn't pan out. Recommendation: don't
block on this today — this phase is late in the plan, which already bought
months to watch LiveCharts2's trajectory — but re-run this comparison
(specifically: has LiveCharts2 reached 1.0? have the 2026 maintenance concerns
resolved or worsened?) before committing to it here, rather than assuming this
record's original conclusion still holds by the time this phase actually starts.

**Practical example:** This phase's cross-workout trend chart and per-workout
speed/HR curve.

---

## Reference docs

- `../phase-06-recording-schema/README.md`: schema, the local-day query pattern, the downsampling
  pattern, performance targets. Read this in full before task 10.1; almost everything
  below builds directly on it.
- [LiveCharts2 documentation home](https://livecharts.dev/) and
  [LiveCharts2 GitHub repository](https://github.com/beto-rodriguez/LiveCharts2),
  already in `docs/learning/03-Doc-Links.md`.
- [LiveCharts2 MAUI installation guide](https://livecharts.dev/docs/maui/2.0.4/overview.installation):
  NuGet package and the `MauiProgram.cs` registration. Verified against the current
  docs while writing this phase. **Not yet in `03-Doc-Links.md`** because it's
  version-pinned in the URL (`2.0.4`); check the version selector on the page against
  whatever `LiveChartsCore.SkiaSharpView.Maui` version you actually install, and read
  the matching docs version if they've since moved on.
- [LiveCharts2 Cartesian chart control](https://livecharts.dev/docs/maui/2.0.4/CartesianChart.Cartesian%20chart%20control):
  the `ISeries[]`/`Axis[]` shape used throughout this phase's chart code.
- `docs/learning/04-Monochrome-Theme.md`: the gray ramp. The "Chart colors" section
  below is the LiveCharts2-specific extension of it, since LiveCharts2 has its own
  `SKColor`-based color system, separate from XAML `StaticResource`.

## Chart colors — the monochrome mapping for LiveCharts2

LiveCharts2 doesn't read `Colors.xaml`. It's a SkiaSharp-backed renderer with its own
`SolidColorPaint`/`SKColor` API. To stay consistent with the rest of the app without
inventing a second palette, every chart in this phase uses `SKColor` values that are
the *exact same hex* as the `GrayNNN` steps already defined in `Colors.xaml`:

| Use | Gray step | Hex | `SKColor` |
|---|---|---|---|
| Primary series (speed; the "main" line on any chart) | Gray900 (light) / Gray100 (dark) | `#2A2A2D` / `#E4E4E6` | `new SKColor(0x2A, 0x2A, 0x2D)` / `new SKColor(0xE4, 0xE4, 0xE6)` |
| Secondary series (heart rate, a comparison line) | Gray500 (light) / Gray300 (dark) | `#7D7D82` / `#B8B8BC` | `new SKColor(0x7D, 0x7D, 0x82)` / `new SKColor(0xB8, 0xB8, 0xBC)` |
| Axis labels / separator lines | Gray600 (light) / Gray400 (dark) | `#626266` / `#9A9AA0` | `new SKColor(0x62, 0x62, 0x66)` / `new SKColor(0x9A, 0x9A, 0xA0)` |

**`SKColor` doesn't respond to `AppThemeBinding`.** That's an XAML-only mechanism.
The code below picks the light or dark pair explicitly from
`Application.Current?.RequestedTheme` at the point the series is built. This is a
real gap worth naming rather than hiding: if the user flips the OS theme while a
chart is already on screen, the chart won't repaint until it's rebuilt. Flag it at
review if you want a cleaner fix (e.g. rebuilding on `Application.RequestedThemeChanged`),
a reasonable follow-up, not a blocker for this phase.

**Second color instead of second hue**: the heart-rate line uses a dashed stroke
(`DashEffect`) rather than a color, following the same "state without hue" rule as
the rest of the app (`04-Monochrome-Theme.md`'s "Conveying state without color"
table). See task 10.4.

---

## Walkthrough

### Task 10.1 — `IStatisticsProvider`: daily/weekly/monthly/yearly aggregates

**Logic: you write this.**

Creates: `src/MyHi.Companion.Core/Statistics/IStatisticsProvider.cs`,
`src/MyHi.Companion.Core/Statistics/AggregateBucket.cs`,
`src/MyHi.Companion.Core/Statistics/SqliteStatisticsProvider.cs`.

`../phase-06-recording-schema/README.md`'s query pattern section already gives you the *daily* version of
this query, local-day-correct via the stored UTC offset:

```sql
SELECT
    date(datetime(StartedAtUtc, '+' || StartOffsetMinutes || ' minutes')) AS LocalDay,
    COUNT(*)             AS Workouts,
    SUM(DistanceMeters)  AS TotalMeters,
    SUM(Calories)        AS TotalCalories,
    SUM(DurationSeconds) AS TotalSeconds
FROM Workout
WHERE Status = 1
  AND StartedAtUtc >= $fromUtc
  AND StartedAtUtc <  $toUtc
GROUP BY LocalDay
ORDER BY LocalDay;
```

Weekly/monthly/yearly are the same query with a different SQLite `date()`/`strftime()`
expression in the `GROUP BY`. Week bucketing is the one that's easy to get subtly
wrong across a DST boundary. Work a real transition date out on paper before trusting
the SQL; that's why it's a named test below rather than an afterthought.

Concrete steps:

1. Create the folder `src/MyHi.Companion.Core/Statistics/`.
2. Define `AggregateBucket` as a `record`:
   ```csharp
   namespace MyHi.Companion.Core.Statistics;

   public sealed record AggregateBucket(
       string BucketLabel,       // e.g. "2026-07-28", "2026-W30", "2026-07"
       int WorkoutCount,
       double TotalDistanceMeters,
       int TotalCalories,
       int TotalDurationSeconds);
   ```
   Decide for yourself whether a raw `string` label is the right shape, or whether a
   `DateOnly` plus the `AggregateGranularity` (next step) serves a chart-binding
   ViewModel better. Think about what task 10.3 actually needs to do with it before
   committing to the shape.
3. Define `AggregateGranularity` as an `enum`: `Daily`, `Weekly`, `Monthly`, `Yearly`.
4. Define `IStatisticsProvider`:
   ```csharp
   namespace MyHi.Companion.Core.Statistics;

   public interface IStatisticsProvider
   {
       Task<IReadOnlyList<AggregateBucket>> GetAggregatesAsync(
           AggregateGranularity granularity,
           DateTimeOffset fromUtc,
           DateTimeOffset toUtc,
           CancellationToken ct = default);

       Task<PersonalRecords> GetPersonalRecordsAsync(CancellationToken ct = default);

       Task<IReadOnlyList<WorkoutSamplePoint>> GetWorkoutSeriesAsync(
           long workoutRowId,
           int maxPoints,
           CancellationToken ct = default);
   }
   ```
   (`PersonalRecords` and `WorkoutSamplePoint` are defined in tasks 10.2 and 10.4;
   the interface is written once, up front, so 10.2/10.4 aren't inventing a second
   provider abstraction.)
5. Implement `SqliteStatisticsProvider` against `Microsoft.Data.Sqlite`, following the
   same connection-per-call pattern as `SqliteConnectionFactory`/`MigrationRunner`
   (already in `src/MyHi.Companion.Core/Data/` from Phase 00). Open via the factory,
   don't hold a connection open across calls:
   ```csharp
   namespace MyHi.Companion.Core.Statistics;

   public sealed class SqliteStatisticsProvider : IStatisticsProvider
   {
       private readonly SqliteConnectionFactory _connectionFactory;

       public SqliteStatisticsProvider(SqliteConnectionFactory connectionFactory)
           => _connectionFactory = connectionFactory;

       public async Task<IReadOnlyList<AggregateBucket>> GetAggregatesAsync(
           AggregateGranularity granularity, DateTimeOffset fromUtc, DateTimeOffset toUtc,
           CancellationToken ct = default)
       {
           // TODO: pick the GROUP BY expression for `granularity` (date()/strftime()
           // variants — see the concept note above), parameterize $fromUtc/$toUtc,
           // ExecuteReaderAsync, map each row into an AggregateBucket.
           throw new NotImplementedException();
       }

       // GetPersonalRecordsAsync (task 10.2), GetWorkoutSeriesAsync (task 10.4)
   }
   ```
6. Register `SqliteStatisticsProvider` as `IStatisticsProvider` in `MauiProgram.cs`.
   Use `AddTransient`, not `AddSingleton`: there's no cross-call state worth sharing, and
   holding a long-lived instance around a short-lived SQLite connection buys nothing.
7. Write the daily-aggregate xUnit test first: seed a handful of known workouts,
   hand-calculate the expected totals, assert. Get that green before writing
   weekly/monthly/yearly. They're the same shape with a different bucket expression,
   and a working daily test gives you a template to copy for each timezone/DST case.

---

### Task 10.2 — Personal records

**Logic: you write this.**

Three independent "biggest single workout" queries: longest distance, longest
duration, fastest average speed. Each is `SELECT ... ORDER BY <column> DESC LIMIT 1`
against `Workout WHERE Status = 1`, no `GROUP BY`. These aren't aggregates over
multiple rows, they're a single row each.

Concrete steps:

1. Define `PersonalRecords` as a `record` holding, for each of the three records: the
   value, the `WorkoutId` (the GUID, not the integer `Id`; see `../phase-06-recording-schema/README.md`'s
   dual-key section for why) it came from, and the date it happened, so the UI can
   link back to the workout.
2. Skeleton for the method:
   ```csharp
   public async Task<PersonalRecords> GetPersonalRecordsAsync(CancellationToken ct = default)
   {
       // TODO: three queries (or one query using window functions / subqueries —
       // your choice) against Workout WHERE Status = 1:
       //   - ORDER BY DistanceMeters DESC LIMIT 1
       //   - ORDER BY DurationSeconds DESC LIMIT 1
       //   - ORDER BY AvgSpeedKph DESC LIMIT 1
       // Handle the empty-database case explicitly: no rows means no records, not an
       // exception — this is one of the phase's tests below.
       throw new NotImplementedException();
   }
   ```
3. Test against a seeded dataset where you know the answer by inspection, plus the
   empty-dataset case.

---

### Task 10.3 — Cross-workout trend chart

**UI: full code, written for you.** Wire the bindings to your actual property names;
everything else (namespaces, series construction, monochrome colors) is ready to
paste in.

LiveCharts2's `CartesianChart` binds to a `Series` collection (`ISeries[]`) and,
optionally, `XAxes`/`YAxes` (`Axis[]`) from its `BindingContext`. A series' *data*
(the numbers) and its *appearance* (color, thickness) live on the same `LineSeries<T>`
object. That's why this is built in the ViewModel rather than pure XAML: LiveCharts2
models "what does this line look like" as a C# object graph, not a XAML style.

`StatisticsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using MyHi.Companion.Core.Statistics;
using SkiaSharp;

namespace MyHi.Companion.Features.Statistics;

public sealed partial class StatisticsViewModel : BaseViewModel
{
    private readonly IStatisticsProvider _statisticsProvider;

    [ObservableProperty] private ISeries[] distanceSeries = [];
    [ObservableProperty] private Axis[] distanceXAxes = [];
    [ObservableProperty] private Axis[] distanceYAxes = [];

    public StatisticsViewModel(IStatisticsProvider statisticsProvider)
        => _statisticsProvider = statisticsProvider;

    // Call from OnAppearing (page code-behind) or a [RelayCommand] — same load
    // pattern as any other ViewModel in this project.
    public async Task LoadWeeklyTrendAsync()
    {
        // TODO (yours): call _statisticsProvider.GetAggregatesAsync with
        // AggregateGranularity.Weekly for a sensible window (e.g. the last 12
        // weeks). The result feeds BuildDistanceChart below.
        IReadOnlyList<AggregateBucket> buckets = await _statisticsProvider.GetAggregatesAsync(
            AggregateGranularity.Weekly,
            DateTimeOffset.UtcNow.AddDays(-84),
            DateTimeOffset.UtcNow);

        BuildDistanceChart(buckets);
    }

    private void BuildDistanceChart(IReadOnlyList<AggregateBucket> buckets)
    {
        bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        // Same hex values as Colors.xaml's Gray900/Gray100 — see this phase's
        // "Chart colors" section for the full mapping and why SKColor can't just
        // reference AppThemeBinding.
        SKColor primaryLine = isDark
            ? new SKColor(0xE4, 0xE4, 0xE6)   // Gray100
            : new SKColor(0x2A, 0x2A, 0x2D);  // Gray900

        SKColor axisText = isDark
            ? new SKColor(0x9A, 0x9A, 0xA0)   // Gray400
            : new SKColor(0x62, 0x62, 0x66);  // Gray600

        DistanceSeries =
        [
            new LineSeries<double>
            {
                Name = "Distance (km)",
                Values = buckets.Select(b => b.TotalDistanceMeters / 1000.0).ToArray(),
                Stroke = new SolidColorPaint(primaryLine) { StrokeThickness = 2 },
                Fill = null,               // no area fill — a plain line, this theme
                                            // doesn't do heavy fills
                GeometrySize = 6,
                GeometryStroke = new SolidColorPaint(primaryLine) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(SKColors.Transparent)
            }
        ];

        DistanceXAxes =
        [
            new Axis
            {
                Labels = buckets.Select(b => b.BucketLabel).ToArray(),
                LabelsPaint = new SolidColorPaint(axisText),
                TextSize = 11
            }
        ];

        DistanceYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(axisText),
                TextSize = 11,
                MinLimit = 0
            }
        ];
    }
}
```

`StatisticsPage.xaml` (trend chart section):

```xml
<ContentPage
    x:Class="MyHi.Companion.Features.Statistics.StatisticsPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:lvc="clr-namespace:LiveChartsCore.SkiaSharpView.Maui;assembly=LiveChartsCore.SkiaSharpView.Maui"
    xmlns:statistics="clr-namespace:MyHi.Companion.Features.Statistics"
    x:DataType="statistics:StatisticsViewModel">

    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="16">

            <Label Text="Weekly distance" Style="{StaticResource SubHeadline}" />

            <Border Padding="8" HeightRequest="220">
                <lvc:CartesianChart
                    Series="{Binding DistanceSeries}"
                    XAxes="{Binding DistanceXAxes}"
                    YAxes="{Binding DistanceYAxes}" />
            </Border>

            <!-- Repeat the same Border/CartesianChart block for calories, duration,
                 and average speed once distance is working end to end — each is
                 BuildXChart(buckets) in the same shape as BuildDistanceChart above,
                 just a different AggregateBucket field. Not duplicated here since
                 it's identical code four times; get the first one reviewed, then
                 copy the pattern for the rest. -->

        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

Concrete steps:

1. Add the NuGet package per the
   [LiveCharts2 MAUI installation guide](https://livecharts.dev/docs/maui/2.0.4/overview.installation):
   `LiveChartsCore.SkiaSharpView.Maui` (pulls in `LiveChartsCore` and
   `LiveChartsCore.SkiaSharpView` transitively) to `MyHi.Companion.csproj`.
2. In `MauiProgram.cs`, add `.UseSkiaSharp().UseLiveCharts()` to the
   `MauiAppBuilder` chain, next to the existing `.UseMauiApp<App>()` /
   `.ConfigureFonts(...)` calls.
3. Create `src/MyHi.Companion/Features/Statistics/StatisticsViewModel.cs` with the
   code above, then fill in the `TODO` (the actual `GetAggregatesAsync` call and date
   range).
4. Create `StatisticsPage.xaml` / `.xaml.cs` with the XAML above; register the page
   and ViewModel in `MauiProgram.cs`'s DI container and add a route in
   `AppShell.xaml.cs`, same pattern as every other page in this project.
5. Run against seeded fake data (task 10.5) and confirm the chart renders before
   wiring the real `SqliteStatisticsProvider`.

---

### Task 10.4 — Per-workout speed/heart-rate curves

**Downsampling and gap-flag handling are logic: you write those. Chart wiring is
UI, written for you below.**

Downsampling: `../phase-06-recording-schema/README.md` gives the pattern `WHERE ElapsedSec % $n = 0`. `$n`
has to be derived from the workout's actual length and the target point count (e.g.
`$n = ceil(totalPoints / maxPoints)`), not hardcoded. A 10-minute workout and a
2-hour workout need very different `$n` to both land near `maxPoints`.

Gaps: `WorkoutSample.Flags` bit 0 marks a connection-gap sample (`../phase-06-recording-schema/README.md`).
LiveCharts2 renders a **break** in a line series wherever its value array contains
`null`, but only if the series' element type is nullable (`LineSeries<double?>`, not
`LineSeries<double>`). The conversion from "a row with `Flags & 1 == 1`" to "a `null`
in the `double?[]` passed to the chart" is where this phase's gap-marking requirement
actually gets enforced. Get it wrong, e.g. defaulting to `0` instead of `null` for a
gap row, and the chart silently draws a fabricated flat segment through the dropout,
exactly what the requirement says not to do.

Concrete steps (logic):

1. Define `WorkoutSamplePoint` as a `record` (`int ElapsedSec, double? SpeedKph,
   int? HeartRate`). Nullable on the value fields specifically so a gap row carries
   `null` all the way through to the chart layer without a separate "is this a gap"
   flag the ViewModel has to remember to check.
2. Implement `GetWorkoutSeriesAsync` in `SqliteStatisticsProvider`:
   ```csharp
   public async Task<IReadOnlyList<WorkoutSamplePoint>> GetWorkoutSeriesAsync(
       long workoutRowId, int maxPoints, CancellationToken ct = default)
   {
       // TODO:
       // 1. SELECT COUNT(*) (or MAX(ElapsedSec)) for this WorkoutRowId to know the
       //    series length, so you can compute $n = max(1, totalPoints / maxPoints).
       // 2. SELECT ElapsedSec, SpeedKph, HeartRate, Flags FROM WorkoutSample
       //    WHERE WorkoutRowId = $w AND ElapsedSec % $n = 0 ORDER BY ElapsedSec.
       // 3. Map each row to a WorkoutSamplePoint — where Flags & 1 == 1 (the gap
       //    bit), SpeedKph and HeartRate become null regardless of what's stored in
       //    those columns, not just passed through as-is.
       throw new NotImplementedException();
   }
   ```
3. Test: a synthetic sample set with a deliberate gap in the middle (a few consecutive
   `Flags = 1` rows). Assert the returned points have `null` at exactly those
   positions, not the raw stored values.

UI: chart wiring (written for you):

```csharp
// WorkoutDetailViewModel.cs (excerpt — same structure as StatisticsViewModel)
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

private void BuildSampleCharts(IReadOnlyList<WorkoutSamplePoint> points)
{
    bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
    SKColor speedColor = isDark
        ? new SKColor(0xE4, 0xE4, 0xE6)   // Gray100
        : new SKColor(0x2A, 0x2A, 0x2D);  // Gray900
    SKColor heartRateColor = isDark
        ? new SKColor(0xB8, 0xB8, 0xBC)   // Gray300
        : new SKColor(0x7D, 0x7D, 0x82);  // Gray500

    SampleSeries =
    [
        new LineSeries<double?>
        {
            Name = "Speed (km/h)",
            Values = points.Select(p => p.SpeedKph).ToArray(),
            Stroke = new SolidColorPaint(speedColor) { StrokeThickness = 2 },
            Fill = null,
            GeometrySize = 0,     // no per-point markers on a ~200-point series
            ScalesYAt = 0          // primary Y axis
        },
        new LineSeries<double?>
        {
            Name = "Heart rate (bpm)",
            Values = points.Select(p => (double?)p.HeartRate).ToArray(),
            // Dashed, not a second color — this theme conveys the second series by
            // stroke pattern, same rule as everything else in
            // 04-Monochrome-Theme.md's "Conveying state without color" table.
            Stroke = new SolidColorPaint(heartRateColor)
            {
                StrokeThickness = 2,
                PathEffect = new DashEffect(new float[] { 6, 4 })
            },
            Fill = null,
            GeometrySize = 0,
            ScalesYAt = 1          // secondary Y axis — different units than speed
        }
    ];

    SampleXAxes = [new Axis { Labels = points.Select(p => FormatElapsed(p.ElapsedSec)).ToArray() }];
    SampleYAxes =
    [
        new Axis { Name = "km/h", Position = AxisPosition.Start },
        new Axis { Name = "bpm",  Position = AxisPosition.End }
    ];
}

private static string FormatElapsed(int elapsedSec) => TimeSpan.FromSeconds(elapsedSec).ToString(@"mm\:ss");
```

```xml
<!-- WorkoutDetailPage.xaml (excerpt) -->
<Border Padding="8" HeightRequest="240">
    <lvc:CartesianChart
        Series="{Binding SampleSeries}"
        XAxes="{Binding SampleXAxes}"
        YAxes="{Binding SampleYAxes}" />
</Border>
```

If Phase 00's V3 finding said heart rate is unusable, leave the heart-rate
`LineSeries` out of `SampleSeries` entirely (don't add it to the array with empty
data), same "hidden, not shown as `--`" rule Phase 03/04 already established for the
live dashboard.

Concrete steps:

1. Write and test `GetWorkoutSeriesAsync` (above) first, against a fixture with a
   known gap.
2. Add `BuildSampleCharts` to a new `WorkoutDetailViewModel` (same folder as
   `StatisticsViewModel`), called after `GetWorkoutSeriesAsync` returns.
3. Add the XAML chart block to whichever page shows a single workout's detail. The
   exact file name depends on what Phase 06 named it; adjust to match.
4. Not hardware-gated, but worth doing before moving on: seed a workout with a
   deliberate gap via the fake provider (task 10.5) and confirm visually that the
   line actually breaks rather than sagging flat through the gap.

---

### Task 10.5 — `FakeStatisticsProvider`

**Logic: you write this, short.**

Creates: `src/MyHi.Companion.Core/Statistics/FakeStatisticsProvider.cs`.

Same seam pattern as `FakeTreadmillService` (01b) and `FakeWorkoutHistoryProvider`
(03): implement `IStatisticsProvider` by generating plausible synthetic buckets,
records, and sample points instead of querying SQLite. Register it in
`MauiProgram.cs` first, build the charts in tasks 10.3/10.4 against it, then swap to
`SqliteStatisticsProvider` once 10.1/10.2/10.4's queries are written and reviewed.
One line changes in `MauiProgram.cs`, nothing in the ViewModels or XAML does.

---

## Tests

- Hand-calculate weekly totals for a seeded dataset and compare
- Timezone: a workout at 23:30 local buckets into the correct **local** day
- DST boundary week aggregates correctly
- Empty dataset renders without crashing: both the aggregate queries and the charts
  (`ISeries[]` with an empty `Values` array should render an empty chart, not throw)
- 5,000-workout dataset: aggregate queries under 200 ms
- `GetWorkoutSeriesAsync` on a fixture with a deliberate gap returns `null` at exactly
  the flagged positions, not the raw stored values

## Acceptance

- [ ] Manual calculations match for daily, weekly, monthly
- [ ] No stored derived values anywhere in the schema
- [ ] Cross-workout trend chart renders against `FakeStatisticsProvider`, then against
      real seeded data, matching the hand-calculated totals
- [ ] A workout with a mid-session connection gap renders as a visible break in the
      per-workout chart, never a flat interpolated line
- [ ] Heart rate curve is absent entirely (not shown empty) if Phase 00's V3 finding
      marked heart rate unusable
