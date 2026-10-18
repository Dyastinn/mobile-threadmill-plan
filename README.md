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

**Starting fresh: no code in the repo yet.** The plan was originally written for
.NET MAUI and was moved to **Expo (React Native) with a development build** in
October 2026, before any app code existed. The earlier MAUI build of the Phase 00
probe app ran on the real treadmill, and its measurements are still good; see
[`phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](phases/phase-00-probe-app/PHASE-00-FINDINGS.md)
for what's already known. The `src/` project is built from scratch, following
[`phases/phase-00-probe-app/`](phases/phase-00-probe-app/) (start with `TASKS.md`).

Why the switch, in one paragraph: the MAUI choice rested on already knowing C#,
which turned out not to be true, while TypeScript and Expo were already familiar.
The one real risk Expo adds (whether JavaScript keeps running with the screen off
on HyperOS) is tested on the treadmill in Phase 00, before any product code
depends on it. The full decision record is in
[`phases/phase-00-probe-app/README.md`](phases/phase-00-probe-app/README.md#technology-decisions).

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

## Tech stack

Everything the app and its tooling use, what each piece is for, and the phase that
adds it. Versions are the ones current when the plan was written (October 2026); the
Expo SDK pins the exact versions of everything Expo-maintained, via
`npx expo install`.

### Platform and framework

| Piece | Version | What it's for | Added in |
|-------|---------|---------------|----------|
| Android | min SDK 31 (Android 12), target SDK 36 | The only platform. Target phone: Poco X6 Pro 5G, Android 16 / HyperOS | 00 |
| Expo SDK | 58 | Toolkit around React Native: builds, native config (CNG), first-party modules | 00 |
| React Native | 0.88 (New Architecture, always on) | Runs the TypeScript on the phone, renders native Android views | 00 |
| React | 19.3 | UI components and hooks | 00 |
| TypeScript | strict, plus `noUncheckedIndexedAccess` | The language for everything except the foreground service | 00 |
| Hermes | (bundled with React Native) | The JavaScript engine on the phone | 00 |
| Kotlin | (Android toolchain) | The foreground service, inside a local Expo module | 00 (spike), 07 |
| Build type | **Development build** (`expo-dev-client`), not Expo Go | Expo Go can't load the BLE library; a dev build is "your own Expo Go" | 00 |

### Libraries in the app

| Library | What it's for | Added in |
|---------|---------------|----------|
| [`react-native-ble-manager`](https://github.com/innoveit/react-native-ble-manager) | Bluetooth LE: scan, connect, read, write, notifications. The only file that imports it is `src/features/bluetooth/bleClient.ts` (plus the scanner) | 00 |
| `expo-router` | File-based navigation: a screen is a file in `src/app/` | 00 |
| `expo-build-properties` | Sets min/target SDK from `app.json` | 00 |
| `expo-sqlite` | The workout database (raw SQL, through a small `Db` interface) | 00, 06 |
| `expo-sqlite/kv-store` | Settings and small persisted values (synchronous key/value) | 00, 08 |
| `expo-file-system` | Capture logs, rolling log files, backup files, file and folder pickers | 00, 09 |
| `expo-sharing` | Share sheet for captures, logs and backups | 00, 09 |
| `expo-clipboard` | Copy hex values on the probe screens | 00 |
| `react-native-safe-area-context` | Keeps screens clear of the status bar (comes with Expo Router) | 00 |
| `expo-crypto` | `randomUUID()` for the portable `WorkoutId`/`DeviceUid` GUIDs | 06 |
| `fflate` | Streaming ZIP for backup export and import (pure JavaScript) | 09 |
| `react-native-svg` | Drawing the charts (the chart component itself is ours) | 10 |
| `expo-haptics` | One haptic, when the treadmill confirms a speed change | 13 |
| `expo-keep-awake` | Keep the screen on during a workout, if the setting is on | 13 |
| `expo-splash-screen` | Splash screen with separate light and dark backgrounds | 13 |

### Native code in this repo

| Piece | What it's for | Added in |
|-------|---------------|----------|
| `src/modules/recording-service/` (local Expo module, Kotlin) | `connectedDevice` foreground service extending React Native's `HeadlessJsTaskService`: keeps the process alive, keeps JavaScript timers running (headless keep-alive task), holds a partial wake lock, shows the workout notification with Pause/Stop | 00 (spike), 07 |
| `src/plugins/withResizeableActivity.js` (config plugin) | Declares `android:resizeableActivity="true"` for split screen | 11 |

### Development and testing tools

| Tool | What it's for | Added in |
|------|---------------|----------|
| Node.js 24 LTS | Runs the tooling; its built-in `node:sqlite` runs the database tests | 00 |
| JDK 17 (the installed JDK 21 may also work) | Gradle, for native builds | 00 |
| Android SDK + `adb` | Builds, installs, logs (`logcat`), `dumpsys` | 00 |
| Metro | Bundler; serves JavaScript to the dev build with Fast Refresh | 00 |
| Jest + `jest-expo` | Unit tests. `src/core/` tests run in plain Node | 00 |
| `node:sqlite` | Real SQLite in Jest, behind the same `Db` interface the app uses | 00 |
| ESLint (`npx expo lint`) | Linting, including the rule that `src/core/` never imports React Native or Expo | 00 |
| `tsc --noEmit` | Type checking: must be clean | 00 |
| React Native DevTools | Debugger, component tree, JavaScript heap snapshots | 00, 12 |
| nRF Connect (phone app) | Independent BLE cross-check during the probe runbook | 00 |
| Android Studio Profiler (optional) | Native CPU/memory, only if `dumpsys` and DevTools leave a question open | 12 |

### Deliberately not used

No state library (Zustand, Redux), no UI kit (Paper, Tamagui, NativeWind), no ORM
(Drizzle), no chart library, no Expo Go, no EAS cloud builds (everything builds
locally), no backend. Each was considered; the decision records say why it lost.

New to how Expo and React Native fit together, or have only used Expo Go? Start at
[`docs/learning/00-Expo-And-React-Native.md`](docs/learning/00-Expo-And-React-Native.md).
Have used React but want the patterns this codebase leans on explained properly
(effects and cleanup, refs vs. state, discriminated unions)? Read
[`docs/learning/00a-React-And-TypeScript-Essentials.md`](docs/learning/00a-React-And-TypeScript-Essentials.md).
Curious *why* each piece of this stack, specifically? Every dependency has a full
decision record (problem it solves, alternatives considered, why they lost) in the
phase that introduced it: framework/BLE/navigation in
[`phases/phase-00-probe-app/README.md`](phases/phase-00-probe-app/README.md#technology-decisions),
state management in [`phases/phase-03-live-dashboard/README.md`](phases/phase-03-live-dashboard/README.md),
SQLite in [`phases/phase-06-recording-schema/README.md`](phases/phase-06-recording-schema/README.md),
the foreground service in [`phases/phase-07-foreground-service/README.md`](phases/phase-07-foreground-service/README.md),
settings storage in [`phases/phase-08-settings/README.md`](phases/phase-08-settings/README.md),
ZIP/file handling in [`phases/phase-09-backup-minimal/README.md`](phases/phase-09-backup-minimal/README.md),
charts in [`phases/phase-10-statistics/README.md`](phases/phase-10-statistics/README.md).

## Repository layout

This is the structure Phase 00 sets up; `src/` doesn't exist yet in this repo.

```
src/                           the Expo project root (package.json, app.json)
├── app/                       Expo Router screens, one file per route (UI)
├── core/                      plain TypeScript: FTMS parsing, workout engine,
│                              SQLite repositories, backup. Imports nothing from
│                              react, react-native or expo-*; a lint rule enforces
│                              it, and Jest tests it in plain Node
├── features/                  the React side: hooks (one per screen), components,
│                              and the adapters that connect core/ to native APIs
│                              (BLE, files, notifications)
├── modules/recording-service/ local Expo module, Kotlin: the foreground service
└── android/                   GENERATED by `npx expo prebuild`; never edited,
                               never committed

phases/                        one folder per phase: the work order for what
                               gets built next, and why, plus the byte-level
                               protocol reference, database schema, and every
                               technology decision record
docs/learning/                 Expo/React Native primer, setup, glossary
captures/                      raw BLE session logs (JSONL) from the probe app,
                               the source of every parser test fixture
```

## Building and running

Once the project exists (see Status above), these are the commands you'll use,
all from `src/`. Full setup (Node, JDK 17, Android SDK, USB/wireless debugging on
the Xiaomi) is in [`docs/learning/01-Device-And-Emulator-Setup.md`](docs/learning/01-Device-And-Emulator-Setup.md).

```powershell
cd src
npm install
npx tsc --noEmit            # type check: must be clean
npx expo lint               # lint: must be clean (includes the core/ import rule)
npm test                    # Jest: core/ unit tests and the database tests
```

To build the development build and install it on the phone (USB or wireless adb):

```powershell
npx expo run:android --device
```

You only need to rebuild when a **native** dependency or `app.json` changes. For
everyday TypeScript edits, start the bundler with `npx expo start` and the
installed dev build picks the changes up with Fast Refresh, the same way Expo Go
did.

For a standalone APK you can install without the computer (and the only kind of
build to measure performance or battery against), build the release variant:

```powershell
npx expo run:android --variant release --device
```

The APK lands in `src/android/app/build/outputs/apk/release/`. It's signed with
the debug keystore Expo's template ships, which is fine for a personal sideloaded
app. Keep that the same between builds: Android refuses to install an update
signed with a different key, and uninstalling first wipes the app's data (that's
what Phase 09's backup exists for).

The emulator is a UI/crash smoke test only: it has no real Bluetooth radio. See
the setup doc above.

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
