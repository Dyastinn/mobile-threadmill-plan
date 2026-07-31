# MyHi Companion

A personal Android app that replaces FitShow for daily use with the MY-HI Q8Y
treadmill — connects over Bluetooth FTMS, shows live metrics, controls speed,
and records workout history with per-workout telemetry. Fully offline: no
account, no network calls, no cloud sync.

This is a learning project as much as an app: past Phase 00, the code is
written by the project owner, with an AI agent teaching concepts and reviewing
rather than implementing. See [`phases/README.md`](phases/README.md) for
exactly how that works.

---

## Status

**Phase 00 (the probe/diagnostic app) is built and working.** It's a lab
instrument, not the product — six screens for finding the treadmill, dumping
every GATT characteristic as hex, watching the live notification stream, and
sending raw control-point commands by hand with the decoded response shown
back. Its job was to answer "what does this specific treadmill actually do"
with real bytes instead of assumptions, and it did: see
[`phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](phases/phase-00-probe-app/PHASE-00-FINDINGS.md)
and [`DEVICE.md`](DEVICE.md) for what's been measured so far.

**Next up:** [Phase 01b](phases/phase-01-protocol-decode/README.md) —
`ITreadmillService` skeleton + `FakeTreadmillService` — the first task in the
real app, unblocked and ready to start. Phase 01a (the real protocol parsers)
is still waiting on one more hands-on session with the treadmill.

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

New to MAUI? Start at [`docs/learning/00-What-Is-Maui.md`](docs/learning/00-What-Is-Maui.md).

## Repository layout

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

Cloud sync, social features, multi-user, iOS, Strava/Garmin export, training
plans, coaching. See [`00-Project-Plan.md`](00-Project-Plan.md) for the full
reasoning behind the stack and scope.
