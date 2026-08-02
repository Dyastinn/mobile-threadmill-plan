# Phase 03 — Live Dashboard + Contribution Graph

> See `../README.md` for the collaboration model: you write the code, the agent
> explains concepts up front and reviews after.

**Hardware:** none for development (`FakeTreadmillService`), yes to verify at the end
**Size:** M · **Blocked by:** Phase 01b only (not 01a)

---

## Goal

Build the app's real home/dashboard screen: live treadmill metrics (built entirely
against `ITreadmillService`, developed and tested with `FakeTreadmillService`, real
hardware only to verify at the end) **and** a GitHub-style workout "contribution
graph" at the top of the screen, using a fake data source until Phase 06 provides
real workout history.

This screen replaces Phase 00's `HomePage` as the app's actual front door. Phase 00's
six diagnostic screens don't disappear (they're still useful for debugging), but
they stop being the first thing the app shows. Exactly how they're relocated, e.g.
into a hidden diagnostics menu, is a small decision to make together when we get
there, not blocking for this phase.

## Learning goals

- Building the **same seam/fake pattern from Phase 01b**, applied to a second,
  unrelated problem (workout history instead of BLE). The point is to notice it's a
  repeatable technique, not a one-off trick.
- A `CollectionView` with a **grid layout** (`GridItemsLayout`), for laying out many
  small uniform items, different from the single-column lists you've seen so far
  (Scan screen, Notification Log).
- Throttling a high-frequency event stream for UI binding, so updates don't outpace
  what a screen can usefully render.
- Deciding what belongs in `Core` vs. the app project. You'll make this call
  yourself this time, using the same test the agent applied in Phase 01b: does this
  code reference MAUI/Android at all?
- This is also the first phase where you'll see the project's existing MVVM shape
  (`BaseViewModel` + CommunityToolkit.Mvvm's `[ObservableProperty]`/`[RelayCommand]`,
  already used by every Phase 00 screen; see `ScanViewModel.cs` for a working
  example) applied to a *new* feature rather than read secondhand. Worth comparing
  your `DashboardViewModel` against `ScanViewModel.cs` once both exist.

## Reference docs

- `src/MyHi.Companion.Core/Treadmill/ITreadmillService.cs` (moved here in Phase 01b)
- `../../05-FTMS-Protocol.md` §4 (fields), §4a (heart rate)
- `../phase-00-probe-app/PHASE-00-FINDINGS.md`: notification rate, flags observed,
  V3 heart rate verdict
- `docs/learning/02-Glossary.md`: add `GridItemsLayout` and anything else new here
  as you go
- `docs/learning/04-Monochrome-Theme.md`: every token/style this phase's XAML uses
  is defined there; read it before task 3.3 if you haven't already
- **`CollectionView` overview**: https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview/
- **`CollectionView` layout (`GridItemsLayout`, `Span`)**: https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview/layout.
  Read this before task 3.7; `Span="7"` is what turns a flat list into rows of 7
- **How to use `DateOnly`**: https://learn.microsoft.com/en-us/dotnet/standard/datetime/how-to-use-dateonly-timeonly.
  Task 3.5's `IWorkoutHistoryProvider` shape uses `DateOnly`, not `DateTimeOffset`,
  because a contribution-graph cell is a calendar day, not an instant

---

## Part 1 — Live metrics

### Understanding what you're building (read this before the tasks)

**The problem.** The treadmill can push `SampleReceived` notifications faster than
a screen full of `Border` tiles can usefully redraw (Phase 00's findings recorded
the actual notification rate; see `PHASE-00-FINDINGS.md`). It's the same mismatch
as a car's speedometer needle: the sensor samples hundreds of times a second, but
the needle only moves smoothly 10-20 times a second, and a human eye can't use
information faster than that anyway. `DashboardViewModel` sits between a fast,
uneven data source and a UI where five bound properties (`SpeedKph`,
`DistanceMeters`, `Calories`, `ElapsedSeconds`, `HeartRate`) all repaint tiles.
Every unnecessary update is a wasted layout pass on a phone screen, and the
Implementation requirements section below is explicit about the cost: "Above
that, MAUI janks in split screen for no visible benefit."

**Why not just render every sample as it arrives?** The naive version (bind
directly and let every `SampleReceived` event push straight into the observable
properties) is what task 3.2's skeleton starts from, then explicitly guards
against with one `if` check. The fix isn't a timer, a queue, or a reactive
pipeline; it's the plainest possible throttle: remember `_lastUiUpdateUtc`, and if
less than ~250 ms have passed, skip the update and let the next sample (a
quarter-second later, at most) carry the fresher number instead. That single
comparison is enough to guarantee both things this phase actually needs: no more
than 4 updates a second, and the *displayed* value is never stale, because a
skipped sample is simply superseded by the next one rather than queued up behind
it. Reaching for a dedicated timer object or a full reactive `Sample`/`Throttle`
operator here would solve a problem this app doesn't have: there's no backlog to
drain, no requirement to batch multiple samples together, just "don't repaint
faster than useful." The skeleton's own comment in task 3.2, "think about whether
a plain 'skip if too soon' check already satisfies both guarantees," is the whole
design decision.

**The pattern, named plainly.** What's being applied here is specifically
**throttle**, not its cousin **debounce**, and the distinction matters for picking
the right one. Debounce waits for a stream to go *quiet* before firing (useful for
a search box: wait until the user stops typing). Throttle fires at a bounded,
regular rate *while* the stream keeps producing, always carrying the latest value.
That's what a continuously-changing number like treadmill speed needs; debouncing
it would mean the UI barely updates at all during a steady, ongoing workout, since
the stream never really goes quiet. The cost of throttling here is one extra field
(`_lastUiUpdateUtc`) and one comparison per event, genuinely cheap. The payoff is
a UI that stays responsive under a notification rate the phone can't (and doesn't
need to) fully render. The pattern isn't needed everywhere, though: Phase 01b's
`FakeTreadmillService` raises samples roughly once a second by design (its own
`PeriodicTimer` loop), already under the 4 Hz ceiling. No throttle was needed
there because the *source*, not just the consumer, was already UI-safe. It only
earns its place in Part 1 because the real device's notification rate isn't
guaranteed to be.

### Your tasks

- Speed, distance, calories, elapsed time, machine status, on a new dashboard page
- Heart rate **only if V3 (Phase 00 findings) said usable**
- Connection indicator
- **Fields the device does not actually send are hidden, not shown as `--`.** A row
  of dashes is a promise the app can't keep.

### Walkthrough

**3.1: Where this lives**

1. Create the folder `src/MyHi.Companion/Features/Dashboard/`.
2. Files you'll add in this part: `DashboardPage.xaml` + `.xaml.cs` (UI, given in
   full below), `DashboardViewModel.cs` (yours to write, spec below), and
   `DashboardConverters.cs` (UI-support code, given in full below).
3. Confirm `ITreadmillService` has actually moved to
   `src/MyHi.Companion.Core/Treadmill/ITreadmillService.cs` with namespace
   `MyHi.Companion.Core.Treadmill` (Phase 01b's task 1b.1) and `FakeTreadmillService`
   is registered in `MauiProgram.cs` before starting. Every binding below assumes
   that seam already exists.

**3.2: `DashboardViewModel`: the spec**

- Depends on `ITreadmillService` (constructor-injected); nothing else for Part 1.
- Subscribes to `SampleReceived` and `ConnectionStateChanged` in its constructor.
- One `[ObservableProperty]` per metric the UI binds to directly: `SpeedKph`,
  `DistanceMeters` (store metres; task 3.3's XAML converts to km for display only),
  `Calories`, `ElapsedSeconds`, `HeartRate` (nullable, matching `TreadmillSample`'s
  own nullability) plus `ConnectionState`.
- One `bool` per metric for the "hidden not `--`" rule: `ShowsSpeed`, `ShowsDistance`,
  `ShowsCalories`, `ShowsElapsedTime`, `ShowsHeartRate`. Until Phase 01a's
  `CapabilityTracker` exists, the honest rule available today is "shown if the latest
  sample actually carries a non-null value for it": a field genuinely absent from
  every packet stays hidden; once 01a lands, swap this for the accumulated-flags
  check the Goal section already describes. The XAML doesn't change either way.
- Heart rate additionally needs a manual override for the V3 verdict in
  `PHASE-00-FINDINGS.md`: "Unusable" or "Marginal" means `ShowsHeartRate` is
  hard-`false` regardless of what the sample contains; only "Usable" lets the
  presence-based rule above apply.
- The throttle is **your design decision**, per the Implementation requirements
  below. Whatever mechanism you pick has to guarantee two things: no bound property
  updates more than 4 times a second, and the *most recent* sample always wins (never
  hold on to a stale "last known good" speed while a fresher one waits behind it,
  that's actively misleading mid-workout, not just slow).

Skeleton, shape only:

```csharp
namespace MyHi.Companion.Features.Dashboard;

public sealed partial class DashboardViewModel : BaseViewModel
{
    private readonly ITreadmillService _treadmill;
    private DateTimeOffset _lastUiUpdateUtc = DateTimeOffset.MinValue;

    public DashboardViewModel(ITreadmillService treadmill)
    {
        _treadmill = treadmill;
        _treadmill.SampleReceived += OnSampleReceived;
        _treadmill.ConnectionStateChanged += OnConnectionStateChanged;
    }

    [ObservableProperty] private ConnectionState connectionState;
    [ObservableProperty] private double? speedKph;
    [ObservableProperty] private double? distanceMeters;
    [ObservableProperty] private int? calories;
    [ObservableProperty] private int? elapsedSeconds;
    [ObservableProperty] private int? heartRate;

    [ObservableProperty] private bool showsSpeed;
    [ObservableProperty] private bool showsDistance;
    [ObservableProperty] private bool showsCalories;
    [ObservableProperty] private bool showsElapsedTime;
    [ObservableProperty] private bool showsHeartRate;

    private void OnSampleReceived(object? sender, TreadmillSample sample)
    {
        // TODO: throttle — if less than ~250ms since _lastUiUpdateUtc, return
        // without updating (the next sample a quarter-second later carries fresher
        // data anyway; think about whether a plain "skip if too soon" check already
        // satisfies both guarantees above before reaching for a timer/queue)

        // TODO: map sample.SpeedKph -> SpeedKph, sample.DistanceMeters ->
        // DistanceMeters, etc. Set each Shows* flag per the design note above.

        _lastUiUpdateUtc = DateTimeOffset.UtcNow;
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        ConnectionState = e.State;
    }
}
```

**3.3: The dashboard page (UI, written in full)**

Two display-formatting converters first
(`src/MyHi.Companion/Features/Dashboard/DashboardConverters.cs`), pure formatting,
not the throttle/mapping logic above, so these are given complete:

```csharp
using System.Globalization;

namespace MyHi.Companion.Features.Dashboard;

/// <summary>Metres -> kilometres for display. Storage/binding upstream stays metres.</summary>
public sealed class MetersToKilometersConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double meters ? meters / 1000.0 : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Elapsed seconds -> "mm:ss" for the elapsed-time tile.</summary>
public sealed class SecondsToClockConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int seconds ? TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss") : "--:--";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

Register both in `src/MyHi.Companion/App.xaml` next to the existing converters (and
add `xmlns:dash="clr-namespace:MyHi.Companion.Features.Dashboard"` to its root tag,
alongside the existing `xmlns:shared`):
```xml
<dash:MetersToKilometersConverter x:Key="MetersToKilometersConverter" />
<dash:SecondsToClockConverter x:Key="SecondsToClockConverter" />
```
If Phase 02's task 2.7 already registered `ConnectionStateToConnectedConverter`,
reuse it below as-is; it lives in `shared:`, not `dash:`.

`DashboardPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:dash="clr-namespace:MyHi.Companion.Features.Dashboard"
             x:Class="MyHi.Companion.Features.Dashboard.DashboardPage"
             x:DataType="dash:DashboardViewModel"
             Title="Dashboard">

    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="20">

            <!-- Part 2 (task 3.8) replaces this comment with the contribution graph -->

            <Grid ColumnDefinitions="Auto,Auto" ColumnSpacing="8" HorizontalOptions="Center">
                <Grid Grid.Column="0" WidthRequest="12" HeightRequest="12">
                    <Ellipse Stroke="{AppThemeBinding Light={StaticResource ColorBorderLight}, Dark={StaticResource ColorBorderDark}}"
                             StrokeThickness="1.5" Fill="Transparent" WidthRequest="12" HeightRequest="12" />
                    <Ellipse Fill="{AppThemeBinding Light={StaticResource ColorTextPrimaryLight}, Dark={StaticResource ColorTextPrimaryDark}}"
                             WidthRequest="12" HeightRequest="12"
                             IsVisible="{Binding ConnectionState, Converter={StaticResource ConnectionStateToConnectedConverter}}" />
                </Grid>
                <Label Grid.Column="1" Text="{Binding ConnectionState}" Style="{StaticResource Caption}" VerticalOptions="Center" />
            </Grid>

            <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,Auto" ColumnSpacing="12" RowSpacing="12">

                <Border Grid.Row="0" Grid.Column="0" Padding="16,12" IsVisible="{Binding ShowsSpeed}">
                    <VerticalStackLayout Spacing="2">
                        <Label Text="{Binding SpeedKph, StringFormat='{0:F1}'}" Style="{StaticResource MetricValue}" />
                        <Label Text="km/h" Style="{StaticResource MetricLabel}" />
                    </VerticalStackLayout>
                </Border>

                <Border Grid.Row="0" Grid.Column="1" Padding="16,12" IsVisible="{Binding ShowsDistance}">
                    <VerticalStackLayout Spacing="2">
                        <Label Text="{Binding DistanceMeters, Converter={StaticResource MetersToKilometersConverter}, StringFormat='{0:F2}'}" Style="{StaticResource MetricValue}" />
                        <Label Text="km" Style="{StaticResource MetricLabel}" />
                    </VerticalStackLayout>
                </Border>

                <Border Grid.Row="1" Grid.Column="0" Padding="16,12" IsVisible="{Binding ShowsCalories}">
                    <VerticalStackLayout Spacing="2">
                        <Label Text="{Binding Calories}" Style="{StaticResource MetricValue}" />
                        <Label Text="kcal" Style="{StaticResource MetricLabel}" />
                    </VerticalStackLayout>
                </Border>

                <Border Grid.Row="1" Grid.Column="1" Padding="16,12" IsVisible="{Binding ShowsElapsedTime}">
                    <VerticalStackLayout Spacing="2">
                        <Label Text="{Binding ElapsedSeconds, Converter={StaticResource SecondsToClockConverter}}" Style="{StaticResource MetricValue}" />
                        <Label Text="elapsed" Style="{StaticResource MetricLabel}" />
                    </VerticalStackLayout>
                </Border>

            </Grid>

            <Border Padding="16,12" IsVisible="{Binding ShowsHeartRate}">
                <VerticalStackLayout Spacing="2">
                    <Label Text="{Binding HeartRate}" Style="{StaticResource MetricValue}" />
                    <Label Text="bpm" Style="{StaticResource MetricLabel}" />
                </VerticalStackLayout>
            </Border>

        </VerticalStackLayout>
    </ScrollView>

</ContentPage>
```

`DashboardPage.xaml.cs`:
```csharp
namespace MyHi.Companion.Features.Dashboard;

public partial class DashboardPage : ContentPage
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
```

Two things worth understanding, even though you didn't write this file:
- Every metric tile is the exact `Border`/`MetricValue`/`MetricLabel` pattern from
  `docs/learning/04-Monochrome-Theme.md`'s own example, repeated five times; nothing
  here is a new style.
- `IsVisible="{Binding ShowsSpeed}"` etc. *is* the entire "hidden not `--`"
  implementation on the UI side: the `Border` simply doesn't lay out at all when the
  bound bool is false, no empty space, no dash.

**3.4: Register and route to it**

1. `MauiProgram.cs`: add `builder.Services.AddTransient<DashboardViewModel>();` and
   `builder.Services.AddTransient<DashboardPage>();`, same pattern as every other
   page already registered there.
2. `AppShell.xaml`: add a new `ShellContent` for `dash:DashboardPage` with
   `Route="dashboard"`, placed **first**, before the existing `home` entry. Shell
   shows whichever `ShellContent` is listed first by default, and this phase's Goal
   is for the dashboard to become that front door. The existing `home` entry (Phase
   00's diagnostics menu) stays reachable as the second item for now; exactly how it
   gets relocated later is the "small decision to make together" the Goal section
   already flags, not blocking here. Add
   `xmlns:dash="clr-namespace:MyHi.Companion.Features.Dashboard"` to `AppShell.xaml`'s
   root tag alongside the existing `xmlns:shared`.
3. Build and run against `FakeTreadmillService`. You should land on the dashboard on
   launch and watch metrics move.

**Field visibility** comes from the union of observed `0x2ACD` flag bits (Phase 01a's
capability tracker, once it exists), **not** from `0x2ACC`. That bitmask on this
device over-claims (it advertises incline target setting on a machine with no
incline). Log it; never branch on it. Until Phase 01a lands, it's fine to show
whatever fields `FakeTreadmillService` populates.

**Heart rate source**: `0x2A37` from `180D` if usable; the FTMS field only if `180D`
is dead; **removed from the UI entirely if V3 said unusable.** If V3 said marginal,
keep recording it (Phase 06) and hide it on this screen.

### Implementation requirements

- Notification callbacks from `ITreadmillService` are documented as already
  marshalled to the UI thread at the service boundary (see the interface's doc
  comments). Don't marshal again, and don't assume they aren't if you changed that
  in Phase 01b.
- **Throttle UI updates to at most 4 Hz** even if notifications arrive faster. Above
  that, MAUI janks in split screen for no visible benefit. (How you throttle,
  a timer sampling the latest value, a debounce, something else, is your design
  decision; think about what "throttle" actually needs to guarantee before picking
  one.)
- Format at display time only. **Never convert units before storage**: metric
  always, everywhere below the ViewModel.

### Tests

- Fake service: 10-minute stream renders continuously, no frozen UI
- Sparse-field scenario: absent fields are hidden, present ones render
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
once in Phase 01b for the treadmill connection itself (`ITreadmillService` /
`FakeTreadmillService`). `IWorkoutHistoryProvider` and `FakeWorkoutHistoryProvider`
are that same idea applied to a second, unrelated problem. If Phase 01b's
rig-and-engine reasoning made sense there, it applies here unchanged: swap
"treadmill hardware" for "SQLite workout history," and everything else about
*why* holds.

**Why not wait for Phase 06, and why not build the full GitHub version?** Two
separate "why not simpler" questions sit inside this feature, pulling in opposite
directions. First: why not just leave this part of the dashboard blank until
Phase 06 ships real data? Because the interface costs almost nothing to define
now (`GetDailyCountsAsync(DateOnly, DateOnly)` returning a list of
`DailyWorkoutCount`, task 3.5), and doing so means the widget, the grid layout,
and the "how do lit days look" decisions all get built and reviewed *this* phase
instead of blocking on Phase 06's database work. It's a smaller win than Phase
01b's six-phase unblock, but the same shape of win: one phase's UI work stops
waiting on another phase's I/O work. Second, pulling the other way: why not build
GitHub's actual gradient-by-count coloring now, since the data
(`DailyWorkoutCount.Count` is already an `int`, not just a bool) technically
supports it? Because nothing in this project has asked for that yet. The phase
spec itself says plainly, "**Simplified from GitHub's original**: no gradient by
count... If you want the gradient-by-count version later, that's a good
follow-up... not a requirement now." Here the simpler version (lit/unlit)
genuinely *is* the right amount of complexity, not a corner cut. There's no
demonstrated need for five shades of intensity yet, just "did a workout happen
this day or not."

**The pattern, named plainly.** The gradient decision is a clean example of
**YAGNI** ("You Aren't Gonna Need It"): deliberately not building a feature until
something concrete actually calls for it, rather than speculatively supporting it
"in case." What makes it cheap to defer here specifically: `DailyWorkoutCount`
already carries the full `int Count`, not just a boolean, so
`ContributionDayViewModel`'s `count > 0` mapping (task 3.6) is the only place that
would need to change later. The seam and the data shape underneath it don't need
to be redesigned to add color intensity later, only the one line that decides what
"lit" means. That's what makes deferring it free instead of risky: YAGNI is a bad
trade when skipping a feature now means an expensive redesign later, and a good
one when, as here, the door is already left open at zero cost.

### The feature

A GitHub-style grid showing which days had a workout, at the top of the dashboard,
above the connection status. **Simplified from GitHub's original**: no gradient by
count; each day is either lit (≥1 workout) or unlit (none). If you want the
gradient-by-count version later, that's a good follow-up once the simple version
works and is reviewed, not a requirement now.

### The seam (same pattern as `ITreadmillService`/`FakeTreadmillService`)

Real workout history doesn't exist until Phase 06 (Recording & Schema). Rather than
wait, define the shape you need and fake it:

- **Interface**, in `Core` (it's plain data, no MAUI dependency):
  a method that returns daily workout counts over a date range. Design the exact
  signature yourself. Think about what Phase 06's real SQLite-backed implementation
  will need to accept and return, and what's easiest for a UI to bind against. (Hint:
  look at how `ITreadmillService` shapes its return types: `TreadmillSample` is a
  `readonly record struct`; is a `record` right here too?)
- **Fake implementation**, also in `Core`: generates a plausible several-months of
  synthetic daily counts (some days with 0, some with 1+) so the widget has something
  real to render and look right immediately.
- **Real implementation** arrives in Phase 06, reading `Workout.StartedAtUtc` grouped
  by local day (there's a query pattern for exactly this in `14-Database.md`). The
  dashboard's UI code does not change when this swap happens, only a `MauiProgram.cs`
  registration does.

### The widget

- A reusable view bound to a collection of "day + lit/unlit" data.
- Look into `CollectionView`'s `GridItemsLayout` (`Span="7"` for a GitHub-style
  7-rows-per-week layout, or however you decide to orient it). This is a different
  `ItemsLayout` than the single-column lists you've built so far. Read MAUI's docs on
  `GridItemsLayout` before starting; it's a good excuse to learn a control you
  haven't used yet, rather than something to guess at.
- Roughly the last 3–6 months is a reasonable range to start with. A full year of
  GitHub-style history can come later once you're happy with the layout at a smaller
  size.
- Placed at the very top of the new dashboard page, above the connection indicator
  and live metrics from Part 1.

### Walkthrough

**3.5: The seam, in `Core`**

1. Create `src/MyHi.Companion.Core/History/` (new folder in `Core`, alongside
   `Treadmill/`, `Ftms/`, `Capture/`, `Data/`).
2. `IWorkoutHistoryProvider.cs` — a starting shape, yours to adjust once you've
   thought about it:
   ```csharp
   namespace MyHi.Companion.Core.History;

   public interface IWorkoutHistoryProvider
   {
       Task<IReadOnlyList<DailyWorkoutCount>> GetDailyCountsAsync(
           DateOnly fromLocal, DateOnly toLocal, CancellationToken ct = default);
   }

   /// <summary>One local calendar day and how many workouts started on it.</summary>
   public readonly record struct DailyWorkoutCount(DateOnly Day, int Count);
   ```
   `DateOnly` rather than `DateTimeOffset` because a contribution-graph cell is a
   calendar day, not an instant — it matches the `LocalDay` computed column in
   `14-Database.md`'s query pattern, which Phase 06's real implementation will read
   from. `record struct` mirrors `TreadmillSample`'s own choice (small, copied by
   value) — the hint the task list above already points at.
3. `FakeWorkoutHistoryProvider.cs` — implements the interface, but "make it look
   like a real training history" is still real logic, not a one-liner, so it gets a
   skeleton rather than a full body:
   ```csharp
   namespace MyHi.Companion.Core.History;

   public sealed class FakeWorkoutHistoryProvider : IWorkoutHistoryProvider
   {
       private readonly Random _random = new();

       public Task<IReadOnlyList<DailyWorkoutCount>> GetDailyCountsAsync(
           DateOnly fromLocal, DateOnly toLocal, CancellationToken ct = default)
       {
           // TODO: walk fromLocal..toLocal one day at a time, giving each day a
           // plausible chance of 0 vs. 1+ workouts (a few days a week reads as
           // realistic; every day or no days doesn't) and return the list.
           throw new NotImplementedException();
       }
   }
   ```
4. Register in `MauiProgram.cs`:
   `builder.Services.AddSingleton<IWorkoutHistoryProvider, FakeWorkoutHistoryProvider>();`
   — same DI-slot pattern as `ITreadmillService`/`FakeTreadmillService`, so swapping
   in Phase 06's real SQLite-backed implementation later touches only this one line.

**3.6 — Wire it into the ViewModel**

1. Add `IWorkoutHistoryProvider` as a second constructor dependency on
   `DashboardViewModel`.
2. Add `public ObservableCollection<ContributionDayViewModel> ContributionDays { get; } = [];`
   — `ContributionDayViewModel` is a tiny binding-facing wrapper (full type in 3.7),
   kept separate from `DailyWorkoutCount` so `Core` never has to know what "lit" means
   on screen.
3. Add `[RelayCommand] private async Task LoadContributionDaysAsync()` that calls
   `_history.GetDailyCountsAsync(...)` for roughly the last 3–6 months, maps each
   `DailyWorkoutCount` to a `ContributionDayViewModel(day, count > 0)`, and populates
   `ContributionDays`. Call it once from the constructor — fire-and-forget is fine
   here, unlike `SampleReceived` there's no ongoing stream to manage.

**3.7 — The widget (UI, written in full)**

`ContributionDayViewModel.cs` — the binding-facing type:
```csharp
namespace MyHi.Companion.Features.Dashboard;

public sealed record ContributionDayViewModel(DateOnly Day, bool IsLit);
```

`ContributionGraphView.xaml` — a reusable `ContentView` exposing its own bindable
`ItemsSource`, the same pattern `CollectionView` itself uses for its own
`ItemsSource`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:dash="clr-namespace:MyHi.Companion.Features.Dashboard"
             x:Class="MyHi.Companion.Features.Dashboard.ContributionGraphView"
             x:Name="Root">

    <CollectionView x:DataType="dash:ContributionGraphView"
                     ItemsSource="{Binding Source={x:Reference Root}, Path=ItemsSource}"
                     HorizontalOptions="Center"
                     IsScrollEnabled="False"
                     HeightRequest="260">
        <CollectionView.ItemsLayout>
            <GridItemsLayout Orientation="Vertical" Span="7" HorizontalItemSpacing="4" VerticalItemSpacing="4" />
        </CollectionView.ItemsLayout>
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="dash:ContributionDayViewModel">
                <Grid WidthRequest="14" HeightRequest="14">
                    <Border StrokeThickness="0" StrokeShape="RoundRectangle 3"
                            BackgroundColor="{AppThemeBinding Light={StaticResource ColorContributionUnlitLight}, Dark={StaticResource ColorContributionUnlitDark}}" />
                    <Border StrokeThickness="0" StrokeShape="RoundRectangle 3"
                            BackgroundColor="{AppThemeBinding Light={StaticResource ColorContributionLitLight}, Dark={StaticResource ColorContributionLitDark}}"
                            IsVisible="{Binding IsLit}" />
                </Grid>
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>

</ContentView>
```

`ContributionGraphView.xaml.cs`:
```csharp
using System.Collections;

namespace MyHi.Companion.Features.Dashboard;

public partial class ContributionGraphView : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(ContributionGraphView));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ContributionGraphView()
    {
        InitializeComponent();
    }
}
```

A few things worth understanding, not just pasting:
- `Span="7"` on `GridItemsLayout` (docs linked in Reference docs above) is what turns
  a flat list into a 7-wide grid — every 7th item wraps to a new row. Feeding it days
  in chronological order (oldest first) produces rows of 7 consecutive days, which is
  the "simplified, no gradient" contribution graph this phase asks for. A true
  GitHub-style layout (columns = weeks, scrolling horizontally) is a fair follow-up
  once this version is reviewed — not required now.
- Each cell is two stacked `Border`s rather than one `Border` whose color changes via
  a converter — same reasoning as Phase 02's connection dot: the `AppThemeBinding`
  colors stay static XAML resolved once per theme, and only `IsVisible` is dynamic.
  Keeps a value converter out of the picture for something this simple.
- `IsScrollEnabled="False"` on the inner `CollectionView` hands scrolling to the outer
  `ScrollView` in `DashboardPage.xaml` — a `CollectionView` nested inside another
  scrolling container otherwise tends to fight it for the gesture. `HeightRequest="260"`
  is a starting guess for ~3–6 months of days at 7 per row; tune it once you see it
  rendered against your actual date range.

**3.8 — Drop it into the dashboard**

1. In `DashboardPage.xaml` (from task 3.3), replace the
   `<!-- Part 2 (task 3.8) replaces this comment... -->` comment with:
   ```xml
   <dash:ContributionGraphView ItemsSource="{Binding ContributionDays}" />
   ```
   placed above the connection indicator, per "at the very top of the new dashboard
   page" above. The `dash:` namespace is already declared on the page from task 3.3.
2. Build and run — the graph should render a scattering of lit/unlit cells against
   `FakeWorkoutHistoryProvider` immediately, before any real workout has ever been
   recorded.

### Review checkpoint

Before wiring Phase 06's real data source in later: agent reviews the
`IWorkoutHistoryProvider` shape (is it something Phase 06 can actually implement
without redesigning it?), the fake's realism, and the widget's binding approach.

---

## Acceptance

- [ ] Smooth updates for 10 minutes with no UI stall (Part 1)
- [ ] `[HUMAN]` Displayed values match the console at three different speeds
- [ ] No field shows a placeholder for data the device never sends
- [ ] Contribution graph renders against `FakeWorkoutHistoryProvider`, lit days
      visibly distinct from unlit ones
- [ ] `IWorkoutHistoryProvider` lives in `Core`, has no MAUI dependency, and is
      registered in `MauiProgram.cs` the same way `ITreadmillService` is
