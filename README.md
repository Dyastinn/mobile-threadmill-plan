# MyHi Companion

A personal Android app that replaces FitShow for daily use with the MY-HI Q8Y
treadmill. Connects over Bluetooth FTMS, shows live metrics, controls speed,
and records workout history with per-workout telemetry. Fully offline: no
account, no network calls, no cloud sync.

Built with **Flutter**, not the .NET MAUI stack this project started with —
see the [Stack](#stack) section below for what changed and why. This is a
learning project as much as an app: past Phase 00, the code is written by the
project owner, with an AI agent teaching concepts and reviewing rather than
implementing. See [`flutter/phases/README.md`](flutter/phases/README.md) for
exactly how that works.

---

## Status

**Starting fresh: no code in the repo yet.** An earlier build of Phase 00 (the
probe/diagnostic app, then written in MAUI) ran on the real treadmill and its
measurements are still good — hardware facts don't depend on the UI
framework. See [`phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](phases/phase-00-probe-app/PHASE-00-FINDINGS.md)
for what's already known. The Flutter app itself is being built from scratch,
following [`flutter/phases/phase-00-probe-app/`](flutter/phases/phase-00-probe-app/)
(start with `TASKS.md`).

> **Note on the two `phases/` folders.** `phases/` at the repo root is the
> original MAUI-era plan; it's kept because its protocol reference, measured
> device findings, and human test procedures are framework-independent and
> still the source of truth. `flutter/phases/` is the current implementation
> plan, in Flutter/Dart, and is what to follow for actually building the app.

---

## The device

MY-HI Q8Y folding treadmill, whose BLE module is a FitShow (Xiamen) transparent-UART
board presenting an FTMS shim (`0x1826`) over the top. The shim's own feature
declaration is provably unreliable (it claims incline support on a machine with
no incline), so this project verifies everything against captured hex rather
than trusting the spec or the device's self-description. See
[`phases/phase-01-protocol-decode/README.md`](phases/phase-01-protocol-decode/README.md)
for the byte-level detail and each phase's own README for what's still an open
question in that phase's scope.

---

## Stack

| Component | Choice |
|-----------|--------|
| Framework | Flutter, Android only |
| Language | Dart |
| Bluetooth | [`flutter_blue_plus`](https://pub.dev/packages/flutter_blue_plus) |
| Database | SQLite via [`sqflite`](https://pub.dev/packages/sqflite) |
| State management / DI | [`flutter_riverpod`](https://pub.dev/packages/flutter_riverpod) |
| Navigation | [`go_router`](https://pub.dev/packages/go_router) |
| Min/target SDK | Android 31 / 36 |

**Why Flutter, and not the .NET MAUI stack this project started with:** the
developer works on Linux, and MAUI's edit loop there has no XAML Hot
Reload — confirmed in JetBrains Rider's own documentation, whose Hot Reload
support table lists Windows, macOS, and iOS only. Every UI change would mean
a full rebuild-and-redeploy cycle, indefinitely. Flutter's hot reload is
engine-level, not IDE-specific, so it works identically on Linux. For a solo
developer, that recurring daily cost outweighed the one-time cost of learning
Dart. Full writeup, including what the original MAUI decision record said and
why it no longer holds, in
[`flutter/phases/phase-00-probe-app/README.md`](flutter/phases/phase-00-probe-app/README.md#technology-decisions).

New to Dart or Flutter? Start at
[`flutter/docs/learning/00-Dart-and-Flutter-Essentials.md`](flutter/docs/learning/00-Dart-and-Flutter-Essentials.md).
Curious *why* each piece of this stack, specifically? Every dependency has a
full decision record (problem it solves, alternatives considered, why they
lost) in the `flutter/phases/` folder that introduced it.

## Repository layout

```
flutter/
├── myhi_companion/                 the Flutter app — Android only
│   ├── lib/features/               scan, connect, dashboard, diagnostics
│   └── test/                       flutter_test — widget tests
├── packages/myhi_companion_core/   pure Dart package: hex/FTMS/capture/SQLite —
│                                   zero Flutter dependency, `dart test`-able
├── phases/                         the current, Flutter-specific work order
└── docs/learning/                  Dart/Flutter primer

phases/                        the original MAUI-era plan — kept for its
                                framework-independent protocol reference,
                                database schema, and device findings
docs/learning/                 the original MAUI/.NET primer (superseded)
captures/                      raw BLE session logs (JSONL) from the probe app —
                                the source of every parser test fixture, shared
                                by both plans
```

## Building and running

Once the project exists (see Status above):

```bash
cd flutter/myhi_companion
flutter pub get
flutter test                                     # widget tests
cd ../packages/myhi_companion_core && dart test  # pure-logic unit tests
```

To install on a phone over USB (Developer Options / USB debugging enabled):

```bash
cd flutter/myhi_companion
flutter run --release
```

For a standalone APK to sideload without a cable:

```bash
flutter build apk --release
```

The APK lands in `flutter/myhi_companion/build/app/outputs/flutter-apk/`.

## Safety

The app can start the belt moving. **The physical safety key is the emergency
stop, not this app.** Every control that can cause motion has a confirmation
step and says so.

## Non-goals

Writing these down so they don't creep in: cloud sync, social features,
multi-user, iOS, Strava/Garmin export, training plans, coaching. This is a
personal, offline, single-device, single-treadmill app.

## Risk register

See [`phases/README.md`](phases/README.md#risk-register) for the standing list
of what's most likely to go wrong and the mitigation in place for each.
