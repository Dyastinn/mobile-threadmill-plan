# Phase 08 — Settings

**Hardware:** none · **Size:** S · **Blocked by:** Phase 07

---

## Goal

User preferences, persisted.

### Understanding what you're building (read this before the tasks)

**Why two storage mechanisms.** Six settings ("dark mode: on", "units: metric") are
single values that only ever get overwritten in place, not unlike a house key on a
hook by the door. Saved Devices is a growing list of Bluetooth devices, each with its
own name, MAC address, last-seen time, and preferred flag, individually added,
removed, and queried; more of a filing cabinet than a hook. This phase stores the
first group in MAUI `Preferences` (a flat, synchronous key/value store, Android
`SharedPreferences` underneath) and leaves the second group exactly where Phase 06
already put it: the SQLite `Device` table.

**Why not put everything in one place.** The simplest-sounding rule would be "this
app already has SQLite, so put every persisted thing there": one mechanism, one thing
to learn. Concretely, that means a `Settings` table, a schema migration every time a
new toggle is added, and an async database round trip every time `SettingsViewModel`
reads or writes a boolean the instant a `Switch` flips. Real latency and real
ceremony for data that's fundamentally just "hold six named values." The opposite
naive rule, keeping Saved Devices in `Preferences` too as one JSON blob under a
single key, is wrong in the other direction. Adding one device means deserializing
the whole blob, appending, and reserializing everything back; there's no way to
update just one device's `LastSeenUtc` without touching every other device's data,
and two writes racing (auto-reconnect updating `LastSeenUtc` while the settings page
happens to be open) can silently clobber each other. SQLite exists specifically to
make per-row updates and concurrent access safe; a hand-rolled JSON blob just
reinvents a worse version of exactly that. Splitting the two isn't extra complexity
for its own sake. Each half is genuinely simpler than forcing the other mechanism
onto it would be.

**The pattern, named plainly.** `IPreferencesStore` (task 8.1) is the same **seam**
you already built in Phase 01b for `ITreadmillService`: a small interface standing
between logic (`AppSettingsService`) and a real platform API (`Preferences`) that
`Core` isn't allowed to reference directly. The cost is the same kind, just smaller
in this phase: one interface, one thin wrapper (`MauiPreferencesStore`), one fake
(`FakePreferencesStore`) for tests, a handful of extra files for four method
signatures. The payoff is the same shape too: `AppSettingsServiceTests` (task 8.4)
can assert "an unrecognized theme string falls back to `System`, never throws"
without an Android runtime anywhere in the loop. Notice where this project *doesn't*
draw the same line: it doesn't build a matching seam over SQLite for Saved Devices in
this phase. The `Device` table already goes through a repository from Phase 06, its
own seam, built when Phase 06 actually needed one. A seam earns its keep only where
something on the other side of it genuinely needs to be swapped or faked for a test,
not as a default habit applied to every dependency in the app.

## Learning goals

- The **seam pattern again, one layer down**: `Preferences` (a MAUI/Android type) can't
  be referenced from `MyHi.Companion.Core`, same rule you already hit with
  `MainThread.BeginInvokeOnMainThread` in Phase 01b. This phase draws the seam as a
  tiny `IPreferencesStore` interface, so the actual setting logic (defaults, enum
  parsing, "never throw on a corrupted value") lives in `Core` and gets a real xUnit
  test against a fake store. The app project supplies the one class that's a thin
  wrapper over the real `Preferences` API.
- Two-way data binding on `Switch.IsToggled` and `Picker.SelectedItem`: the same
  `{Binding X}` idea from `docs/learning/00-What-Is-Maui.md`, but now the value flows
  *back* from the control into the ViewModel, not just out to the screen.
- `partial void On<Property>Changed(...)`, the `[ObservableProperty]`-generated hook
  you already used once in `HomeViewModel.OnSelectedDestinationChanged`. Here you'll
  use it on five properties in a row, each one writing straight through to storage the
  instant the user flips a switch (no separate "Save" button, a deliberate choice
  worth noticing, not an accident).

## Storage decision

Scalar settings live in **MAUI `Preferences`** (Android `SharedPreferences`), not
SQLite: synchronous access, no async ceremony in ViewModels, no database hit at startup
for six booleans.

**Exception:** *Saved Devices* is a collection with a lifecycle and lives in SQLite
(the `Device` table from `14-Database.md`, owned by the Phase 02/06 connection flow).
It does **not** get a control on the Settings page built in this phase. There's
nothing here to manage yet (no "forget this device" UI is in scope), so it's listed in
the table below for completeness only.

| Setting | Store | Default |
|---------|-------|---------|
| Auto-reconnect | Preferences | true |
| Keep screen awake during workout | Preferences | true |
| Dark mode | Preferences | system |
| Units (metric / imperial) | Preferences | metric |
| Voice announcements | Preferences | off |
| Dashboard layout | Preferences (string) | "Default" |
| Saved devices | SQLite (`Device` table) | — |

## The one rule that matters

**Store metric always; convert at display time only.** The `Units` setting built in
this phase is only ever a *preference token*; Phase 08 has no dashboard or history
screen to convert. Whichever future screen renders a distance reads
`AppSettingsService.Units` and converts the number it displays; it never writes a
converted value back to `Preferences` or to the database. An imperial toggle that
writes converted values corrupts the dataset permanently and there is no way back.
See `14-Database.md`'s "All measurements are metric" section.

---

## Reference docs

- **`Preferences`**: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/preferences.
  Read this before task 8.2; it's the API `MauiPreferencesStore` wraps. Note the
  platform mapping (Android `SharedPreferences`) and that values are typed per-call
  (`Get<T>`/`Set<T>` with a required default), which is exactly why `IPreferencesStore`
  below only needs `bool`/`string` overloads: every setting in this phase reduces to
  one of those two.
- **Dependency injection in .NET MAUI**: https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/dependency-injection.
  Same doc Phase 01b pointed at; task 8.5 registers three more things in
  `MauiProgram.cs` following the pattern already there.
- **Data binding and MVVM**: https://learn.microsoft.com/en-us/dotnet/maui/xaml/fundamentals/mvvm.
  Read the two-way binding section before task 8.4; `Switch.IsToggled` and
  `Picker.SelectedItem` both default to `TwoWay` on a bindable control, which is what
  makes flipping a switch on screen update the ViewModel property directly.
- **MVVM source generators overview** (`[ObservableProperty]`):
  https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/overview.
  Re-read the partial-method-hook section; task 8.3 uses `On<Property>Changed` five
  times.
- **Theming (`AppThemeBinding`, `Application.Current.UserAppTheme`)**:
  https://learn.microsoft.com/en-us/dotnet/maui/user-interface/theming. Only needed
  for the optional bonus in task 8.3 that actually flips the app's light/dark theme
  live; not required for this phase's acceptance criteria.
- **`docs/learning/04-Monochrome-Theme.md`**: the token/style reference for task 8.4's
  XAML. Every color in that page comes from here; nothing is invented.

---

## Walkthrough

### 8.1 — The seam: `IPreferencesStore`

Creates: `src/MyHi.Companion.Core/Settings/IPreferencesStore.cs`.

A tiny interface so the setting logic in 8.2 can be unit-tested without touching
Android `SharedPreferences`, same reasoning as `ITreadmillService` in Phase 01b, just
much smaller.

```csharp
namespace MyHi.Companion.Core.Settings;

public interface IPreferencesStore
{
    bool GetBool(string key, bool defaultValue);
    void SetBool(string key, bool value);
    string GetString(string key, string defaultValue);
    void SetString(string key, string value);
}
```

This is small enough to type in directly rather than being handed a skeleton. There's
no design decision left to make, it's four method signatures.

### 8.2 — `AppSettingsService` (the logic — you write this)

Creates: `src/MyHi.Companion.Core/Settings/AppSettingsService.cs` and
`src/MyHi.Companion.Core/Settings/AppThemePreference.cs` +
`MeasurementUnits.cs` (two small enums).

**Naming note:** call the theme enum `AppThemePreference`, not `AppTheme`. MAUI
already has `Microsoft.Maui.ApplicationModel.AppTheme` (`Unspecified`/`Light`/`Dark`),
and a same-named type in `Core` would collide/confuse once both are `using`d in the
same file (task 8.3 needs both).

```csharp
namespace MyHi.Companion.Core.Settings;

public enum AppThemePreference { System, Light, Dark }
```

```csharp
namespace MyHi.Companion.Core.Settings;

public enum MeasurementUnits { Metric, Imperial }
```

The service itself: the shape, not the implementation.

```csharp
namespace MyHi.Companion.Core.Settings;

public sealed class AppSettingsService(IPreferencesStore store)
{
    private const string AutoReconnectKey = "settings.auto_reconnect";
    private const string KeepScreenAwakeKey = "settings.keep_screen_awake";
    private const string ThemeKey = "settings.theme";
    private const string UnitsKey = "settings.units";
    private const string VoiceAnnouncementsKey = "settings.voice_announcements";
    private const string DashboardLayoutKey = "settings.dashboard_layout";

    public bool AutoReconnect
    {
        get => store.GetBool(AutoReconnectKey, defaultValue: true);
        set => store.SetBool(AutoReconnectKey, value);
    }

    // TODO: KeepScreenAwake — same shape as AutoReconnect, default true
    // TODO: VoiceAnnouncements — same shape, default false

    public AppThemePreference Theme
    {
        get
        {
            // TODO: read the stored string (default "System"), Enum.TryParse it into
            // AppThemePreference, and fall back to AppThemePreference.System for
            // anything that doesn't parse — a future app version could store a value
            // this version doesn't recognize; never throw on that, just fall back
            throw new NotImplementedException();
        }
        set => store.SetString(ThemeKey, value.ToString());
    }

    // TODO: Units — same TryParse-with-fallback pattern as Theme, default MeasurementUnits.Metric

    public string DashboardLayout
    {
        get => store.GetString(DashboardLayoutKey, "Default");
        set => store.SetString(DashboardLayoutKey, value);
    }
}
```

Concrete steps:
1. Write the two enums.
2. Write `AppSettingsService` filling in the three `TODO`s, following the pattern the
   `AutoReconnect` and `DashboardLayout` properties already show.
3. Build just `Core` to confirm it compiles standalone:
   ```powershell
   dotnet build src/MyHi.Companion.Core/MyHi.Companion.Core.csproj
   ```

### 8.3 — `MauiPreferencesStore` (the wrapper — you write this)

Creates: `src/MyHi.Companion/Features/Settings/MauiPreferencesStore.cs`.

This one *is* MAUI-flavoured, so it lives in the app project, not `Core`. It's mostly
boilerplate: two of the four methods below to prove you've got the pattern, but no
new decisions to make.

```csharp
using MyHi.Companion.Core.Settings;

namespace MyHi.Companion.Features.Settings;

public sealed class MauiPreferencesStore : IPreferencesStore
{
    public bool GetBool(string key, bool defaultValue) =>
        Preferences.Default.Get(key, defaultValue);

    public void SetBool(string key, bool value) =>
        Preferences.Default.Set(key, value);

    // TODO: GetString / SetString — same one-line delegation to Preferences.Default
}
```

Also creates: `src/MyHi.Companion.Tests/Settings/FakePreferencesStore.cs`, an
in-memory `IPreferencesStore` for the tests in 8.4, same idea as
`FakeTreadmillService` from Phase 01b but much smaller:

```csharp
namespace MyHi.Companion.Tests.Settings;

public sealed class FakePreferencesStore : IPreferencesStore
{
    private readonly Dictionary<string, object> _values = [];

    public bool GetBool(string key, bool defaultValue) =>
        _values.TryGetValue(key, out var v) ? (bool)v : defaultValue;

    public void SetBool(string key, bool value) => _values[key] = value;

    // TODO: GetString / SetString — same dictionary-backed pattern
}
```

### 8.4 — Unit tests for `AppSettingsService`

Creates: `src/MyHi.Companion.Tests/Settings/AppSettingsServiceTests.cs`.

This is the phase's real automated coverage. The manual "change every setting,
force-stop, relaunch" test below still matters, but it can't run in CI.

Concrete steps:
1. `[Fact]`: a fresh `AppSettingsService` over an empty `FakePreferencesStore` returns
   every documented default (`AutoReconnect == true`, `Theme == AppThemePreference.System`,
   `Units == MeasurementUnits.Metric`, etc.).
2. `[Fact]`: setting `Units = MeasurementUnits.Imperial` then reading it back returns
   `Imperial`; setting it back to `Metric` and reading again returns `Metric`. This is
   the "round-trip through imperial and back" test from the table below, proved against
   the *setting itself* rather than against workout data (there isn't any in this
   phase).
3. `[Fact]`: manually write an unrecognized string directly into the fake store under
   the theme key (`store.SetString("settings.theme", "Purple")`), then read
   `AppSettingsService.Theme` through a *new* instance and assert it comes back
   `AppThemePreference.System`, not an exception. This is what "fall back, never throw"
   in task 8.2 is actually protecting against: a future version writing a value this
   one won't recognize.
4. `dotnet test src/MyHi.Companion.Tests`: all green, including every prior phase's
   tests (regression).

### 8.5 — `SettingsViewModel` (the logic — you write this)

Creates: `src/MyHi.Companion/Features/Settings/SettingsViewModel.cs`.

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using MyHi.Companion.Core.Settings;
using MyHi.Companion.Features.Shared;

namespace MyHi.Companion.Features.Settings;

public sealed partial class SettingsViewModel : BaseViewModel
{
    private readonly AppSettingsService _settings;

    public SettingsViewModel(AppSettingsService settings)
    {
        _settings = settings;
        // TODO: initialize every [ObservableProperty] backing field below from
        // _settings's current value — this runs once, in the constructor, before
        // the page's first Appearing
    }

    public IReadOnlyList<AppThemePreference> ThemeOptions { get; } = Enum.GetValues<AppThemePreference>();
    public IReadOnlyList<MeasurementUnits> UnitOptions { get; } = Enum.GetValues<MeasurementUnits>();

    // Placeholder options — Phase 11 (Split Screen) is what actually defines what
    // "Compact" means. This picker only proves the setting round-trips for now.
    public IReadOnlyList<string> DashboardLayoutOptions { get; } = ["Default", "Compact", "Detailed"];

    [ObservableProperty]
    private bool autoReconnect;

    // TODO: keepScreenAwake, voiceAnnouncements — same [ObservableProperty] shape

    [ObservableProperty]
    private AppThemePreference selectedTheme;

    // TODO: selectedUnits (MeasurementUnits), selectedDashboardLayout (string)

    partial void OnAutoReconnectChanged(bool value) => _settings.AutoReconnect = value;

    // TODO: OnKeepScreenAwakeChanged, OnVoiceAnnouncementsChanged, OnSelectedUnitsChanged,
    // OnSelectedDashboardLayoutChanged — same one-line "write straight through" shape

    partial void OnSelectedThemeChanged(AppThemePreference value)
    {
        _settings.Theme = value;

        // Optional bonus, not required for this phase's acceptance: actually flip the
        // running app's theme live. Application.Current!.UserAppTheme takes MAUI's own
        // AppTheme enum (Unspecified/Light/Dark), not the one you just wrote —
        // Application.Current!.UserAppTheme = value switch
        // {
        //     AppThemePreference.Light => Microsoft.Maui.ApplicationModel.AppTheme.Light,
        //     AppThemePreference.Dark => Microsoft.Maui.ApplicationModel.AppTheme.Dark,
        //     _ => Microsoft.Maui.ApplicationModel.AppTheme.Unspecified,
        // };
    }
}
```

Concrete steps:
1. Fill in the constructor's initialization `TODO`.
2. Fill in the two remaining `[ObservableProperty]` groups.
3. Fill in the four remaining `On<Property>Changed` partial methods.
4. Decide whether to uncomment the live-theme bonus. Either answer is fine, just make
   it deliberate.

### 8.6 — UI: `SettingsPage.xaml` (agent-authored)

Per the collaboration model in `../README.md`, this is UI. The full XAML is below,
ready to paste in. It uses only tokens/styles from
`docs/learning/04-Monochrome-Theme.md`: implicit `Border`/`Label`/`Switch`/`Picker`
styles from `Styles.xaml`, plus the keyed `SubHeadline` and `Caption` styles. No new
token is needed.

Creates: `src/MyHi.Companion/Features/Settings/SettingsPage.xaml` and
`SettingsPage.xaml.cs`.

**`SettingsPage.xaml`:**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:settings="clr-namespace:MyHi.Companion.Features.Settings"
             x:Class="MyHi.Companion.Features.Settings.SettingsPage"
             x:DataType="settings:SettingsViewModel"
             Title="Settings">

    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="12">

            <Label Text="Connection" Style="{StaticResource SubHeadline}" HorizontalOptions="Start" />

            <Border Padding="14,10">
                <Grid ColumnDefinitions="*,Auto">
                    <VerticalStackLayout Grid.Column="0" Spacing="2" VerticalOptions="Center">
                        <Label Text="Auto-reconnect" />
                        <Label Text="Reconnect automatically after a dropped connection" Style="{StaticResource Caption}" />
                    </VerticalStackLayout>
                    <Switch Grid.Column="1" IsToggled="{Binding AutoReconnect}" VerticalOptions="Center" />
                </Grid>
            </Border>

            <Border Padding="14,10">
                <Grid ColumnDefinitions="*,Auto">
                    <VerticalStackLayout Grid.Column="0" Spacing="2" VerticalOptions="Center">
                        <Label Text="Keep screen awake" />
                        <Label Text="Prevent the screen from locking during a workout" Style="{StaticResource Caption}" />
                    </VerticalStackLayout>
                    <Switch Grid.Column="1" IsToggled="{Binding KeepScreenAwake}" VerticalOptions="Center" />
                </Grid>
            </Border>

            <Label Text="Appearance" Style="{StaticResource SubHeadline}" HorizontalOptions="Start" Margin="0,12,0,0" />

            <Border Padding="14,10">
                <Grid ColumnDefinitions="*,Auto">
                    <Label Grid.Column="0" Text="Dark mode" VerticalOptions="Center" />
                    <Picker Grid.Column="1"
                            ItemsSource="{Binding ThemeOptions}"
                            SelectedItem="{Binding SelectedTheme}"
                            WidthRequest="140" />
                </Grid>
            </Border>

            <Border Padding="14,10">
                <Grid ColumnDefinitions="*,Auto">
                    <Label Grid.Column="0" Text="Units" VerticalOptions="Center" />
                    <Picker Grid.Column="1"
                            ItemsSource="{Binding UnitOptions}"
                            SelectedItem="{Binding SelectedUnits}"
                            WidthRequest="140" />
                </Grid>
            </Border>

            <Border Padding="14,10">
                <Grid ColumnDefinitions="*,Auto">
                    <Label Grid.Column="0" Text="Dashboard layout" VerticalOptions="Center" />
                    <Picker Grid.Column="1"
                            ItemsSource="{Binding DashboardLayoutOptions}"
                            SelectedItem="{Binding SelectedDashboardLayout}"
                            WidthRequest="140" />
                </Grid>
            </Border>

            <Label Text="Workout" Style="{StaticResource SubHeadline}" HorizontalOptions="Start" Margin="0,12,0,0" />

            <Border Padding="14,10">
                <Grid ColumnDefinitions="*,Auto">
                    <VerticalStackLayout Grid.Column="0" Spacing="2" VerticalOptions="Center">
                        <Label Text="Voice announcements" />
                        <Label Text="Speak distance and pace milestones during a workout" Style="{StaticResource Caption}" />
                    </VerticalStackLayout>
                    <Switch Grid.Column="1" IsToggled="{Binding VoiceAnnouncements}" VerticalOptions="Center" />
                </Grid>
            </Border>

            <Label Text="{Binding StatusMessage}"
                   Style="{StaticResource Caption}"
                   IsVisible="{Binding StatusMessage, Converter={StaticResource NotNullConverter}}"
                   Margin="0,8,0,0" />

        </VerticalStackLayout>
    </ScrollView>

</ContentPage>
```

`NotNullConverter` is already registered in `App.xaml` (Phase 00); no new converter
needed.

**`SettingsPage.xaml.cs`**: identical pattern to every other page in the project
(`HomePage.xaml.cs`).

```csharp
namespace MyHi.Companion.Features.Settings;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
```

### 8.7 — Wire it up

Touches: `MauiProgram.cs`, `AppShell.xaml.cs`, `Features/Shared/HomeViewModel.cs`.

Concrete steps:
1. In `MauiProgram.cs`, near the other `AddSingleton`/`AddTransient` calls, add:
   ```csharp
   // ---- Settings ----
   builder.Services.AddSingleton<IPreferencesStore, MauiPreferencesStore>();
   builder.Services.AddSingleton<AppSettingsService>();
   builder.Services.AddTransient<SettingsViewModel>();
   builder.Services.AddTransient<SettingsPage>();
   ```
   `AppSettingsService` is a singleton: it holds no per-screen state, it's a thin
   typed wrapper over `Preferences`, which is itself effectively global.
2. Add the `using` directives (`MyHi.Companion.Core.Settings;`,
   `MyHi.Companion.Features.Settings;`) if they're not already present.
3. In `AppShell.xaml.cs`, add `Routing.RegisterRoute("settings", typeof(SettingsPage));`
   next to the existing routes.
4. In `HomeViewModel.cs`, add a `NavDestination` to the `Destinations` list so the
   screen is actually reachable:
   ```csharp
   new NavDestination("Settings", "Preferences: connection, appearance, units, voice", "settings"),
   ```
5. Build the app project and run it (`dotnet build src/MyHi.Companion/MyHi.Companion.csproj -f net10.0-android`),
   then launch on the emulator or device, open Settings, flip every toggle, and
   force-stop/relaunch to confirm persistence by eye before calling the phase done.

---

## Tests

- `AppSettingsServiceTests` (task 8.4): defaults, round-trip, corrupt-value fallback.
  Automated, runs in CI.
- Change every setting, force-stop, relaunch: all persist. **`[HUMAN]`**, on-device.
- Units toggle round-trips through the setting (covered by 8.4's test 2) without
  touching the database. There is no database write path from `SettingsViewModel`
  at all in this phase, which is itself the proof.

## Acceptance

- [ ] All settings survive force-stop
- [ ] No unit conversion ever touches the database
- [ ] `AppSettingsServiceTests` and every prior phase's tests pass, zero warnings
