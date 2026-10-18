# Phase 00 — Tasks

Do these in order. Each task lists the files it creates (paths from the repo root;
`src/` is the Expo project). Commit after each one.

---

## 0.1: Project setup

**Creates:** `src/` (the Expo project), `src/eslint.config.js`, `src/jest.config.js`,
`src/features/shared/theme/`, `src/features/services.tsx`

```powershell
npx create-expo-app@latest src --template default
cd src
npm run reset-project          # removes the template's example screens; delete app-example/
npx expo install expo-dev-client expo-build-properties expo-sqlite expo-file-system expo-sharing expo-clipboard
npm install react-native-ble-manager
npx expo install jest-expo jest @types/jest --dev
npx expo lint                  # the first run sets up ESLint
```

- **Expo 58, TypeScript, Android only.** In `app.json`: `"platforms": ["android"]`,
  `"orientation": "default"` (the dashboard must handle rotation), `android.package`
  `"com.myhi.companion"`, `name` `"MyHi Companion"`.
- **Min SDK 31, target SDK 36**, through the `expo-build-properties` plugin in
  `app.json` (never by editing Gradle files):
  ```json
  ["expo-build-properties", { "android": { "minSdkVersion": 31, "targetSdkVersion": 36 } }]
  ```
- **No code for old-Android Bluetooth permissions** (`BLUETOOTH`, `BLUETOOTH_ADMIN`,
  location). They don't apply on this phone. The Bluetooth library's plugin still adds
  some of them to the manifest, limited to old Android versions. They do nothing at
  min SDK 31, so leave them alone (see 0.2).
- **TypeScript:** the template already uses `"strict": true`. Add
  `"noUncheckedIndexedAccess": true` to `tsconfig.json`. Then `bytes[i]` has the type
  `number | undefined`, so the compiler forces a length check in every parser.
- **Folders:**
  ```
  src/app/                  screens (home + diagnostics/*)
  src/core/ftms/            hex helpers, command builder, reply decoder
  src/core/capture/         capture event types + JSON line formatting
  src/core/data/            Db interface, migration runner, testing/ adapter
  src/core/shared/          small helpers (createEmitter)
  src/features/bluetooth/   the Bluetooth library wrapper, scanner, connection, permissions
  src/features/diagnostics/ hooks and parts for the test screens
  src/features/capture/     the capture recorder (writes files)
  src/features/data/        the phone's database adapter and opener
  src/features/shared/      theme, shared components, logger
  src/modules/              Kotlin modules (0.12)
  ```
- **The `core/` rule**, in `eslint.config.js`: files in `core/**` can't import
  `react`, `react-native`, `expo`, `expo-*`, `@expo/*` or `react-native-*`. Use
  `no-restricted-imports` with `patterns`. This guarantees everything in `core/` can
  be tested on the PC.
- **Jest** with two projects in `jest.config.js`: `core` (`preset: 'jest-expo/node'`,
  `testMatch: ['<rootDir>/core/**/*.test.ts']`) and `app` (`preset: 'jest-expo'`, the
  rest). Add `"test": "jest"` to `package.json`.
- **Services file** (`src/features/services.tsx`): creates the app's long-lived
  objects once, at the top level of the file (not inside a component, so they survive
  screen changes), and shares them through React context (`ServicesProvider`,
  `useServices()`). Phase 00 adds the Bluetooth client, the capture recorder and the
  database. Later phases add more. Swapping a fake for the real thing is one line here.
- **Theme:** build [`04-Monochrome-Theme.md`](../../docs/learning/04-Monochrome-Theme.md)
  in `src/features/shared/theme/`: the tokens, `useTheme()`, and the components
  `Screen`, `Text`, `Surface`, `Button`, `SecondaryButton`, `MetricTile`,
  `ConnectionDot`. It follows the phone's light/dark setting; Phase 08 adds an
  override.
- **Strict Mode:** wrap the root layout (`src/app/_layout.tsx`) in `<StrictMode>`. In
  development it mounts every component twice, which shows up effects that forget to
  clean up. It does nothing in release builds.
- **Home screen** (`src/app/index.tsx`): a list of the six test screens.

**Done when:** `npx expo run:android --device` installs the app and it opens to the
list; `npx tsc --noEmit`, `npx expo lint` and `npm test` pass; adding
`import { View } from 'react-native'` to a `core/` file makes lint fail.

---

## 0.2: Permissions and Bluetooth on/off

**Creates:** `src/features/bluetooth/bluetoothPermissions.ts`, an `app.json` plugin entry

```json
["react-native-ble-manager", { "isBleRequired": true, "neverForLocation": true }]
```

`neverForLocation` tells Android the app never uses Bluetooth scans to find your
location, so there's no location prompt. **Check the generated manifest** to be sure:
run `npx expo prebuild`, then look for `usesPermissionFlags="neverForLocation"` on
`BLUETOOTH_SCAN` in `src/android/app/src/main/AndroidManifest.xml`.

- Ask for permission at the first scan, not at launch:
  `PermissionsAndroid.requestMultiple([BLUETOOTH_SCAN, BLUETOOTH_CONNECT])`.
- Denied → explain why it's needed and show a button that opens app settings
  (`Linking.openSettings()`). A result of `never_ask_again` means Android won't show
  the prompt again, so the settings button is the only way. Never leave a dead end.
- Bluetooth off → ask to turn it on (`BleManager.enableBluetooth()` shows the system
  dialog). Track the state with `BleManager.checkState()` and the `onDidUpdateState`
  event. Never crash; never just show zero results.
- Call `BleManager.start({ showAlert: false })` once, in the services file.

**Done when:** granted, denied, permanently denied and Bluetooth-off each show a
different, useful screen.

---

## 0.3: Scan screen

**Creates:** `src/app/diagnostics/scan.tsx`, `src/features/bluetooth/bleScanner.ts`,
`src/features/diagnostics/useScan.ts`

- Start/stop scanning, with a **30 second limit** (`BleManager.scan({ seconds: 30 })`)
  and a button that can't be spammed.
- Rows from `onDiscoverPeripheral`: name, id (the MAC address on Android), signal
  strength, updating live, **one row per device**. Scan with
  `allowDuplicates: true` so the signal keeps updating, and keep one entry per id in a
  `Map`.
- A three-way filter: `0x1826` service · `FS-` name prefix · off. Three, because
  **whether the treadmill advertises `0x1826` is still unknown** (Finding V4), and this
  screen finds out. The real app will use just one.
- Show the **raw advertisement bytes** for the selected device
  (`peripheral.advertising.rawData.bytes`, as hex). That answers V4. The library
  doesn't show the **address type** (public or random), so the runbook reads that
  from nRF Connect.
- Long-press a device → save its advertisement to the capture file.

**Trap:** Android allows about 5 scan starts every 30 seconds, then silently returns
nothing. Never restart scans in a loop.

---

## 0.4: Connect and discover services

**Creates:** `src/features/bluetooth/bleClient.ts`,
`src/features/bluetooth/treadmillConnection.ts`, `src/app/diagnostics/gatt-tree.tsx`

`bleClient.ts` is the only file (with `bleScanner.ts`) that imports the Bluetooth
library. It converts UUIDs to the `2ACD` form, turns `number[]` into `Uint8Array`, and
logs every call and result.

In this order (see the connection diagram in `README.md`):

1. `connect(id, { autoconnect: false })`.
2. Wait about 200 ms.
3. `retrieveServices(id)`.
4. `requestMTU(id, 517)`. **Record the size you get back**; it's an open question.

The service list screen shows every service → characteristic → what it supports
(Read/Write/Notify/Indicate) and its descriptors. Tap a characteristic to use it.

- `treadmillConnection.ts` is a small state machine: `disconnected → connecting →
  discovering → ready`, with `onStateChange(listener)`. It chains its calls one after
  another, so two taps can't start two connections.
- There are no threads to manage: all your TypeScript runs on one JavaScript thread,
  and Bluetooth events arrive one at a time.
- **No automatic reconnect here.** Connect and disconnect by hand only. Reconnecting
  is Phase 02.
- **No pairing.** The treadmill doesn't need it.

**Trap, GATT 133:** the library already closes the old connection on every disconnect
(see `Peripheral.java`, `onConnectionStateChange`). 133 will still happen sometimes.
Log every disconnect's `status` number.

---

## 0.5: Read dump screen

**Creates:** `src/app/diagnostics/read-dump.tsx`, `src/features/diagnostics/useReadDump.ts`

- "Dump all": read every readable characteristic in **every** service, including
  `1800`, `180A`, `FFE0` and `FFF0`, one at a time.
- Show `uuid | name if known | length | hex`. Hex is uppercase, space-separated, and
  **keeps leading zeros**: `02 00`, never `2 0`.
- A copy button per row, and a "copy all" button (`expo-clipboard`).
- Write every dump into the capture file (0.8).

Most important: `0x2ACC`, `0x2AD4`, `0x2AD3`, and the `180A` firmware/model text. These
become Phase 01's test data.

**Don't decode anything.** Hex only.

---

## 0.6: Notification log screen

**Creates:** `src/app/diagnostics/notification-log.tsx`,
`src/features/diagnostics/useNotificationLog.ts`

- On/off switches for `0x2ACD`, `0x2ADA`, `0x2AD3` and `0x2A37` (heart rate, in
  service `180D`), using `startNotification`/`stopNotification` and the
  `onDidUpdateValueForCharacteristic` event.
- A live list (`FlatList`), newest first, showing at most ~500 rows. **Every** row
  still goes to the capture file.
- Row format: `HH:mm:ss.SSS | uuid | length | hex`.
- **Rate**: packets per second for each characteristic, over the last 10 seconds.
  Expect about 1 per second.
- **Flags tracker** for `0x2ACD`: the different values of the first two bytes seen
  this session, with a count each. More than one value means the packet layout
  changes, and Phase 01's parser must handle that.
- **Gap meter** (for 0.12 and runbook Part H): the longest gap between two `0x2ACD`
  packets, and the longest gap between two heartbeat ticks. A button resets both.
- Pause the list without unsubscribing, so you can read a packet while walking.
- Tap a row → confirm + note (0.8).

**Don't decode.** The flags tracker shows the raw two bytes, not field names.

---

## 0.7: Control console (the main screen)

**Creates:** `src/app/diagnostics/control-console.tsx`,
`src/features/bluetooth/controlPointClient.ts`, `src/core/ftms/ftmsCommands.ts`,
`src/core/ftms/controlPointResponse.ts`, `src/core/ftms/hex.ts`

This screen answers *"what should be sent to the treadmill"*.

### The required order, enforced by the screen

1. **Turn on replies** for `0x2AD9` with `startNotification`, automatically when the
   screen opens. Show whether it worked.
2. A big **Request Control** (`00`) button. Nothing else is enabled until it returns
   a result. Show the raw reply either way.
3. Then everything else.
4. Request control again after any reconnect, and after a `0xFF` ("control lost")
   event on `0x2ADA`.

### Preset buttons

| Button | Bytes | Notes |
|--------|-------|-------|
| Request Control | `00` | Must succeed first |
| Reset | `01` | |
| Set Target Speed | `02 LL HH` | Type km/h; show the bytes before sending |
| Start / Resume | `07` | **Asks for confirmation: this moves the belt** |
| Pause | `08 02` | Second byte required |
| Stop | `08 01` | Second byte required |

Example: 6.5 km/h → 650 → `0x028A` → `02 8A 02`. Show the bytes next to the input
**before** the send button is pressed.

### Free hex box

Sends whatever hex you type, unchanged. No limits, no checks except "is it valid
hex". On purpose: the point is to find out what the treadmill accepts, including bad
input (runbook step D5).

### Reply panel

For each command, show and log:

```
→ 02 8A 02                       (sent 14:22:31.402)
← 80 02 01                       (reply 14:22:31.588, +186 ms)
   Response Code   0x80
   Request Op      0x02  Set Target Speed
   Result          0x01  Success
```

| Result | Meaning |
|--------|---------|
| `0x01` | Success |
| `0x02` | Command not supported |
| `0x03` | Invalid value |
| `0x04` | Operation failed |
| `0x05` | Control not allowed |

No reply within **3 seconds** → log a "timeout" row. No reply is a finding too.

### How it works

- Always `BleManager.write` (write with response).
- **One command at a time.** `controlPointClient.ts` queues commands, and finishes
  each one when its reply arrives (matched by the command byte) or after 3 seconds,
  before starting the next.
- Show the round-trip time (runbook step D4 asks for it).
- Every command and reply goes into the capture file with both times.

### Safety

- Confirmation dialog (`Alert.alert`) before `07` Start, and before `02` Set Target
  Speed while the belt is stopped.
- Always show: *"The physical safety key is the emergency stop. This app is not."*
- No "repeat" or macro buttons. One tap, one command.

---

## 0.8: Capture recorder

**Creates:** `src/core/capture/captureEvent.ts`, `src/features/capture/captureRecorder.ts`,
`src/app/diagnostics/captures.tsx`, files in `../../captures/`

### Format: one JSON object per line, only ever appended

```json
{"id":41,"t":"2026-07-31T14:22:31.402Z","kind":"write","uuid":"2AD9","hex":"02 8A 02"}
{"id":42,"t":"2026-07-31T14:22:31.588Z","kind":"indicate","uuid":"2AD9","hex":"80 02 01"}
{"id":43,"t":"2026-07-31T14:22:33.101Z","kind":"notify","uuid":"2ACD","hex":"08 00 ..."}
{"id":44,"t":"2026-07-31T14:22:40.000Z","kind":"note","ref":42,"ok":true,
 "text":"belt actually sped up, console showed 6.5"}
```

One line at a time, so a crash loses at most the last line, never the file.

- `captureEvent.ts` (in `core/`) defines the event types and turns one event into one
  line. That's what the tests check.
- `captureRecorder.ts` adds each line with `file.write(line + '\n', { append: true })`
  (`expo-file-system`). The line is on disk as soon as the call returns.
- `id` counts up within a session, so a note can point at the row it's about.
- One file per session: `<documents>/captures/session-YYYY-MM-DD-HHmm.jsonl`.
- **Confirm + note:** tap any row on any screen to add `{ok, text}`. This is how you
  record "this exact byte sequence did the right thing".
- **Console values:** a quick form for what the treadmill's own display shows (speed,
  distance, time). It saves a `console` line with the current time. Phase 01 needs at
  least four of these to prove the decoder right.
- Share button → the Android share sheet (`Sharing.shareAsync(file.uri)`).
- A list of past sessions: size, number of lines, share, delete.

---

## 0.9: Guided checklist

**Creates:** `src/app/diagnostics/probe-checklist.tsx`,
`src/features/diagnostics/probeChecklist.json`, `src/features/diagnostics/useProbeChecklist.ts`

`HUMAN-RUNBOOK.md` Parts A–H as an in-app form, so you're not juggling a phone, a
handrail and a document.

- One step per screen, in order, with the runbook's own wording.
- Answer fields of the right type: yes/no, number, hex, text.
- Steps the app can answer itself are **filled in automatically** and marked: packet
  size, rate, flags seen, raw `0x2ACC`/`0x2AD4` hex, advertisement, gap meter numbers.
  You confirm instead of copying.
- Progress is saved (`expo-sqlite/kv-store`), so you can stop and continue later.
- **Export** → markdown in the same shape as `PHASE-00-FINDINGS.md`, ready to paste,
  plus the raw answers as JSON in the captures folder.

Steps marked as blocking:

| Step | Question | Blocks |
|------|----------|--------|
| C7 | Do the counters reset per session? | Phase 06 |
| D1–D6 | Does the treadmill obey commands? | Whether Phase 05 happens |
| C3 | Four console-vs-hex readings | Phase 01 |
| G5 | Heart rate: usable, marginal or cut | Phase 03 |
| H2–H3 | Does the keep-alive hold with the screen off and in split screen? | Phase 07's design |

---

## 0.10: Log files

**Creates:** `src/features/shared/logger.ts`

- Every log line goes to `console` (shows in `adb logcat` as `ReactNativeJS`) **and**
  to a rotating set of files in `<documents>/logs/` (about 5 files of 1 MB each).
- Log every Bluetooth call, result and disconnect with its status number, in a form
  you can search: `GATT disconnect status=133`.
- A "share log" button next to the capture share button.
- Show the two useful logcat filters at the bottom of the home screen:
  `adb logcat -s ReactNativeJS RNBleManager`.

---

## 0.11: Database setup

**Creates:** `src/core/data/db.ts`, `src/core/data/migrationRunner.ts`,
`src/core/data/migrations.ts` (empty list), `src/core/data/testing/nodeSqliteDb.ts`,
`src/features/data/expoDb.ts`, `src/features/data/openAppDatabase.ts`

Done now so that Phase 06 only has to add tables.

- `db.ts`, the interface both versions follow:
  ```ts
  export type SqlValue = string | number | null;
  export type SqlParams = Record<string, SqlValue>;   // keys like "$workoutId"
  export interface RunResult { lastInsertRowId: number; changes: number }
  export interface Statement { run(params: SqlParams): Promise<RunResult>; finalize(): Promise<void> }
  export interface Db {
    exec(sql: string): Promise<void>;
    run(sql: string, params?: SqlParams): Promise<RunResult>;
    all<T>(sql: string, params?: SqlParams): Promise<T[]>;
    first<T>(sql: string, params?: SqlParams): Promise<T | null>;
    prepare(sql: string): Promise<Statement>;
    transaction<T>(work: (tx: Db) => Promise<T>): Promise<T>;
  }
  ```
- `expoDb.ts` wraps **one** `expo-sqlite` connection (`openDatabaseAsync('myhi.db')`)
  and runs every call through a single queue. `transaction()` holds the queue until it
  finishes (`BEGIN` … `COMMIT`, or `ROLLBACK` on error) and gives `work` a `tx` that
  skips the queue. Two reasons:
  1. `expo-sqlite`'s `withTransactionAsync` lets other queries slip into your
     transaction while it's waiting.
  2. Its `withExclusiveTransactionAsync` avoids that by opening a **second
     connection**, which doesn't get the settings below, so cascade deletes silently
     stop working inside it.
- `nodeSqliteDb.ts` is the same interface using Node's `node:sqlite`, for tests only.
  App code never imports it, so it never ends up in the app.
- Apply these settings right after opening. There's only one connection, so this is
  the only place:
  ```sql
  PRAGMA journal_mode = WAL;
  PRAGMA foreign_keys = ON;   -- off by default; without it, ON DELETE CASCADE does nothing
  PRAGMA synchronous = NORMAL;
  PRAGMA busy_timeout = 5000;
  ```
- The migration runner keeps a `SchemaVersion` table (see Phase 06) and applies each
  migration once, in order, inside a transaction. No migrations yet.
- Tests (`migrationRunner.test.ts`, using `nodeSqliteDb`): `SchemaVersion` exists
  after one run; a second run does nothing; a cascade-delete check proves
  `foreign_keys` is on.

**Done when:** the database file exists after the first launch, `SchemaVersion` can
be queried, and the tests pass.

---

## 0.12: Keep-alive test (the phone question)

**Creates:** `src/modules/recording-service/` (Kotlin), `src/features/background/keepAlive.ts`,
`src/index.ts` (new entry point)

A minimal version of Phase 07's background service, so runbook Part H can answer
"does the app keep running with the screen off?" on the real treadmill. Phase 07
finishes it. Read the "screen off" section of
[`00-Expo-And-React-Native.md`](../../docs/learning/00-Expo-And-React-Native.md) first.

**Why it's needed.** React Native stops running `setTimeout`/`setInterval` when the
screen is off or the app is in the background, **unless a "headless JS task" is
running**. Bluetooth data still arrives, but anything that waits on a timer stalls:
the control point's 3-second timeout, Phase 02's reconnect delays, Phase 06's save
timer. A recent Expo bug report (expo/expo#51304) shows exactly this happening when a
headless task ended too early. So the fix has two parts:

- a **foreground service**, so Android doesn't kill the app, and
- a **headless JS task that never finishes** until recording stops, so the timers
  keep running.

```powershell
cd src
npx create-expo-module@latest --local recording-service
```

- **`WorkoutRecordingService.kt`** extends React Native's `HeadlessJsTaskService`.
  That one class does three jobs: it starts the JavaScript task named in
  `getTaskConfig()` (`"RecordingKeepAlive"`, timeout `0` = never), it keeps the CPU
  awake while the task runs (a "partial wake lock"), and it stops itself when the task
  ends. In `onStartCommand`, show the notification and call
  `ServiceCompat.startForeground(this, id, notification, FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)`
  *before* calling `super.onStartCommand`.
- **The module's manifest** (`src/modules/recording-service/android/src/main/AndroidManifest.xml`,
  merged into the app when it builds): the permissions `FOREGROUND_SERVICE`,
  `FOREGROUND_SERVICE_CONNECTED_DEVICE`, `WAKE_LOCK` and `POST_NOTIFICATIONS`, and a
  `<service android:foregroundServiceType="connectedDevice" android:exported="false">`
  entry.
- **`RecordingServiceModule.kt`** gives TypeScript two functions: `start(title, text)`
  and `stop()`. The notification is simple for now: one quiet channel, a fixed title,
  the text "Keep-alive test". No buttons yet; those are Phase 07.
- **`keepAlive.ts`** registers the task with
  `AppRegistry.registerHeadlessTask('RecordingKeepAlive', ...)`. The task returns a
  promise that only resolves when `stopKeepAlive()` is called. That unfinished
  promise *is* the keep-alive.
- **`src/index.ts`** becomes the app's entry point (`"main": "index.ts"` in
  `package.json`). It imports `keepAlive.ts` first (so the task is registered before
  anything else), then `expo-router/entry`.
- **A heartbeat:** a 5-second `setInterval` that writes `{"kind":"heartbeat"}` to the
  capture file. Gaps in the heartbeat mean timers stopped. Gaps in `0x2ACD` packets
  mean Bluetooth data stopped. Part H needs both.
- **A "Keep-alive" switch** on the notification log screen starts and stops it. That
  tap also asks for `POST_NOTIFICATIONS`
  (`PermissionsAndroid.request(PERMISSIONS.POST_NOTIFICATIONS)`). On Android 14+, a
  `connectedDevice` service can only start once `BLUETOOTH_CONNECT` is already granted,
  so the switch stays disabled until 0.2's permissions are done.

**Done when:** with the switch on and the screen locked for two minutes,
`adb shell dumpsys activity services com.myhi.companion` shows the service running in
the foreground, and the capture file shows a heartbeat about every 5 seconds the
whole time. Do this check unplugged, over wireless `adb`: the phone behaves
differently while charging.
