# MyHi Companion

A personal Android app that replaces FitShow for daily use with the MY-HI Q8Y
treadmill. Connects over Bluetooth FTMS, shows live metrics, controls speed,
and records workout history with per-workout telemetry. Fully offline: no
account, no network calls, no cloud sync.

This is a learning project as much as an app: past Phase 00, the code is
written by the project owner, with an AI agent teaching concepts and reviewing
rather than implementing. See [`phases/README.md`](phases/README.md) for
exactly how that works.

---

## Status

**Starting fresh: no code in the repo yet.** An earlier build of Phase 00 (the
probe/diagnostic app) ran on the real treadmill and its measurements are still
good; see [`phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](phases/phase-00-probe-app/PHASE-00-FINDINGS.md)
and [`DEVICE.md`](DEVICE.md) for what's already known. The `src/` project
itself is being rebuilt from scratch, following
[`phases/phase-00-probe-app/`](phases/phase-00-probe-app/) (start with
`TASKS.md`).

---

## The device

MY-HI Q8Y folding treadmill, whose BLE module is a FitShow (Xiamen) transparent-UART
board presenting an FTMS shim (`0x1826`) over the top. The shim's own feature
declaration is provably unreliable (it claims incline support on a machine with
no incline), so this project verifies everything against captured hex rather
than trusting the spec or the device's self-description. See
[`05-FTMS-Protocol.md`](05-FTMS-Protocol.md) for the byte-level detail and
[`ASSUMPTIONS.md`](ASSUMPTIONS.md) for what's still an open question.

---

## Stack

| Component | Choice |
|-----------|--------|
| Framework | .NET MAUI, Android only (`net10.0-android`) |
| Bluetooth | [Plugin.BLE](https://github.com/dotnet-bluetooth-le/dotnet-bluetooth-le) |
| Database | SQLite via `Microsoft.Data.Sqlite` |
| MVVM | `CommunityToolkit.Mvvm` |
| Min/target SDK | Android 31 / 36 |

New to C#? Start at
[`docs/learning/00a-CSharp-Essentials.md`](docs/learning/00a-CSharp-Essentials.md).
Know C#, new to MAUI? Start at
[`docs/learning/00-What-Is-Maui.md`](docs/learning/00-What-Is-Maui.md).
Curious *why* each piece of this stack, specifically? See
[`02-Technology-Stack.md`](02-Technology-Stack.md) — every dependency here has a
full decision record (problem it solves, alternatives considered, why they lost).

## Repository layout

This is the structure Phase 00 sets up; `src/` doesn't exist yet in this repo.

```
src/
├── MyHi.Companion/            the Android app — XAML, ViewModels, BLE plumbing
├── MyHi.Companion.Core/       plain net10.0 library: hex/FTMS/capture/SQLite —
│                              zero MAUI dependency, unit-testable without Android
└── MyHi.Companion.Tests/      xUnit tests against Core

phases/                        one folder per phase — the work order for what
                                gets built next, and why
docs/learning/                 MAUI/.NET primer, emulator setup, glossary
captures/                      raw BLE session logs (JSONL) from the probe app —
                                the source of every parser test fixture

00-Project-Plan.md             vision, stack decisions, non-goals, risk register
05-FTMS-Protocol.md            byte-level FTMS spec for this specific device
ASSUMPTIONS.md                 every open question, with the phase it blocks
DEVICE.md                      measured facts only — never a guess
ITreadmillService.cs           the seam most of the app builds against, not BLE directly
```

## Building and running

Once the project exists (see Status above), these are the commands you'll use:

```powershell
cd src
dotnet build MyHiCompanion.slnx        # whole solution
dotnet test MyHi.Companion.Tests       # Core's unit tests
```

To install on a phone over USB (with Developer Options / USB debugging enabled):

```powershell
dotnet build MyHi.Companion/MyHi.Companion.csproj -t:Run -f net10.0-android
```

For a standalone APK to sideload without a cable, build **Release** — Debug
builds use Fast Deployment and ship with no assemblies embedded, so they won't
run if just copied to a phone:

```powershell
dotnet build MyHi.Companion/MyHi.Companion.csproj -c Release -f net10.0-android
```

The APK lands in `src/MyHi.Companion/bin/Release/net10.0-android/`.

Want to see a screen without a phone plugged in? See
[`docs/learning/01-Emulator-Setup.md`](docs/learning/01-Emulator-Setup.md) —
with the caveat that the emulator has no real Bluetooth radio, so it's a
UI/crash smoke test only.

## Safety

The app can start the belt moving. **The physical safety key is the emergency
stop, not this app.** Every control that can cause motion has a confirmation
step and says so.

## Non-goals

See [`00-Project-Plan.md`](00-Project-Plan.md) for the full list and the
reasoning behind the stack and scope.
