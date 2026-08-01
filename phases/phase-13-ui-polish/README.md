# Phase 13 — UI Polish

**Hardware:** none · **Size:** M · **Blocked by:** Phase 12

---

## Goal

This phase is a **polish pass across every screen built in Phases 03/05/08/10/11**,
not a new screen of its own. It doesn't rewrite any of those screens — instead it
delivers a small set of **reusable building blocks** (an empty-state view, a
loading overlay, a typography scale, a haptics call, accessibility examples) that
get dropped into each existing screen at the review checkpoint, once those phases'
real code exists to drop them into.

Per `../README.md`'s collaboration model, **this entire phase is UI/XAML** — the
agent writes the actual code below, you paste it in and wire the bindings to your
ViewModels' real property names. See `../../docs/learning/04-Monochrome-Theme.md`
for the token vocabulary everything below is built from — every snippet here uses
only tokens and styles that already exist in `Colors.xaml`/`Styles.xaml`, or is
explicitly flagged as something to add there.

### Understanding what you're building (read this before the tasks)

**The everyday problem.** Picture furnishing five rooms in a house one at a
time, as each room gets used. The first room needs a door handle, so you design
one for it. The second room needs a door handle too, so — since you're already
in "finish this room" mode — you design another one, slightly different because
you didn't compare notes with the first. By room four you have four different
door handles, none of them interchangeable, and fixing a flaw in one (say, it's
the wrong height for building code) means redoing the fix four separate times.
The alternative — once you notice every room needs the same kind of handle — is
to design one handle and install the same part everywhere.

That's exactly the shape of this phase. Phases 03, 08, 09, and 10 each built a
real screen, and each of those screens independently needs to say "there's
nothing here yet" (no workouts, no saved devices, an empty search result) and
"this is working on it" (`IsBusy` while connecting, while loading history).
Phase 13 doesn't go back and redesign those screens — it notices that the same
two needs showed up four separate times, and only now, with four real examples
in hand, builds `EmptyStateView` and `LoadingOverlay` as reusable `ContentView`s
with `BindableProperty`s (`Icon`, `Title`, `Message`, `ActionCommand`;
`IsRunning`, `Message`) that get dropped into each of those four screens' `Grid`
at the review checkpoint.

**Why not just polish each screen as you build it.** The simpler-sounding plan —
and the one this project actually followed through Phase 10 — is to write each
screen's empty/loading markup inline, right there in that screen's XAML, when
that screen gets built. That's not wrong; it's what happened, and it's why this
phase exists now rather than in Phase 03. The cost of continuing that way past
this point is concrete: four (soon more) separate copies of near-identical
`VerticalStackLayout` + `ActivityIndicator` markup, each free to drift — one
screen's spinner a different size, another missing the
`SemanticProperties.Description="Loading"` the others have. When the
touch-target fix (44→48) or a contrast fix needs to land, it has to land N times
instead of once, and nothing stops the Nth copy from being subtly wrong.
Building `EmptyStateView`/`LoadingOverlay` *before* Phase 03, on the other hand,
would have been guessing: the actual set of properties it needs
(`ActionCommand` for a retry button, `IconDescription` for accessibility) only
became knowable by having real screens with real "nothing to show" reasons to
look at. Phase 13 sits at exactly the point where the repetition is proven and
the shape is known — not before.

**The pattern, named plainly.** This is "Don't Repeat Yourself" (DRY) via
extract-shared-component, and MAUI's mechanism for it — `BindableProperty` — is
real, measurable cost: each property is roughly five lines of ceremony (the
static field, the getter/setter, sometimes a `propertyChanged` callback),
multiplied by five properties on `EmptyStateView` alone. That cost buys a
specific payoff for this project: a fix to how "no workouts" looks — spacing,
wording tone, the icon — happens in one file and appears correctly on every
screen that uses it, including screens Phase 14+ hasn't built yet. It would
*not* be worth it for a screen with a genuinely one-off empty condition unlike
any other screen's — the win here comes specifically from the same
icon/title/message/action shape recurring across four-plus real screens, which
is the concrete, already-observed repetition Metz's "wait for the second or
third occurrence" rule asks for before you extract anything.

## Learning goals

- **`ContentView` vs. `ContentPage`** — a `ContentView` is a reusable *piece* of UI
  (like the empty-state and loading-overlay views below) that gets embedded inside
  a page, as opposed to `ContentPage`, which *is* a full screen. Both are declared
  the same XAML+code-behind way.
- **`BindableProperty`** — how a custom `ContentView` exposes its own bindable
  properties (`Icon`, `Title`, `IsRunning`, ...) so a page embedding it can bind to
  them exactly like any built-in control's properties. This is the mechanism that
  makes `EmptyStateView`/`LoadingOverlay` below reusable rather than copy-pasted
  per screen.
- **Layering views in a `Grid`** — MAUI stacks children of the same `Grid` cell in
  declaration order (later children on top), which is how the loading-overlay
  pattern below sits *over* a page's normal content without a separate popup/modal
  mechanism.
- **`SemanticProperties.Description`/`.Hint`** — the .NET MAUI accessibility API
  (superseding the older `AutomationProperties.Name`/`HelpText`, which are
  deprecated as of .NET 8) that tells a screen reader what to say for a control
  with no readable text of its own (an icon-only button, an image).
- **`HapticFeedback`** — the platform-abstracted vibration API, and the Android
  manifest permission it needs.
- **44pt vs. 48dp** — Apple's Human Interface Guidelines recommend a 44×44 point
  minimum touch target; Android's own accessibility guidance
  (`developer.android.com`) recommends 48×48 dp. This project is Android-only, so
  the Android figure is the one that should govern — see the flagged discrepancy
  in `Styles.xaml` below.

## Reference docs

| Topic | URL |
|---|---|
| Add an app icon to a .NET MAUI app project | https://learn.microsoft.com/en-us/dotnet/maui/user-interface/images/app-icons?view=net-maui-10.0 |
| Add a splash screen to a .NET MAUI app project | https://learn.microsoft.com/en-us/dotnet/maui/user-interface/images/splashscreen?view=net-maui-10.0 |
| Build accessible apps with semantic properties | https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/accessibility?view=net-maui-10.0 |
| Haptic Feedback — .NET MAUI | https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device/haptic-feedback?view=net-maui-10.0 |
| Android: make apps more accessible (48dp touch target guidance) | https://developer.android.com/guide/topics/ui/accessibility/apps |
| Android: test your app's accessibility (TalkBack pass) | https://developer.android.com/guide/topics/ui/accessibility/testing |
| Apple Human Interface Guidelines (44pt figure, for the discrepancy note below) | https://developer.apple.com/design/human-interface-guidelines/ |
| Theming (light/dark, `AppThemeBinding`) — already in `docs/learning/03-Doc-Links.md` | https://learn.microsoft.com/en-us/dotnet/maui/user-interface/theming |
| Style apps using XAML — already in `docs/learning/03-Doc-Links.md` | https://learn.microsoft.com/en-us/dotnet/maui/user-interface/styles/xaml |

---

## Reusable building blocks

These live under `Features/Shared/` alongside the existing `BaseViewModel.cs` and
`Converters.cs` (see `docs/learning/00-What-Is-Maui.md`'s project-structure
section). Full working code — paste it in as-is, then wire it into each real
screen at the review checkpoint.

### 1. Typography scale

`Styles.xaml` already has `Headline` (32pt), `SubHeadline` (24pt), `Caption`
(12pt), `MetricValue` (40pt), and `MetricLabel` (13pt), plus an implicit `Label`
default of 14pt. One gap: there's no named style for ordinary paragraph/body
text sized a step above the 14pt default — e.g. an error-message body, a settings
description, empty-state message text. Add this to `Styles.xaml` (**you add this,
not the agent** — this phase doesn't touch `src/` directly, see the note at the
top of this doc):

```xml
<!-- Body: readable paragraph text — error messages, settings descriptions,
     empty-state copy. One step above the implicit Label default (14) so it's a
     deliberate choice, not just "whatever a bare Label happens to be." -->
<Style TargetType="Label" x:Key="Body">
    <Setter Property="TextColor" Value="{AppThemeBinding Light={StaticResource ColorTextPrimaryLight}, Dark={StaticResource ColorTextPrimaryDark}}" />
    <Setter Property="FontSize" Value="16" />
    <Setter Property="LineHeight" Value="1.3" />
</Style>
```

The resulting scale, smallest to largest: `Caption` (12) → `MetricLabel` (13) →
implicit `Label` (14) → `Body` (16) → `SubHeadline` (24) → `Headline` (32) →
`MetricValue` (40).

**Consistent spacing** — there's no equivalent named-token system for spacing in
this project (colors have `Colors.xaml`; spacing doesn't have a `Sizes.xaml`).
Rather than invent one speculatively, the convention going forward is: pick
`Spacing`/`Padding` values from **4, 8, 12, 16, 24, 32** — multiples of 4, which
is already what the existing `Button`/`SecondaryButton` styles use
(`Padding="14,10"` is the one existing exception, already in the codebase from
Phase 00). If this convention starts feeling unwieldy across many screens, a
`Resources/Styles/Sizes.xaml` of named `double`/`Thickness` constants is a
reasonable future addition — flag it at a review checkpoint rather than adding it
speculatively now.

### 2. Empty state — `EmptyStateView`

For any screen that can legitimately have nothing to show yet (no workouts, no
saved devices, no history) — icon/glyph, title, message, and an optional action
button.

**`src/MyHi.Companion/Features/Shared/EmptyStateView.xaml`:**

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="MyHi.Companion.Features.Shared.EmptyStateView"
             x:Name="Root">
    <VerticalStackLayout Spacing="12"
                          Padding="32"
                          VerticalOptions="Center"
                          HorizontalOptions="Center"
                          MaximumWidthRequest="320">

        <!-- Placeholder glyph, not an icon font — this project has no icon font
             set up yet. Swap for a real icon/image later; flag that decision at
             a review checkpoint rather than pulling in an icon library now for
             one glyph. -->
        <Label Text="{Binding Icon, Source={x:Reference Root}}"
               FontSize="40"
               HorizontalOptions="Center"
               TextColor="{AppThemeBinding Light={StaticResource ColorTextSecondaryLight}, Dark={StaticResource ColorTextSecondaryDark}}"
               SemanticProperties.Description="{Binding IconDescription, Source={x:Reference Root}}" />

        <Label Text="{Binding Title, Source={x:Reference Root}}"
               Style="{StaticResource SubHeadline}" />

        <Label Text="{Binding Message, Source={x:Reference Root}}"
               Style="{StaticResource Body}"
               HorizontalTextAlignment="Center"
               TextColor="{AppThemeBinding Light={StaticResource ColorTextSecondaryLight}, Dark={StaticResource ColorTextSecondaryDark}}" />

        <Button Text="{Binding ActionText, Source={x:Reference Root}}"
                Command="{Binding ActionCommand, Source={x:Reference Root}}"
                Style="{StaticResource SecondaryButton}"
                IsVisible="{Binding HasAction, Source={x:Reference Root}}"
                Margin="0,8,0,0" />

    </VerticalStackLayout>
</ContentView>
```

**`src/MyHi.Companion/Features/Shared/EmptyStateView.xaml.cs`:**

```csharp
using System.Windows.Input;

namespace MyHi.Companion.Features.Shared;

public partial class EmptyStateView : ContentView
{
    public EmptyStateView()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(EmptyStateView), "○");

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly BindableProperty IconDescriptionProperty =
        BindableProperty.Create(nameof(IconDescription), typeof(string), typeof(EmptyStateView), string.Empty);

    public string IconDescription
    {
        get => (string)GetValue(IconDescriptionProperty);
        set => SetValue(IconDescriptionProperty, value);
    }

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(EmptyStateView), string.Empty);

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(EmptyStateView), string.Empty);

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public static readonly BindableProperty ActionTextProperty =
        BindableProperty.Create(nameof(ActionText), typeof(string), typeof(EmptyStateView), string.Empty);

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public static readonly BindableProperty ActionCommandProperty =
        BindableProperty.Create(
            nameof(ActionCommand),
            typeof(ICommand),
            typeof(EmptyStateView),
            propertyChanged: (bindable, _, _) => ((EmptyStateView)bindable).OnPropertyChanged(nameof(HasAction)));

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public bool HasAction => ActionCommand is not null;
}
```

**Usage example** (e.g. Phase 10's workout history list when there are zero
workouts):

```xml
<shared:EmptyStateView
    Icon="&#x25CB;"
    IconDescription="No workouts"
    Title="No workouts yet"
    Message="Connect to the treadmill and start a workout to see it here."
    IsVisible="{Binding HasNoWorkouts}" />
```

(`xmlns:shared="clr-namespace:MyHi.Companion.Features.Shared"` at the top of
whichever page uses it.)

### 3. Loading overlay — `LoadingOverlay`

A dimmed `ActivityIndicator` overlay that sits on top of a page's normal content
inside a `Grid`, instead of every screen writing its own `IsBusy`-triggered
show/hide logic from scratch.

**`src/MyHi.Companion/Features/Shared/LoadingOverlay.xaml`:**

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="MyHi.Companion.Features.Shared.LoadingOverlay"
             x:Name="Root"
             InputTransparent="False"
             IsVisible="{Binding IsRunning, Source={x:Reference Root}}">
    <Grid BackgroundColor="{AppThemeBinding Light={StaticResource ColorBackgroundLight}, Dark={StaticResource ColorBackgroundDark}}"
          Opacity="0.9">
        <VerticalStackLayout Spacing="8"
                              VerticalOptions="Center"
                              HorizontalOptions="Center">
            <ActivityIndicator IsRunning="{Binding IsRunning, Source={x:Reference Root}}"
                                WidthRequest="36"
                                HeightRequest="36"
                                SemanticProperties.Description="Loading" />
            <Label Text="{Binding Message, Source={x:Reference Root}}"
                   Style="{StaticResource Caption}"
                   IsVisible="{Binding HasMessage, Source={x:Reference Root}}" />
        </VerticalStackLayout>
    </Grid>
</ContentView>
```

**`src/MyHi.Companion/Features/Shared/LoadingOverlay.xaml.cs`:**

```csharp
namespace MyHi.Companion.Features.Shared;

public partial class LoadingOverlay : ContentView
{
    public LoadingOverlay()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty IsRunningProperty =
        BindableProperty.Create(nameof(IsRunning), typeof(bool), typeof(LoadingOverlay), false);

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(
            nameof(Message),
            typeof(string),
            typeof(LoadingOverlay),
            string.Empty,
            propertyChanged: (bindable, _, _) => ((LoadingOverlay)bindable).OnPropertyChanged(nameof(HasMessage)));

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
}
```

**Usage example** — the layering pattern: page content in the `Grid`'s first
child, the overlay as a later child so it paints on top:

```xml
<Grid>
    <!-- normal page content, e.g. the dashboard from Phase 03 -->
    <VerticalStackLayout Spacing="16">
        ...
    </VerticalStackLayout>

    <shared:EmptyStateView
        IsVisible="{Binding HasNoWorkouts}"
        Title="No workouts yet"
        Message="Connect to the treadmill and start a workout to see it here." />

    <shared:LoadingOverlay
        IsRunning="{Binding IsBusy}"
        Message="Connecting..." />
</Grid>
```

`IsBusy` here is the property every ViewModel already has from `BaseViewModel`
(see `docs/learning/00-What-Is-Maui.md`) — no new ViewModel plumbing needed, just
bind to what's already there.

### 4. Haptics on speed-change confirmation

Verified against the current `HapticFeedback` API (`Microsoft.Maui.Devices`) —
the method is `HapticFeedback.Default.Perform(HapticFeedbackType)`, not
`Vibrate()` or anything similarly guessable.

**Android setup** — add to `Platforms/Android/AndroidManifest.xml` (**you add
this, not the agent** — outside this phase's scope to touch `src/` directly):

```xml
<uses-permission android:name="android.permission.VIBRATE" />
```

**Call site** — wherever the speed-change *confirmation* actually lands (e.g. in
Phase 05's control ViewModel, right after `SetSpeedAsync` returns a successful
`ControlResult`, not on every debounced tap):

```csharp
using Microsoft.Maui.Devices;

private async Task ConfirmSpeedChangeAsync(double newSpeedKmh)
{
    var result = await _treadmillService.SetSpeedAsync(newSpeedKmh);

    if (result.Succeeded)
    {
        HapticFeedback.Default.Perform(HapticFeedbackType.Click);
    }
    // on failure: surface the plain-language error (per the existing
    // "error messages that say what to do" task below) — no haptic, since a
    // vibration on a *failed* command would read as confirmation of the wrong
    // thing.
}
```

`HapticFeedbackType` has two values: `Click` (short tap, used above — this is a
confirmation, not a warning) and `LongPress`. Nothing else needs a haptic in this
app per the monochrome theme's "convey state without a second signal channel
unless it earns it" philosophy — resist the urge to add one to every button tap.

### 5. Accessibility — concrete examples

**Icon-only button** (e.g. the Stop control from Phase 05 — an `ImageButton` has
no text of its own for a screen reader to announce):

```xml
<ImageButton Source="stop_icon.png"
             Command="{Binding StopCommand}"
             SemanticProperties.Description="Stop treadmill"
             SemanticProperties.Hint="Requests the treadmill to stop the belt. The physical safety key is the emergency stop." />
```

**Image** (a purely illustrative image still needs a description if it carries
meaning — e.g. a treadmill diagram on an empty/onboarding state):

```xml
<Image Source="treadmill_illustration.png"
       SemanticProperties.Description="Illustration of a treadmill" />
```

Two things worth knowing before applying this everywhere, straight from the
current Microsoft docs (not obvious from the API signature):

- **Never set `SemanticProperties.Description` on a `Label`.** It replaces the
  spoken text with the description instead of adding to it — since a `Label`
  already has readable `Text`, this makes things worse, not better.
- **Never set it on an `Entry`/`Editor` on Android** — it breaks TalkBack's
  editing actions. Use `Placeholder` or `SemanticProperties.Hint` instead for
  input fields.

### 6. Touch target size — flag, don't fix here

`Styles.xaml` sets `MinimumHeightRequest="44"` / `MinimumWidthRequest="44"` on
`Button`, `SecondaryButton`, `CheckBox`, `Editor`, `Entry`, `ImageButton`,
`Picker`, `RadioButton`, `SearchBar`, and `TimePicker`. **44 is Apple's Human
Interface Guidelines figure, not Android's.** Android's own accessibility
guidance (`developer.android.com/guide/topics/ui/accessibility/apps`) recommends
a minimum touch target of **48×48 dp** — this app is Android-only, so 44 is the
wrong platform's number, carried over from a generic MAUI-template default rather
than chosen deliberately.

**This phase does not change `Styles.xaml`** — that's out of scope here (see the
top of this doc). Bring `44` → `48` as a one-line, ten-value find/replace to the
review checkpoint for this phase; it's a real correctness fix, just not one to
make silently outside the file's own review.

---

## Tasks

### App icon, splash

Concrete steps:
1. Replace the two files the MAUI template already provides:
   `Resources/AppIcon/appicon.svg` (background layer) and
   `Resources/AppIcon/appiconfg.svg` (foreground layer) — see
   [Add an app icon to a .NET MAUI app project](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/images/app-icons?view=net-maui-10.0)
   for exactly what "adaptive icon" background/foreground layering means on
   Android and why two files, not one.
2. Same replacement pattern for `Resources/Splash/splash.svg` — see
   [Add a splash screen to a .NET MAUI app project](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/images/splashscreen?view=net-maui-10.0).
3. **Deliberate choice needed:** the `<MauiSplashScreen>` MSBuild item's `Color`
   attribute takes one static hex value — it does **not** support an
   `AppThemeBinding`-style light/dark pair, because the OS paints the splash
   before your app's XAML resources are even loaded. Recommendation: use the
   monochrome theme's light background, `#F2F2F3` (`Gray050`/`ColorBackgroundLight`
   from `Colors.xaml`), since Android 12+'s system splash-screen API already
   layers the OS's own day/night handling around your icon — read the "Platform
   notes" section of the splashscreen doc above before assuming otherwise.
4. Build and check the launcher icon and the first-frame splash on the device —
   this is one of the few things you genuinely cannot verify from the XAML alone.

### Typography scale, consistent spacing

Already covered in full above under "Reusable building blocks → 1. Typography
scale." Apply `Body` (once added to `Styles.xaml`) to any Label currently relying
on the bare 14pt implicit default for actual paragraph copy — settings
descriptions, empty-state messages, error explanations.

### Loading and empty states for **every** screen

Concrete steps, repeated once per screen from Phases 03/05/08/10/11:
1. Identify the screen's "nothing to show yet" condition (no workouts, no saved
   devices, not yet connected, empty search results) and its existing `IsBusy`
   property (already present via `BaseViewModel`).
2. Wrap the screen's root content in a `Grid` if it isn't already (most single-
   child `ContentPage`s already are, per Phase 00/03's layout conventions).
3. Add `EmptyStateView` and `LoadingOverlay` as later `Grid` children per the
   usage example above, with `IsVisible` bound to that screen's actual empty
   condition and `IsBusy`.
4. Confirm at the review checkpoint: does the empty state read correctly for
   *that* screen's specific nothing-to-show reason (a fresh install has no
   workouts; a mid-scan devices list is empty but not "wrong")?

### Error messages that say what to do, not what failed

**"Couldn't reach the treadmill — check it's powered on and try again"** beats
"GATT error 133," though the code still belongs in the log. Concrete steps:
1. Audit every user-facing error surface across Phases 02/05/07 (connection
   failures, control-point rejections, foreground-service issues) — list the raw
   codes/exceptions currently shown, if any.
2. For each, write a short plain-language sentence describing the *next action*
   the user can take, not the failure mechanism.
3. Log the original code/exception alongside the friendly message (both, always
   — the friendly message is for the user, the code is for you debugging later).
4. The `LoadingOverlay`/`EmptyStateView` pair above double as an error surface —
   an `EmptyStateView` with an `ActionText="Try again"` bound to a retry command
   is a natural fit for a connection failure, not just a genuinely-empty list.

### Accessibility: content descriptions, minimum touch targets, contrast check

Concrete steps:
1. Apply `SemanticProperties.Description`/`.Hint` per the "Accessibility —
   concrete examples" section above to every icon-only control across the real
   screens (dashboard connection indicator, control buttons, any `ImageButton`).
2. Bring the 44→48 touch-target fix (flagged above) to the review checkpoint.
3. Contrast check: the monochrome ramp's darkest/lightest steps
   (`ColorTextPrimary*` on `ColorBackground*`/`ColorSurface*`) were chosen to
   already clear WCAG AA at normal text size — spot-check with a contrast
   checker if a screen introduces a *new* pairing not already in
   `04-Monochrome-Theme.md`'s token table (e.g. `ColorTextSecondary*` on
   `ColorSurface*`, used in dimmer/disabled contexts, is the pairing most worth
   double-checking since it's the smallest gap in the ramp).

### Haptics on speed change confirmation

Covered in full above under "Reusable building blocks → 4."

### The diagnostic screens from Phase 00 stay, behind a developer toggle

They are how the next firmware surprise gets diagnosed — do not delete them.
Concrete steps:
1. Reuse Phase 08's `Preferences`-backed settings pattern to add one new boolean,
   e.g. `ShowDeveloperOptions` (default `false`), exposed from a `SettingsService`
   or equivalent the way every other Phase 08 setting already is.
2. In `AppShell.xaml`, bind whichever `FlyoutItem`/`TabBar` entry currently
   exposes Phase 00's diagnostic screens (`ScanPage`, `ControlConsolePage`, etc.)
   to that flag, e.g. `IsVisible="{Binding ShowDeveloperOptions}"` — the exact
   property/binding-context names depend on how `AppShell.xaml` currently wires
   its `BindingContext`, so treat the shape above as the pattern and adjust to
   match what Phase 00 actually produced.
3. Leave the routes themselves registered unconditionally in
   `AppShell.xaml.cs` — only the flyout/tab *entry point* is gated, so a
   developer can still `GoToAsync("scan")` directly if needed even with the
   toggle off.
4. Add the toggle itself to the Settings screen from Phase 08, off by default.

---

## Tests

- Every screen reviewed in light and dark, at 33% and full width
- TalkBack pass over the dashboard and controls
- Font scale at 200% does not break layouts

### How to actually run each of these

1. **Light/dark:** toggle the phone's system theme (**Settings → Display → Dark
   theme**) and revisit every screen — the monochrome theme's `AppThemeBinding`
   pairs should make this automatic with zero code changes; if a screen looks
   wrong in one mode, it's almost always a raw hex value that snuck in instead of
   a semantic token (see `04-Monochrome-Theme.md`'s "never invent a hex color"
   rule).
2. **33% / full width:** use Phase 11's split-screen support to resize the app to
   its narrowest supported size and check every screen (particularly the
   `EmptyStateView`'s `MaximumWidthRequest="320"` — confirm it doesn't overflow at
   33% on a small phone) and again at full width.
3. **TalkBack pass:** enable TalkBack (**Settings → Accessibility → TalkBack →
   Use TalkBack**, per the
   [Android accessibility testing guide](https://developer.android.com/guide/topics/ui/accessibility/testing)),
   then navigate the dashboard and controls using only swipe-to-move-focus and
   double-tap-to-activate — no direct touch. Every control that does something
   should announce something meaningful; anything silent or announcing only "Button"
   is missing a `SemanticProperties.Description`.
4. **Font scale 200%:** **Settings → Display → Font size**, set to maximum, and
   revisit the dashboard and any screen with dense text (settings, statistics).
   Layouts using `Auto`/`*` `Grid` rows and `VerticalStackLayout` (already the
   project's convention per `docs/learning/00-What-Is-Maui.md`) should reflow
   correctly; a layout that clips or overlaps at 200% has a hardcoded
   `HeightRequest` somewhere that needs to become `Auto`.

## Acceptance

- [ ] Release candidate
