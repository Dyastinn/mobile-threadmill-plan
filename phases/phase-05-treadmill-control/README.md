# Phase 05 — Treadmill Control

> **This phase may not exist.** Phase 00 verdict V2 decides. If the control point does
> not honour commands, skip to Phase 06 — a read-only app is still worth shipping, and
> "void" here is a finding, not a failure.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 04 · **Gated on:** V2

---

## Goal

Change treadmill speed from the app, reliably. Everything here was already proven by
hand in the Phase 00 control console; this phase turns that into product UI.

## Learning goals

- **Debounce/coalesce as a reusable pattern.** Phase 04's grace timer was the first
  place this project used cancel-and-restart on a `CancellationTokenSource`; this
  phase's tap debounce (task 5.1) is the same shape applied to rapid button taps
  instead of a connection drop — the point is to recognize it as one pattern, not
  learn it twice.
- **Gating UI on a live handshake, not a static feature bit.** `0x2ACC`'s Speed
  Target Setting bit is set on this device and is still not trustworthy — see
  `../../ASSUMPTIONS.md` A3. This phase gates every control on the actual
  `Request Control` → `Set Target Speed` round trip instead.
- **`BindableLayout`** for a small, non-virtualized, data-driven row of buttons —
  different from `CollectionView`, which Phase 03 used for a much larger,
  virtualized list. Knowing when each one is the right tool is the point.
- **Confirmation-before-destructive-action UI**, and specifically why this project
  uses friction instead of a color for it — there is no red in the monochrome theme,
  and even if there were, `00-Project-Plan.md`'s safety section treats "Stop" as
  needing a deliberate second step regardless of color.
- **`IAsyncRelayCommand`** — what `[RelayCommand]` generates for an `async Task`
  method, and why the Stop confirmation dialog calls `StopCommand.ExecuteAsync(null)`
  from code-behind instead of binding `Clicked` straight to the command.

## Reference docs

- `../../05-FTMS-Protocol.md` §7 — commands, response format, result codes
- `../phase-00-probe-app/PHASE-00-FINDINGS.md` V2 — the exact byte sequences that
  worked, with the operator's notes on what physically happened
- `docs/learning/04-Monochrome-Theme.md` — every token and style this phase's XAML
  uses; read "Conveying state without color" before task 5.4, it's the direct source
  of the Stop button's design
- **`BindableLayout`** — [BindableLayout - .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/layouts/bindablelayout) —
  read this before task 5.3; it's what drives the preset-speed button row
- **Display pop-ups (`DisplayAlert`)** — [Display pop-ups - .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/pop-ups) —
  the mechanism behind the Stop confirmation in task 5.4
- **Commanding (`ICommand`)** — https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/data-binding/commanding
- **MVVM source generators overview** (`[RelayCommand]`) — https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/overview
- **Compiled bindings (`x:DataType`)** — https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/data-binding/compiled-bindings
- **`CancellationTokenSource`** — https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource
  — same class Phase 04's grace timer used; task 5.1's debounce reuses it

---

## Features

- Increase / decrease by the device's minimum increment (0.1 km/h)
- Preset speed buttons **generated from the range read from `0x2AD4`**, not hardcoded.
  For 1.0–16.0: 2, 4, 6, 8, 10, 12 km/h. Generated, so the code survives a firmware
  change or a different treadmill
- Stop

## Availability is decided by behaviour, not by a feature bit

The `Speed Target Setting` bit is set on this device — and the same bitmask claims
incline support on a machine with no incline, so it carries little weight. Gate on the
live handshake:

1. `0x2AD9` was discovered
2. `Request Control` (`00`) returned `0x01`
3. The first `Set Target Speed` returned `0x01`

Any step fails → disable speed controls **for the session**, log the result code, show
the user a plain explanation.

---

## Task 5.1 — `TreadmillControlViewModel` shape

Creates: `src/MyHi.Companion/Features/Treadmill/TreadmillControlViewModel.cs`.

This is logic — the agent describes the shape below, you write the real method
bodies, per the usual "you write it, the agent teaches" track from `../README.md`.

Concrete steps:

1. Create `src/MyHi.Companion/Features/Treadmill/`. This lives in the **app**
   project, not `Core` — it's a ViewModel and depends on
   `[ObservableProperty]`/`[RelayCommand]` MVVM plumbing, the same reasoning that put
   the real `TreadmillService` in the app project in Phase 01a rather than `Core`.
2. Constructor takes `ITreadmillService` — same DI pattern as every other ViewModel
   in this project (see `ScanViewModel`, `ControlConsoleViewModel` from Phase 00 for
   examples already in the codebase).
3. Write the class against the shape below. Keep the signatures as they are so the
   XAML in task 5.3 binds without edits; fill in every `// TODO`.

```csharp
namespace MyHi.Companion.Features.Treadmill;

public sealed partial class TreadmillControlViewModel : BaseViewModel
{
    private readonly ITreadmillService _treadmill;
    private CancellationTokenSource? _debounceCts;

    [ObservableProperty]
    private double targetSpeedKph;

    [ObservableProperty]
    private bool canControl;

    public ObservableCollection<double> PresetSpeeds { get; } = new();

    public TreadmillControlViewModel(ITreadmillService treadmill)
    {
        _treadmill = treadmill;
        _treadmill.ConnectionStateChanged += OnConnectionStateChanged;
        _treadmill.MachineEventReceived += OnMachineEventReceived;
        _treadmill.SampleReceived += OnSampleReceived;
    }

    [RelayCommand]
    private void IncreaseSpeed()
    {
        // TODO: TargetSpeedKph = _treadmill.SpeedRange!.Clamp(TargetSpeedKph + _treadmill.SpeedRange.IncrementKph);
        // then DebounceSend();
    }

    [RelayCommand]
    private void DecreaseSpeed()
    {
        // TODO: mirror of IncreaseSpeed, subtracting the increment instead
    }

    [RelayCommand]
    private void SetPresetSpeed(double presetKph)
    {
        // TODO: TargetSpeedKph = presetKph; DebounceSend();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        // TODO: await _treadmill.StopAsync(), surface the result via StatusMessage
        // (inherited from BaseViewModel). No confirmation here — the confirmation
        // already happened in the View before this command was invoked. See task 5.4.
    }

    private void DebounceSend()
    {
        // TODO: cancel _debounceCts, start a new CancellationTokenSource, delay
        // ~300ms against its token, then call _treadmill.SetSpeedAsync(TargetSpeedKph)
        // if the token wasn't cancelled in the meantime. Same cancel-and-restart shape
        // as Phase 04's grace timer (WorkoutEngine.StartGraceTimer) — reread that if
        // this feels unfamiliar.
    }

    private async Task EvaluateControlAvailabilityAsync()
    {
        // TODO — task 5.2: RequestControlAsync(), then CanControl = result.Success.
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        // TODO: on Ready, populate PresetSpeeds from _treadmill.SpeedRange (see
        // GeneratePresets below) and call EvaluateControlAvailabilityAsync().
        // On Disconnected, CanControl = false.
    }

    private void OnMachineEventReceived(object? sender, MachineEvent e)
    {
        // TODO — task 5.2: on ControlPermissionLost, CanControl = false, then
        // re-run EvaluateControlAvailabilityAsync(). This is the spot Phase 04's
        // WorkoutEngine left a "// handled in Phase 05" comment pointing at.
    }

    private void OnSampleReceived(object? sender, TreadmillSample sample)
    {
        // TODO — task 5.6: reconcile TargetSpeedKph against sample.SpeedKph if you
        // used optimistic UI; revert the displayed value if the machine didn't comply.
    }
}
```

**Task 5.1b — preset generation.** Fill in the part of `OnConnectionStateChanged` that
populates `PresetSpeeds`. Generate, don't hardcode:

```csharp
private static IEnumerable<double> GeneratePresets(SpeedRange range, int count = 6)
{
    // TODO: evenly space `count` values between range.MinKph and range.MaxKph,
    // passing each through range.Clamp(...) so it lands on a real device increment.
    // For 1.0-16.0 km/h this should land close to 2, 4, 6, 8, 10, 12.
}
```

---

## Task 5.2 — Control availability gating

Fill in `EvaluateControlAvailabilityAsync` and the `OnMachineEventReceived` stub.

Concrete steps:

1. On `ConnectionState.Ready`: call `await _treadmill.RequestControlAsync()`.
2. `CanControl = result.Success` (equivalently, `result.Code == FtmsResultCode.Success`).
3. If it failed, set `StatusMessage` to a plain explanation — task 5.5 gives the full
   result-code-to-message mapping; wire it in now or come back once that task is done.
4. Re-run this same evaluation after a `ControlPermissionLost` `MachineEvent` — that's
   what `0x2ADA`'s `0xFF` op code means (`05-FTMS-Protocol.md` §5), and controls must
   stay disabled until `Request Control` succeeds again.

---

## Task 5.3 — Speed control XAML

Creates: `src/MyHi.Companion/Features/Treadmill/TreadmillControlView.xaml` and
`TreadmillControlView.xaml.cs`. This is UI — the full, working code, ready to paste
in. Adjust bindings only if your ViewModel's property/command names in task 5.1
ended up different.

`TreadmillControlView.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:treadmill="clr-namespace:MyHi.Companion.Features.Treadmill"
             x:Class="MyHi.Companion.Features.Treadmill.TreadmillControlView"
             x:Name="Root"
             x:DataType="treadmill:TreadmillControlViewModel">

    <VerticalStackLayout Spacing="16" IsEnabled="{Binding CanControl}">

        <!-- Current target speed -->
        <VerticalStackLayout Spacing="2">
            <Label Text="{Binding TargetSpeedKph, StringFormat='{0:F1}'}"
                   Style="{StaticResource MetricValue}" />
            <Label Text="km/h target" Style="{StaticResource MetricLabel}" />
        </VerticalStackLayout>

        <!-- Increase / decrease -->
        <HorizontalStackLayout Spacing="12" HorizontalOptions="Center">
            <Button Text="&#8722;" Command="{Binding DecreaseSpeedCommand}"
                    WidthRequest="64" FontSize="24" />
            <Button Text="+" Command="{Binding IncreaseSpeedCommand}"
                    WidthRequest="64" FontSize="24" />
        </HorizontalStackLayout>

        <!-- Preset speeds, generated from the device's supported range -->
        <Label Text="Presets" Style="{StaticResource Caption}" />
        <HorizontalStackLayout Spacing="8"
                                BindableLayout.ItemsSource="{Binding PresetSpeeds}">
            <BindableLayout.ItemTemplate>
                <DataTemplate x:DataType="x:Double">
                    <Button Text="{Binding ., StringFormat='{0:F0}'}"
                            Style="{StaticResource SecondaryButton}"
                            Command="{Binding Source={x:Reference Root}, Path=BindingContext.SetPresetSpeedCommand}"
                            CommandParameter="{Binding .}" />
                </DataTemplate>
            </BindableLayout.ItemTemplate>
        </HorizontalStackLayout>

        <BoxView HeightRequest="1" Margin="0,4" />

        <!-- Stop — never call this an emergency stop. See task 5.4. -->
        <Button Text="Stop" Clicked="OnStopClicked" />
        <Label Text="The physical safety key is the emergency stop. This button sends a Bluetooth command and may not respond instantly."
               Style="{StaticResource Caption}"
               HorizontalTextAlignment="Center" />

    </VerticalStackLayout>
</ContentView>
```

`TreadmillControlView.xaml.cs`:

```csharp
namespace MyHi.Companion.Features.Treadmill;

public partial class TreadmillControlView : ContentView
{
    public TreadmillControlView()
    {
        InitializeComponent();
    }

    private async void OnStopClicked(object sender, EventArgs e)
    {
        if (BindingContext is not TreadmillControlViewModel viewModel)
        {
            return;
        }

        bool confirmed = await Shell.Current.DisplayAlert(
            "Stop the belt?",
            "This sends a Bluetooth stop command. It is not the emergency stop — " +
            "pull the physical safety key if you need the belt to stop immediately.",
            "Stop",
            "Cancel");

        if (confirmed)
        {
            await viewModel.StopCommand.ExecuteAsync(null);
        }
    }
}
```

Two things worth understanding, not just pasting:

- **`BindableLayout.ItemsSource` on a `HorizontalStackLayout`**, not a
  `CollectionView`. Six preset buttons don't need virtualization, and a `Button`
  nested inside a `CollectionView` `DataTemplate` is exactly the "gesture
  recognizers inside `CollectionView` items are unreliable on Android" trap noted in
  `docs/learning/02-Glossary.md` — `BindableLayout` sidesteps it by not recycling
  anything.
- **`Source={x:Reference Root}, Path=BindingContext.SetPresetSpeedCommand`.** Inside
  the `DataTemplate`, the binding context is the `double` preset value itself (that's
  what `CommandParameter="{Binding .}"` sends), not the ViewModel — so the command
  binding can't just say `{Binding SetPresetSpeedCommand}`. `x:Reference Root` reaches
  back up to the named root `ContentView`, whose own `BindingContext` is the
  ViewModel, to find the command.

Register in DI (`MauiProgram.cs`):

```csharp
builder.Services.AddTransient<TreadmillControlViewModel>();
builder.Services.AddTransient<TreadmillControlView>();
```

Embed it in Phase 03's dashboard page: add
`<treadmill:TreadmillControlView BindingContext="{Binding TreadmillControlViewModel}" />`
to its layout if the dashboard ViewModel exposes one, or resolve
`TreadmillControlViewModel` via DI in the dashboard page's own constructor and assign
it directly — either is fine, pick whichever matches how Phase 03's page already
takes its dependencies.

Build the app project to confirm the XAML compiles:

```powershell
dotnet build src/MyHi.Companion/MyHi.Companion.csproj -f net10.0-android
```

---

## Task 5.4 — Why the Stop confirmation, specifically

This isn't decoration — `00-Project-Plan.md`'s safety section and
`docs/learning/04-Monochrome-Theme.md`'s "Conveying state without color" table both
call it out directly: **"Emergency Stop" was in an earlier draft and was rejected.** A
Bluetooth command over an unreliable radio link is not an emergency stop; the physical
safety key on the treadmill is. Two consequences, both already in the code above:

- The button is labelled **"Stop"**, never "Emergency Stop" or anything implying it's
  the safety mechanism.
- The confirmation dialog's message states the safety key exists and what it's for,
  every time — not just once in a settings screen the user will forget.

The confirmation step itself is the answer to "how do you make Stop feel different
from every other button without a red color to lean on" — this theme has no hue to
spend on danger, so the friction of a deliberate second tap carries that weight
instead. That's the same reasoning that keeps `StartAsync` (Phase 05's counterpart,
already gated in `ITreadmillService`'s doc comments) behind "a deliberate on-screen
user action," never a notification action or restored state.

Nothing further to implement here — task 5.3's `OnStopClicked` already is this
requirement. This task exists so the reasoning isn't just implicit in code you pasted.

---

## Task 5.5 — Result code messages

Fill in the `StatusMessage` assignment left as a TODO in tasks 5.1 and 5.2.

Concrete steps:

1. Add a small mapping from `FtmsResultCode` to a user-facing string. Shape:
   ```csharp
   private static string DescribeResult(FtmsResultCode code) => code switch
   {
       FtmsResultCode.Success => "OK",
       FtmsResultCode.OpCodeNotSupported => "This treadmill doesn't support that command.",
       FtmsResultCode.InvalidParameter => "That speed isn't valid for this treadmill.",
       FtmsResultCode.OperationFailed => "The treadmill rejected that. Try again.",
       FtmsResultCode.ControlNotPermitted => "Control was lost — reconnecting control...",
       FtmsResultCode.Timeout => "The treadmill didn't respond in time.",
       FtmsResultCode.NotConnected => "Not connected to the treadmill.",
       FtmsResultCode.NotSupported => "Speed control isn't available on this device.",
       _ => "Something went wrong."
   };
   ```
   `Control Not Permitted` and `Invalid Parameter` genuinely need different messages
   — one tells the user to wait, the other means a bug in your clamping logic sent a
   value outside the device's range (log the actual value sent, per
   `05-FTMS-Protocol.md` §7's result table).
2. On `ControlNotPermitted` specifically (result code `0x05`): re-issue
   `RequestControlAsync()` and retry the original command once before giving up and
   showing the message — this is the "re-issue Request Control, then retry once"
   behaviour from the Implementation requirements below.

---

## Task 5.6 — Reconcile against `0x2ACD`

Fill in `OnSampleReceived`.

Concrete steps:

1. If you implemented optimistic UI (setting `TargetSpeedKph` immediately on tap,
   before the control point confirms), compare each incoming `sample.SpeedKph`
   against what you expect a few seconds after a command was sent.
2. If the machine's reported speed never approaches the target you set, revert
   `TargetSpeedKph` to the last confirmed value and set `StatusMessage` accordingly
   — don't leave the UI claiming a speed the belt never reached.
3. This is a judgement call on exact timing/tolerance — discuss the approach at the
   review checkpoint rather than guessing a magic number alone.

---

## Implementation requirements

- **Debounce and coalesce.** Rapid +/- taps produce **one** write of the final target,
  not one per tap. ~300 ms debounce — task 5.1's `DebounceSend`.
- Clamp to the `0x2AD4` range; round to the device increment — `SpeedRange.Clamp`
  already exists in `ITreadmillService.cs` and has its own unit tests from Phase 01b.
- **Serialise writes.** One outstanding command, wait for the indication (3 s timeout)
  before the next. This is `ITreadmillService`'s implementation's job (Phase 01a's
  `TreadmillService`), not this ViewModel's — the ViewModel just calls
  `SetSpeedAsync`/`StopAsync` and trusts the service to serialise underneath.
- Surface failures with the **actual result code meaning** — task 5.5.
- `0x05 Control Not Permitted` → re-issue `Request Control`, then retry once — task 5.5.
- `0xFF` on `0x2ADA` → disable controls, re-request control before re-enabling —
  task 5.2.
- **Re-issue `Request Control` after every reconnect** before enabling controls —
  task 5.2's `OnConnectionStateChanged`.
- Optimistic UI is fine, but **reconcile against the next `0x2ACD` notification** and
  revert if the machine did not comply — task 5.6.
- If V2 found control permission expires after idle, refresh it before a command
  rather than letting the first tap after a pause fail.

---

## Safety — not pedantry

"Emergency Stop" was in an earlier draft. **Do not name it that.** A Bluetooth stop
command over an unreliable link is not an emergency stop; the physical safety key is.

- Label it **"Stop"**.
- State in the UI that the safety key is the emergency stop.
- Never send `Start` (`07`) without a deliberate on-screen user action. **Not** from a
  notification action, **not** from restored state, **not** from auto-reconnect.

---

## Tests

| Test | Expected | |
|------|----------|---|
| Increase speed | Belt actually speeds up | `[HUMAN]` |
| Decrease speed | Belt slows | `[HUMAN]` |
| Rapid 10× tap on + | **One** write; final speed correct | `[HUMAN]` |
| Set speed below minimum | Clamped, no error | |
| Set speed above maximum | Clamped, no error | |
| Stop | Belt stops | `[HUMAN]` |
| Stop tapped, dialog cancelled | No command sent | |
| Control without Request Control | Fails gracefully, clear message | |
| Set speed after reconnect | Works — control re-requested | `[HUMAN]` |
| Result code 0x05 handling | Re-requests control, retries once | |
| Indication timeout | Surfaces after 3 s, does not hang the queue | |

Automated tests, creates `src/MyHi.Companion.Tests/Treadmill/TreadmillControlViewModelTests.cs`:

1. Using `FakeTreadmillService`: call `IncreaseSpeedCommand` ten times in quick
   succession, assert only one `SetSpeedAsync` call reached the fake (add a call
   counter to `FakeTreadmillService` if it doesn't already track this) and that the
   final `TargetSpeedKph` is correct.
2. `SetPresetSpeedCommand` with a value outside the fake's `SpeedRange` — assert it
   gets clamped, not sent as-is.
3. `GeneratePresets` as a `[Theory]` against a couple of different `SpeedRange`
   values (not just 1.0–16.0) — assert every generated preset is inside range and on
   a valid increment.
4. `DescribeResult` — one assertion per `FtmsResultCode` value, so a future added
   enum value fails the test instead of silently falling through to the default
   message.
5. Run:
   ```powershell
   dotnet test src/MyHi.Companion.Tests
   ```

## Acceptance

- [ ] 20 consecutive speed changes succeed
- [ ] No command ever sent outside the device's range
- [ ] Rapid tapping produces one write
- [ ] Nothing in the app can start the belt without a deliberate on-screen tap
- [ ] Stop requires confirmation and is never labelled "Emergency Stop"
