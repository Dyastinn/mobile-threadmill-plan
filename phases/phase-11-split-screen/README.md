# Phase 11 — Split Screen

> One of the two reasons this app exists at all. Treat it as a feature, not a layout
> chore.

**Hardware:** required to verify · **Size:** M · **Blocked by:** Phase 10

> See `../README.md` for the collaboration model. This phase is almost entirely
> UI/layout work, so the agent writes the XAML and the `[Activity(...)]` change in
> full — but two of its three tasks are verification against code that already
> exists from Phase 00, not new logic, and are called out as such below.

---

## Goal

Usable alongside YouTube.

### Understanding what you're building (read this before the tasks)

**The everyday problem.** Picture a phone call where the phone company hangs up
and redials every time you shift in your chair, adjust the phone against your
ear, or the screen dims and wakes back up. None of those things have anything to
do with whether the conversation needs to continue — but if the phone company
treated them as reasons to restart the call, it would be unusable. Android has
a similar instinct with activities and configuration changes: by default, a
"big" event like a window resize tears the whole activity down and rebuilds it
from scratch, the same as force-closing and reopening the app. Left unchecked,
that default would apply directly to `MainActivity` during exactly the moment
this phase exists to support — treadmill still running, screen still on, user
mid-workout — the app "hangs up and redials" a Bluetooth connection that never
actually needed to drop.

**Why split screen doesn't force a Bluetooth reconnect.** The naive-sounding
plan — "handle a window resize like any other big UI event: assume the
connection might be gone, reconnect defensively once the layout changes" — isn't
a simpler alternative worth weighing, it's literally what *would* happen without
work already done in earlier phases. Two things prevent it, and this phase adds
no new machinery to get there — task 11.3 exists to verify the machinery already
works, not to build it. First, `MainActivity.cs`'s `[Activity(...)]` attribute
already declares the `configChanges` a split-screen resize triggers
(`ScreenSize`, `SmallestScreenSize`, `ScreenLayout`, from Phase 00) — that's what
stops Android's own tear-down-and-rebuild reflex on `MainActivity` at all.
Second, and the one worth sitting with: `WorkoutRecordingService` (Phase 07)
owns the BLE connection and sample recording — the UI only *binds* to it, it
never holds the connection itself. So even in the hypothetical worst case where
`MainActivity` somehow did get destroyed and rebuilt mid-resize, the treadmill
connection wouldn't go down with it, because it was never `MainActivity`'s to
lose. A window resize, traced all the way through, is nothing more than a
layout event — `AdaptiveTrigger` swapping which `VisualState` is active — with
no path to the connection at all.

**The pattern, named plainly.** This is the Single Responsibility Principle
showing up as a concrete, already-banked payoff rather than a rule to recite:
`MainActivity` changes for UI/display reasons (screen size, orientation, theme);
`WorkoutRecordingService` changes for connection-lifecycle reasons (BLE state,
buffered-sample flushes). Because those are different reasons to change, Phase 07
already put them in different classes — not in anticipation of split screen, but
because "the UI's lifetime" and "the connection's lifetime" were already worth
separating on their own terms, to survive screen-lock. The cost was real at the
time: an extra component to stand up, bind to, and reason about, instead of one.
The payoff shows up here, for free, four phases later — split screen needed zero
new connection-management code, only a manifest attribute and some XAML. Worth
naming honestly: this payoff isn't automatic just from "using a `Service`" — it
required deliberately keeping the BLE connection out of `MainActivity`/the
ViewModel layer in the first place. A project that let a ViewModel hold the
`ITreadmillService` connection directly — a perfectly reasonable choice for an
app with no background-survival or split-screen requirement — would be paying
for this phase's constraint the hard way, right now, instead of collecting it as
a free line in a changelog.

## Features

- Responsive layout at 33% / 50% / 75% / full
- Compact dashboard variant below a height threshold
- `android:resizeableActivity="true"`, correct `configChanges` handling

## Implementation requirements

- Handle configuration changes **without recreating the BLE connection**. The Phase 07
  service owns it, so this should already hold — verify that it does rather than
  assuming it.
- **Speed controls must remain reachable at 33% height.** This is the real constraint;
  everything else follows from it.
- No horizontal scrolling at any width.

---

## Learning goals

- **`VisualStateManager` + `AdaptiveTrigger`** — the declarative, XAML-only way to
  change a page's layout based on window size, instead of hand-rolling `if`
  statements in an event handler. `Styles.xaml` already uses plain `VisualState`s
  (no triggers) for `Normal`/`Disabled` — those are states a control enters on its
  own. This phase is where you meet the *triggered* kind: a state the framework picks
  automatically based on a measured condition.
- **`AdaptiveTrigger.MinWindowWidth`/`MinWindowHeight`** specifically watch the app's
  **window**, not the page or any one container — which is exactly the thing that
  changes size when Android resizes the app for split screen. That's why
  `AdaptiveTrigger` is the right tool here, not a page-local `SizeChanged` handler.
- Reading and reasoning about an `[Activity(...)]` attribute that's already partially
  correct (`MainActivity.cs` already declares most of what this phase needs) —
  verifying existing code against a real requirement, rather than writing from a
  blank file.
- `android:resizeableActivity` and Android's multi-window `configChanges` model — why
  an undeclared config change tears down and rebuilds the whole activity, and your
  BLE connection with it.

## Reference docs

- [Visual states — .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/visual-states?view=net-maui-10.0) —
  read the "Adaptive trigger" section specifically; it has the exact XAML shape this
  phase's layout uses, including the "set state on multiple elements" technique task
  11.2 relies on.
- [Triggers — .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/triggers?view=net-maui-10.0) —
  the "State triggers" and "Adaptive trigger" sections; also documents the precedence
  rule that matters once more than one `AdaptiveTrigger` is active at once (width
  beats height when both conditions are met simultaneously).
- [`AdaptiveTrigger` class reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.controls.adaptivetrigger?view=net-maui-10.0)
- [`Page.OnSizeAllocated` method reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.controls.page.onsizeallocated?view=net-maui-10.0)
  and [`VisualElement.SizeChanged` event reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.controls.visualelement.sizechanged?view=net-maui-10.0)
  — the code-behind alternatives to `AdaptiveTrigger`, useful only where a ViewModel
  needs to *know* the current layout mode programmatically rather than just display
  differently. Prefer `AdaptiveTrigger` for anything purely visual (task 11.2); reach
  for these only if that need actually comes up.
- [Support multi-window mode — Android Developers](https://developer.android.com/develop/adaptive-apps/guides/support-multi-window-mode) —
  `resizeableActivity`, the `configChanges` values Android recommends declaring, and
  the `Activity.isInMultiWindowMode()` / `onMultiWindowModeChanged()` APIs.
- `src/MyHi.Companion/Platforms/Android/MainActivity.cs` — read this before task 11.1;
  it already exists and already declares most of what this phase asks you to verify.
- `docs/learning/04-Monochrome-Theme.md` — `Styles.xaml`'s `MinimumHeightRequest="44"`/
  `MinimumWidthRequest="44"` on every interactive control is already the touch-target
  floor this phase's 33%-height constraint depends on; nothing new to add there, just
  worth knowing it's already covered.

---

## Walkthrough

### Task 11.1 — Manifest: `resizeableActivity` + verify `configChanges`

**Small, and mostly verification — the actual code change is one attribute.**

Android has two independent knobs here:

1. **`resizeableActivity`** — whether the activity is *allowed* to run in split
   screen / multi-window at all. Defaults to `true` on API 24+ if unset, but this
   project should say so explicitly rather than rely on a version-dependent default.
2. **`configChanges`** — which configuration changes the activity handles itself (via
   `OnConfigurationChanged`) instead of being destroyed and recreated for. A
   split-screen resize is a `screenSize`/`smallestScreenSize`/`screenLayout` change;
   if none of those are declared, Android tears down and rebuilds `MainActivity` on
   every resize — taking the BLE connection with it, exactly what this phase's
   Implementation requirements say must not happen.

`MainActivity.cs` already carries a `ConfigurationChanges` list, added in Phase 00:

```csharp
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
                          | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
```

Compare this against what
[Android's multi-window guide](https://developer.android.com/develop/adaptive-apps/guides/support-multi-window-mode)
recommends declaring — `screenSize | smallestScreenSize | screenLayout | orientation`
— and every one of those flags is already present (plus `UiMode` and `Density`, which
don't hurt). **This is the "verify it already holds" case the Implementation
requirements warned about**: the work here is confirming it with a real resize test
(step 3 below), not writing new `configChanges` handling.

What's genuinely missing is `resizeableActivity` itself — there's no
`ResizeableActivity = true` on the attribute yet.

Concrete steps:

1. Open `src/MyHi.Companion/Platforms/Android/MainActivity.cs`.
2. Add `ResizeableActivity = true` to the `[Activity(...)]` attribute — it's a
   property on `ActivityAttribute`, same as `MainLauncher` or `LaunchMode` already
   there:
   ```csharp
   [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
       ResizeableActivity = true,
       ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
                             | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
   public class MainActivity : MauiAppCompatActivity
   ```
3. Build and deploy to the phone. `[HUMAN]`: drag the app into split screen from the
   Recents screen. The split-screen affordance being available at all is itself part
   of the pass/fail signal for this step — on a device/Android version where
   `resizeableActivity` was ever effectively `false`, the option wouldn't appear.
4. `[HUMAN]`: with the app connected to the treadmill and mid-workout, trigger a
   split-screen resize (drag the divider). Confirm in `adb logcat` that `MainActivity`
   is **not** destroyed and recreated during the resize — look for the absence of a
   second `OnCreate` call, not just "the app didn't crash." This is the test that
   actually proves the existing `configChanges` list is sufficient, rather than just
   plausible-looking.

---

### Task 11.2 — Responsive layout: compact dashboard below a height threshold

**UI — full code, written for you.**

`AdaptiveTrigger` reacts to the app's **window** size, which is exactly what shrinks
when the split-screen divider moves. The plan: two `VisualState`s on the dashboard's
root layout, `Full` and `Compact`, switched by `MinWindowHeight`. `Compact` hides or
shrinks the secondary metrics and the contribution graph so the **speed controls stay
reachable at 33% height** — the one hard constraint this phase exists to satisfy.

Pick the breakpoint by *measuring*, not guessing. `[HUMAN]`: with the app in split
screen at roughly 33% of the target device's height (Poco X6 Pro 5G), log the actual
window height MAUI reports — either from `OnSizeAllocated` on the dashboard page, or
from `Window.Height` in `App.xaml.cs`'s `CreateWindow` — and use that measured number
in place of the `420` placeholder below. `420` is an unverified arithmetic estimate,
not a measured value; treat it as something to replace, not trust.

`DashboardPage.xaml` (root layout excerpt):

```xml
<ContentPage
    x:Class="MyHi.Companion.Features.Dashboard.DashboardPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:dashboard="clr-namespace:MyHi.Companion.Features.Dashboard"
    x:DataType="dashboard:DashboardViewModel">

    <Grid x:Name="RootLayout" RowDefinitions="Auto,Auto,*,Auto" Padding="12" RowSpacing="8">

        <VisualStateManager.VisualStateGroups>
            <VisualStateGroupList>
                <VisualStateGroup x:Name="WindowSizeStates">

                    <!-- Fallback state: MinWindowHeight="0" means this applies
                         whenever no larger threshold below is also met. -->
                    <VisualState x:Name="Compact">
                        <VisualState.StateTriggers>
                            <AdaptiveTrigger MinWindowHeight="0" />
                        </VisualState.StateTriggers>
                        <VisualState.Setters>
                            <Setter TargetName="ContributionGraphSection" Property="IsVisible" Value="False" />
                            <Setter TargetName="SecondaryMetricsSection" Property="IsVisible" Value="False" />
                            <Setter TargetName="PrimaryMetricLabel" Property="Style" Value="{StaticResource SubHeadline}" />
                        </VisualState.Setters>
                    </VisualState>

                    <!-- Replace 420 with your measured 33%-height breakpoint from
                         the [HUMAN] step above before relying on this. -->
                    <VisualState x:Name="Full">
                        <VisualState.StateTriggers>
                            <AdaptiveTrigger MinWindowHeight="420" />
                        </VisualState.StateTriggers>
                        <VisualState.Setters>
                            <Setter TargetName="ContributionGraphSection" Property="IsVisible" Value="True" />
                            <Setter TargetName="SecondaryMetricsSection" Property="IsVisible" Value="True" />
                            <Setter TargetName="PrimaryMetricLabel" Property="Style" Value="{StaticResource MetricValue}" />
                        </VisualState.Setters>
                    </VisualState>

                </VisualStateGroup>
            </VisualStateGroupList>
        </VisualStateManager.VisualStateGroups>

        <!-- Row 0: contribution graph (Phase 03) — hidden entirely in Compact -->
        <Border x:Name="ContributionGraphSection" Grid.Row="0">
            <!-- Phase 03's contribution graph widget goes here -->
        </Border>

        <!-- Row 1: connection indicator — always visible, both states -->
        <HorizontalStackLayout Grid.Row="1" Spacing="8">
            <Ellipse WidthRequest="10" HeightRequest="10"
                     Fill="{Binding IsConnected, Converter={StaticResource BoolToConnectionFillConverter}}" />
            <Label Text="{Binding ConnectionStatusText}" Style="{StaticResource Caption}" />
        </HorizontalStackLayout>

        <!-- Row 2: primary metric (speed) — always visible, font size changes by state -->
        <VerticalStackLayout Grid.Row="2" Spacing="2" VerticalOptions="Center">
            <Label x:Name="PrimaryMetricLabel" Text="{Binding SpeedKmh, StringFormat='{0:F1}'}" Style="{StaticResource MetricValue}" />
            <Label Text="km/h" Style="{StaticResource MetricLabel}" />
        </VerticalStackLayout>

        <!-- Secondary metrics (distance, calories, elapsed time) — hidden in Compact -->
        <Grid x:Name="SecondaryMetricsSection" Grid.Row="2" ColumnDefinitions="*,*,*">
            <!-- distance / calories / elapsed time tiles — existing Phase 03/04 content -->
        </Grid>

        <!-- Row 3: speed controls — ALWAYS visible in both states. This row is the
             entire reason Compact exists: everything above it can be sacrificed,
             this can't. -->
        <HorizontalStackLayout Grid.Row="3" Spacing="8" HorizontalOptions="Center">
            <Button Text="−" Command="{Binding DecreaseSpeedCommand}" />
            <Button Text="+" Command="{Binding IncreaseSpeedCommand}" />
            <Button Text="Stop" Style="{StaticResource SecondaryButton}" Command="{Binding StopCommand}" />
        </HorizontalStackLayout>

    </Grid>
</ContentPage>
```

Note the `Setter TargetName="..."` pattern rather than the visual state applying to
itself — this is the "set state on multiple elements from one trigger" technique from
the [Visual states doc](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/visual-states?view=net-maui-10.0):
the `VisualStateGroup` is declared once, on the root `Grid`, and its setters reach
into named children by `TargetName` instead of needing the group repeated on every
child that changes.

Concrete steps:

1. `[HUMAN]`: measure the real window height at ~33% split on the target device (see
   above) and replace the `420` placeholder with that number.
2. Merge the `VisualStateManager.VisualStateGroups` block and the `x:Name`s above into
   your actual `DashboardPage.xaml` from Phase 03/04 — the section names
   (`ContributionGraphSection`, `SecondaryMetricsSection`, `PrimaryMetricLabel`) need
   to match whatever you actually named those elements; adjust the `TargetName`
   values to fit your layout rather than renaming your layout to match this sample.
3. Build and run in the emulator first (resizing the emulator window approximates a
   window-size change, though it isn't real Android split screen — see
   `docs/learning/01-Emulator-Setup.md`'s caveat on this) to confirm the state
   actually switches before testing on-device.
4. `[HUMAN]`: on the phone, drag into split screen from 100% down through 75% / 50% /
   33%, both portrait and landscape. At every size, confirm: no horizontal scrolling,
   and the speed +/−/Stop row is fully visible and tappable. The 44 dp minimum touch
   target is already the default via `Styles.xaml`'s `MinimumHeightRequest`/
   `MinimumWidthRequest` on `Button`, so this should hold automatically — confirm
   rather than assume.

---

### Task 11.3 — Verify: no BLE reconnect on resize

**Verification, not new code — a fix (if one turns out to be needed) belongs in task
11.1 or in Phase 07, not here.**

Concrete steps:

1. `[HUMAN]`: start a workout, confirm connected and recording.
2. `[HUMAN]`: resize the split-screen divider through all four sizes, pausing a few
   seconds at each.
3. Check that `ConnectionState` never leaves `Ready` during any resize — watch the
   connection indicator from task 11.2 (it should never flicker to disconnected), or
   log `StateChanged` events. Then check the recorded workout afterward for sample
   gaps that line up with a resize event (`WorkoutSample.Flags` bit 0, per
   `14-Database.md`) — none should be present if task 11.1's `configChanges` is doing
   its job.
4. If a disconnect or gap **does** show up correlated with a resize, the fix belongs
   in task 11.1 (a missing `configChanges` flag) or in Phase 07's foreground-service
   ownership of the connection — not in this task's XAML. Don't paper over a real
   reconnect with UI state; find out which layer actually dropped it.

---

## Tests

| Test | | |
|------|---|---|
| Each of 4 sizes, portrait and landscape | Controls reachable and tappable | `[HUMAN]` |
| Resize mid-workout | No disconnect, no data gap | `[HUMAN]` |
| YouTube / Messenger / Chrome in the other pane | Dashboard keeps updating | `[HUMAN]` |
| `MainActivity` survives a resize | No second `OnCreate` in logcat during resize | `[HUMAN]` |
| `resizeableActivity` present | Split-screen option available from Recents | `[HUMAN]` |

## Acceptance

- [ ] Fully usable at 33% with no disconnect on resize
- [ ] `resizeableActivity="true"` confirmed present, via `ResizeableActivity = true`
      on `MainActivity`'s `[Activity(...)]` attribute
- [ ] `AdaptiveTrigger` breakpoint value is a measured number from the real device,
      not an unverified guess
- [ ] No horizontal scrolling at any of the 4 sizes, either orientation
