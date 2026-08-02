# MyHi Companion (Flutter track)

The Flutter/Dart implementation of the app described in the root
[`../README.md`](../README.md): a personal Android app that replaces FitShow for
daily use with the MY-HI Q8Y treadmill. Connects over Bluetooth FTMS, shows live
metrics, controls speed, records workout history. Fully offline: no account, no
network calls, no cloud sync.

**Why a separate track, and why Flutter over the original MAUI plan:** the
developer works on Linux. .NET MAUI's edit loop on Linux has no XAML Hot
Reload — confirmed in JetBrains' own Rider documentation, whose Hot Reload
support table lists Windows, macOS, and iOS only, Linux is absent even with
Rider installed — meaning every UI change is a full rebuild-and-redeploy cycle,
indefinitely, not a one-time cost. Flutter is a first-class Linux dev target:
hot reload is engine-level, not IDE-specific, so it works identically on Linux,
macOS, and Windows. For a solo developer prioritizing development experience,
that recurring daily tax outweighed the one-time cost of learning Dart. See the
technology decision record in
[`phases/phase-00-probe-app/README.md`](phases/phase-00-probe-app/README.md#technology-decisions)
for the full writeup, including why MAUI was the original pick and what changed.

This folder is **additive**, not a replacement: the original MAUI-based plan
still lives at the repo root (`../README.md`, `../phases/`, `../docs/`) and is
left as-is. Everything genuinely framework-independent — the FTMS protocol
reference, measured hardware facts, the human test procedures, raw BLE
captures — is **not duplicated here**. This track references it in place
rather than forking it, so there is exactly one copy of ground truth to keep
correct.

---

## Status

**No code yet.** This folder currently holds the plan for Phase 00 (the
probe/diagnostic app) and the seam interface Phase 01b needs to start. See
[`phases/README.md`](phases/README.md) for the phase order and
[`phases/phase-00-probe-app/TASKS.md`](phases/phase-00-probe-app/TASKS.md) for
where to actually start.

The earlier MAUI build of Phase 00 already ran on the real treadmill and its
measurements are still the project's ground truth — see
[`../phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](../phases/phase-00-probe-app/PHASE-00-FINDINGS.md).
Nothing about switching UI frameworks changes what was measured about the
hardware.

---

## The device

MY-HI Q8Y folding treadmill; BLE module is a FitShow (Xiamen) transparent-UART
board presenting an FTMS shim (`0x1826`) over the top. Its feature declaration
is provably unreliable — see
[`../phases/phase-01-protocol-decode/README.md`](../phases/phase-01-protocol-decode/README.md)
for the byte-level protocol reference this track builds its parsers against
unchanged. Nothing in this section depends on the UI framework, so it isn't
repeated here.

---

## Stack

| Component | Choice |
|-----------|--------|
| Framework | Flutter, Android only |
| Language | Dart |
| Bluetooth | [`flutter_blue_plus`](https://pub.dev/packages/flutter_blue_plus) |
| Local database | [`sqflite`](https://pub.dev/packages/sqflite) — raw SQL, no ORM, same philosophy as the MAUI track's `Microsoft.Data.Sqlite` choice |
| State management / DI | [`flutter_riverpod`](https://pub.dev/packages/flutter_riverpod) (plain providers, no code generation — see below) |
| Navigation | [`go_router`](https://pub.dev/packages/go_router) |
| Min/target SDK | Android 31 / 36 (same measured constraints as the MAUI track) |

New to Dart or Flutter? Start at
[`docs/learning/00-Dart-and-Flutter-Essentials.md`](docs/learning/00-Dart-and-Flutter-Essentials.md).
Full decision records (problem solved, alternatives considered, why they lost)
live in the phase that introduces each dependency, same convention as the
original track: framework/BLE/navigation in
[`phases/phase-00-probe-app/README.md`](phases/phase-00-probe-app/README.md#technology-decisions).

**Why plain Riverpod, not `riverpod_generator`:** code generation via
`build_runner` is a real DX cost on any platform (a watcher process, generated
files to keep in sync), and it's the same category of "extra moving part in
the edit loop" that pushed this project away from MAUI-on-Linux in the first
place. Plain `Notifier`/`StateNotifier` providers are slightly more
boilerplate but zero extra tooling. Revisit if the boilerplate becomes the
actual bottleneck, not preemptively.

**Why `sqflite`, not `drift`:** `drift` is a real ORM with its own code
generation and query builder; `sqflite` is a thin wrapper over raw SQL, which
matches the project's existing "write the SQL, no ORM" stance (see
[`../phases/phase-06-recording-schema/README.md`](../phases/phase-06-recording-schema/README.md)
for the schema itself, which doesn't change with the framework).

## Repository layout

Flutter's own idiom, not a reskin of the MAUI three-project split. A pure Dart
package needs no separate "Tests" project — tests live in its own `test/`
directory and run with `dart test`; the Flutter app's widget tests live in its
`test/` and run with `flutter test`.

```
flutter/
├── myhi_companion/                 the Flutter app — Android only
│   ├── lib/
│   │   ├── features/
│   │   │   ├── bluetooth/          scan, connect, GATT access
│   │   │   ├── diagnostics/        the probe screens (Phase 00)
│   │   │   └── shared/             theme, shared widgets, router
│   │   └── main.dart
│   ├── test/                       flutter_test — widget tests
│   └── pubspec.yaml
├── packages/
│   └── myhi_companion_core/        pure Dart package — zero Flutter dependency
│       ├── lib/
│       │   └── treadmill/          the ITreadmillService seam, FTMS parsers,
│       │                           capture format, SQLite plumbing
│       └── test/                   dart test — no Android target needed
└── docs/learning/                  Dart/Flutter primer, glossary, doc links
```

`myhi_companion` depends on `myhi_companion_core` as a local path package. The
split exists for the same reason the MAUI track split `Core` out: anything
that's pure logic (parsers, the capture format, the treadmill-service
contract) needs to be unit-testable without a device or emulator, and a plain
Dart package gets that via `dart test` — no Flutter SDK, no Android target,
same as `MyHi.Companion.Core` needing no MAUI reference.

## Building and running

Once the project exists (see Status above):

```bash
cd flutter/myhi_companion
flutter pub get
flutter test                                    # this package's widget tests
cd ../packages/myhi_companion_core && dart test  # pure-logic unit tests
```

To install on a phone over USB (Developer Options / USB debugging enabled):

```bash
cd flutter/myhi_companion
flutter run --release
```

`--release` matters for the same reason the MAUI track calls it out: a debug
build sideloaded without a live `flutter run` session behind it won't behave
like a real standalone install. For a build to hand off or keep installed
without a cable:

```bash
flutter build apk --release
```

The APK lands in `flutter/myhi_companion/build/app/outputs/flutter-apk/`.

## Safety

Unchanged from the root plan: the app can start the belt moving. **The
physical safety key is the emergency stop, not this app.** Every control that
can cause motion has a confirmation step and says so.

## Non-goals

Same as the root plan: cloud sync, social features, multi-user, iOS,
Strava/Garmin export, training plans, coaching. Personal, offline,
single-device, single-treadmill.

## Risk register

See [`../phases/README.md`](../phases/README.md#risk-register) for the
standing list — it's about the hardware and protocol, not the UI framework,
so it isn't forked here. One row is now moot (MAUI Android BLE background
reliability) and gets a Flutter-specific equivalent when Phase 07 (foreground
service) is actually written for this track.
