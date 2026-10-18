# MyHi Companion

A personal Android app for the MY-HI Q8Y treadmill, to replace the FitShow app. It
connects over Bluetooth, shows live speed and distance, controls the speed, and saves
every workout. It works fully offline: no account, no internet, no cloud.

It's also a learning project. After Phase 00, you write the app's logic yourself and
the AI agent teaches and reviews. [`phases/README.md`](phases/README.md) explains how
that works.

---

## Status

**No app code yet.** The plan was first written for .NET MAUI, then moved to
**Expo (React Native)** in October 2026, before any code existed. You already know
TypeScript and Expo, and C# was new, so Expo is the better fit.

An earlier MAUI version of the Phase 00 test app did run on the treadmill. Its
measurements are still valid and are in
[`PHASE-00-FINDINGS.md`](phases/phase-00-probe-app/PHASE-00-FINDINGS.md).

Next step: Phase 00, starting with
[`phases/phase-00-probe-app/TASKS.md`](phases/phase-00-probe-app/TASKS.md).

---

## What you need

Everything, so nothing surprises you later. Status is as checked on this PC on
2026-10-18. How to set each one up is in
[`docs/learning/01-Device-And-Emulator-Setup.md`](docs/learning/01-Device-And-Emulator-Setup.md).

### Before Phase 00 (required)

| Thing | Why | Status |
|---|---|---|
| Windows PC | Builds the app | ✅ |
| Git | Version control | ✅ |
| VS Code (or any editor) | Writing code | ✅ |
| Node.js 24 LTS | Runs Expo, the bundler and the tests | ✅ v24.17.0 |
| JDK 17 | Gradle needs it to build the Android app | ✅ 17.0.20, `JAVA_HOME` set |
| Android SDK: platform 36, build-tools 36, platform-tools | Building and installing Android apps | ✅ in `%LOCALAPPDATA%\Android\Sdk` |
| `ANDROID_HOME` set, and `adb` on `PATH` | So the build tools can find the SDK | ⚠️ not set yet. One command, in the setup doc |
| The phone (Poco X6 Pro) with Developer options on | Where the app runs | ⬜ |
| Phone: **USB debugging**, **Install via USB**, **USB debugging (Security settings)** | Xiaomi needs all three to install apps from the PC. May need a Mi account and a SIM card | ⬜ |
| A USB cable that carries data | First connection to the phone | ⬜ check it's not charge-only |
| Phone and PC on the same Wi-Fi | The app loads its code from the PC while you develop | ⬜ |

### During Phase 00

| Thing | Why | Status |
|---|---|---|
| Wireless debugging paired with the PC | Screen-off tests must run unplugged (the phone acts differently while charging) | ⬜ |
| **nRF Connect** app on the phone (free, by Nordic) | Double-checks what our app reads over Bluetooth | ⬜ |
| The four HyperOS settings: Autostart on, Battery saver "No restrictions", app locked in Recents, battery optimisation off | Stops HyperOS from killing the app in the background | 1 of 4 done |
| The treadmill and its safety key | The thing we're testing | ✅ |
| Somewhere to stand off the belt | Some tests start the belt from the phone | ✅ |

### Later phases

| Thing | Phase | Why |
|---|---|---|
| A SQLite viewer on the PC, e.g. "DB Browser for SQLite" (free) | 06, 12 | Open the app's database file and check it by hand |
| Google Drive or email on the phone | 09, 14 | Somewhere to send backup files |
| Excel or Google Sheets | 15 | Open the CSV export |
| Any image editor (Figma, Inkscape, Paint.NET) | 13 | App icon and splash image |
| A kitchen timer or a second device | 14 | Timing long tests without unlocking the phone |

### Optional

| Thing | When it helps |
|---|---|
| Android Studio | Easiest way to manage the SDK; its profiler is an extra in Phase 12 |
| An Android emulator (x86_64 image) | Looking at screens without the phone. It can't do Bluetooth |
| Battery Historian | A battery timeline in Phase 12, if the drain looks bad |
| VS Code extensions: ESLint, Expo Tools | Lint errors and `app.json` help inside the editor |

### Not needed

An Expo account, EAS cloud builds, a Mac, or a Play Store developer account. Everything
builds on this PC and installs straight onto the phone.

---

## The device

The MY-HI Q8Y is a folding treadmill. Its Bluetooth chip is made by FitShow, and it
speaks the standard fitness-machine protocol (FTMS, service `0x1826`) on top of
FitShow's own one. That FTMS layer doesn't always tell the truth about itself: it
claims the machine has an incline, and it doesn't. So this project checks everything
against bytes actually captured from the machine, not against the spec.

The byte-level details are in
[`phases/phase-01-protocol-decode/README.md`](phases/phase-01-protocol-decode/README.md).

---

## Tech stack

What the app and its tools use, what each piece is for, and the phase that adds it.
Versions are from October 2026. `npx expo install` picks matching versions of the
Expo packages for you.

### Platform and framework

| Piece | Version | What it's for | Added in |
|-------|---------|---------------|----------|
| Android | min SDK 31 (Android 12), target SDK 36 | The only platform. Phone: Poco X6 Pro, Android 16 / HyperOS | 00 |
| Expo SDK | 58 | Builds the app, manages Android settings, provides most libraries | 00 |
| React Native | 0.88 | Runs your TypeScript on the phone and shows real Android screens | 00 |
| React | 19.3 | Components and hooks | 00 |
| TypeScript | strict mode, plus `noUncheckedIndexedAccess` | The language for everything except one Kotlin file group | 00 |
| Kotlin | comes with the Android tools | The background service (Phase 07) | 00, 07 |
| Development build | via `expo-dev-client` | Your own version of Expo Go that includes the Bluetooth library. Expo Go itself can't run this app | 00 |

### Libraries in the app

| Library | What it's for | Added in |
|---------|---------------|----------|
| [`react-native-ble-manager`](https://github.com/innoveit/react-native-ble-manager) | Bluetooth: scan, connect, read, write, notifications | 00 |
| `expo-router` | Moving between screens. Each screen is a file in `src/app/` | 00 |
| `expo-build-properties` | Sets the Android SDK versions from `app.json` | 00 |
| `expo-sqlite` | The workout database | 00, 06 |
| `expo-sqlite/kv-store` | Small saved values, like settings | 00, 08 |
| `expo-file-system` | Log files, capture files, backup files, file pickers | 00, 09 |
| `expo-sharing` | The Android share sheet | 00, 09 |
| `expo-clipboard` | Copying hex values on the test screens | 00 |
| `react-native-safe-area-context` | Keeps screens clear of the status bar. Comes with Expo Router | 00 |
| `expo-crypto` | Creates unique workout IDs | 06 |
| `fflate` | Creates and reads backup ZIP files | 09 |
| `react-native-svg` | Draws the charts | 10 |
| `expo-haptics` | One small vibration when a speed change is confirmed | 13 |
| `expo-keep-awake` | Keeps the screen on during a workout (if you turn that setting on) | 13 |
| `expo-splash-screen` | The splash screen | 13 |

### Code in this repo that runs on Android directly

| Piece | What it's for | Added in |
|-------|---------------|----------|
| `src/modules/recording-service/` (Kotlin) | A background service that keeps the app and its timers running while the screen is off, and shows the workout notification | 00 (test version), 07 |
| `src/plugins/withResizeableActivity.js` | Turns on split-screen support in the Android settings Expo generates | 11 |

### Tools for building and testing

| Tool | What it's for | Added in |
|------|---------------|----------|
| Metro | Sends your code to the phone while you develop, and updates it when you save | 00 |
| Jest + `jest-expo` | Automated tests | 00 |
| `node:sqlite` (built into Node) | A real database for tests, with no phone | 00 |
| ESLint (`npx expo lint`) | Catches mistakes, including importing phone-only code into `src/core/` | 00 |
| `tsc --noEmit` | Type checking | 00 |
| React Native DevTools | Debugger and memory snapshots (press `j` while `npx expo start` runs) | 00, 12 |
| `adb` | Installs the app, shows logs, reads phone state | 00 |

### Deliberately not used

No state library (Zustand, Redux), no UI kit (Paper, Tamagui), no ORM (Drizzle), no
chart library, no Expo Go, no cloud builds, no server. Each one was considered and
didn't earn its place. Each phase's "Technology decision" section says why, starting
with [Phase 00's](phases/phase-00-probe-app/README.md#technology-decisions).

New to how Expo works under the hood? Read
[`00-Expo-And-React-Native.md`](docs/learning/00-Expo-And-React-Native.md). Want the
React and TypeScript patterns explained properly? Read
[`00a-React-And-TypeScript-Essentials.md`](docs/learning/00a-React-And-TypeScript-Essentials.md).

---

## Folders

Phase 00 creates `src/`. It doesn't exist yet.

```
src/                           the Expo project (package.json, app.json)
├── app/                       screens: one file per screen
├── core/                      plain TypeScript: Bluetooth decoding, workout logic,
│                              database code, backup. No phone-only imports, so it
│                              can be tested on the PC
├── features/                  React code: hooks, components, and the code that
│                              talks to Bluetooth, files and notifications
├── modules/recording-service/ Kotlin: the background service
└── android/                   made by Expo when you build. Never edit it, never
                               commit it

phases/                        one folder per phase: what to build next and why
docs/learning/                 guides: how Expo works, setup, glossary, theme
captures/                      raw Bluetooth logs from the treadmill
```

---

## Building and running

All commands run from `src/`, once Phase 00 has created it.

Check the code:

```powershell
cd src
npm install
npx tsc --noEmit      # types: must show no errors
npx expo lint         # lint: must show no errors or warnings
npm test              # tests
```

Build the app and install it on the phone:

```powershell
npx expo run:android --device
```

You only rebuild when something native changes (a new library with Android code, or
`app.json`). For normal code changes, run `npx expo start`. The app on the phone
updates when you save, like Expo Go did.

Build the release version, which runs without the PC:

```powershell
npx expo run:android --variant release --device
```

Use the release version for battery tests, screen-off tests and daily use. The APK
lands in `src/android/app/build/outputs/apk/release/`. It's signed with Expo's
built-in test key, which is fine for a personal app. Keep using the same key: Android
won't install an update signed with a different one, and uninstalling first deletes
your data (that's why Phase 09 adds backups).

The emulator can't use Bluetooth, so it's only for checking screens.

## Safety

The app can start the belt. **The physical safety key is the emergency stop, not
this app.** Every button that can move the belt asks you to confirm first.

## Not in scope

Cloud sync, social features, multiple users, iOS, Strava/Garmin export, training
plans, coaching. This is a personal, offline app for one phone and one treadmill.

## Risks

[`phases/README.md`](phases/README.md#risk-register) lists what's most likely to go
wrong, and the plan for each.
