# Phase 00 — Tasks

Work in order. Each task lists the files it creates or touches (paths relative to
the repo root; `src/` is the Expo project root). Commit after each.

---

## 0.1: Project scaffold

**Creates:** `src/` (the Expo project), `src/eslint.config.js`, `src/jest.config.js`,
`src/features/shared/theme/`, `src/features/services.tsx`

```powershell
npx create-expo-app@latest src --template default
cd src
npm run reset-project          # moves the template's example screens out; delete app-example/
npx expo install expo-dev-client expo-build-properties expo-sqlite expo-file-system expo-sharing expo-clipboard
npm install react-native-ble-manager
npx expo install jest-expo jest @types/jest --dev
npx expo lint                  # first run installs and configures ESLint
```

- **Expo SDK 58, TypeScript, Android only.** In `app.json`: `"platforms": ["android"]`,
  `"orientation": "default"` (the dashboard must survive rotation), `android.package`
  `"com.myhi.companion"`, `name` `"MyHi Companion"`.
- **`minSdkVersion` 31, `targetSdkVersion` 36**, through the `expo-build-properties`
  plugin in `app.json` (never by editing Gradle):
  ```json
  ["expo-build-properties", { "android": { "minSdkVersion": 31, "targetSdkVersion": 36 } }]
  ```
- **The legacy Bluetooth permission path is out of scope.** No code for `BLUETOOTH`,
  `BLUETOOTH_ADMIN`, or location permissions; they're dead on this device and an
  extra prompt for nothing. (The ble-manager config plugin still writes legacy
  entries into the manifest, capped at `maxSdkVersion 30`. They're inert at
  `minSdkVersion 31`; leave them, don't write code for them. See 0.2.)
- **New Architecture** is always on in SDK 55+. Nothing to configure.
- TypeScript: the template already sets `"strict": true`. Add
  `"noUncheckedIndexedAccess": true` to `tsconfig.json`. With it, `bytes[i]` has type
  `number | undefined`, so "never read past the buffer" becomes something the
  compiler checks in every parser, not a rule you have to remember.
- Folder layout:
  ```
  src/app/                 Expo Router screens (home + diagnostics/*)
  src/core/ftms/           byte-level FTMS: hex helpers, command builder, response decoder
  src/core/capture/        capture event types + JSONL line formatting (pure)
  src/core/data/           Db interface, migration runner, testing/ adapter
  src/core/shared/         tiny utilities (createEmitter)
  src/features/bluetooth/  ble-manager adapter, scanner, connection, permissions
  src/features/diagnostics/ hooks + components for the probe screens
  src/features/capture/    the capture recorder (file I/O)
  src/features/data/       expo-sqlite adapter, app database opener
  src/features/shared/     theme, shared components, logger
  src/modules/             local Expo modules (0.12)
  ```
- **The `core/` import rule**, in `eslint.config.js`: files under `core/**` may not
  import `react`, `react-native`, `expo`, `expo-*`, `@expo/*` or `react-native-*`.
  Use `no-restricted-imports` with `patterns`. This is the mechanical guarantee that
  everything in `core/` runs (and is tested) in plain Node.
- **Jest** with two projects in `jest.config.js`: `core` (`preset: 'jest-expo/node'`,
  `testMatch: ['<rootDir>/core/**/*.test.ts']`) and `app` (`preset: 'jest-expo'`,
  everything else). `"test": "jest"` in `package.json`.
- **Composition root**: `src/features/services.tsx` creates the app's long-lived
  objects once, at module scope (not inside a component, so they survive any
  screen remounting), and exposes them through a React context
  (`ServicesProvider`, `useServices()`). Phase 00 puts the BLE client, the capture
  recorder and the database in it. Later phases add to it; swapping a fake for a
  real implementation is one line here.
- **Monochrome theme**: implement `docs/learning/04-Monochrome-Theme.md` in
  `src/features/shared/theme/` (tokens, `useTheme()`, and the base components
  `Screen`, `Text`, `Surface`, `Button`, `SecondaryButton`, `MetricTile`,
  `ConnectionDot`). It follows the system light/dark setting; Phase 08 adds an override.
- Wrap the root layout (`src/app/_layout.tsx`) in React's `<StrictMode>`. In
  development it mounts, unmounts and remounts every component once, which flushes
  out effects that forget to clean up (Phase 03 leans on this). It has no effect in
  release builds.
- Home screen (`src/app/index.tsx`) lists the six probe screens.

**Done when:** `npx expo run:android --device` installs the dev build on the phone
and it launches to the home list; `npx tsc --noEmit`, `npx expo lint` and `npm test`
are all clean; a deliberate `import { View } from 'react-native'` in a `core/` file
fails lint.

---

## 0.2: Permissions and adapter state

**Creates:** `src/features/bluetooth/bluetoothPermissions.ts`, `app.json` plugin entry

```json
["react-native-ble-manager", { "isBleRequired": true, "neverForLocation": true }]
```

`neverForLocation` makes the plugin write
`android:usesPermissionFlags="neverForLocation"` on `BLUETOOTH_SCAN`. That's correct:
the app never derives location from scan results, and it avoids the location prompt
entirely. **Check it in the generated manifest** (`npx expo prebuild`, then read
`src/android/app/src/main/AndroidManifest.xml`) rather than trusting the option
name.

- Request at first scan, not at launch:
  `PermissionsAndroid.requestMultiple([BLUETOOTH_SCAN, BLUETOOTH_CONNECT])`.
- Denied → explanatory message plus a button that opens app settings
  (`Linking.openSettings()`). A result of `never_ask_again` is "permanently denied":
  the system won't show the prompt again, so the settings button is the only way
  forward. Never a dead end.
- Adapter off → clear prompt to enable (`BleManager.enableBluetooth()` shows the
  system dialog). Track adapter state with `BleManager.checkState()` and the
  `onDidUpdateState` event. Never crash, never silently return zero results.
- Call `BleManager.start({ showAlert: false })` once, from the composition root.

**Done when:** all four states (granted, denied, permanently denied, adapter off)
produce a distinct, useful screen.

---

## 0.3: Scan screen

**Creates:** `src/app/diagnostics/scan.tsx`, `src/features/bluetooth/bleScanner.ts`,
`src/features/diagnostics/useScan.ts`

- `react-native-ble-manager` (MIT). Rationale in `README.md`'s Technology decisions
  section; revisit only if a reconnect or background bug can't be reached through it.
- Start/stop scan with a **30 s timeout** (`BleManager.scan({ seconds: 30, ... })`)
  and a **debounced button**.
- Device rows from `onDiscoverPeripheral`: name, id (the MAC on Android), RSSI,
  live-updating, **de-duplicated by id**. Scan with `allowDuplicates: true` so RSSI
  keeps updating; de-duplicate in your own `Map`.
- Filter toggle with three positions: `0x1826` service UUID (passed as
  `serviceUUIDs`) · `FS-` name prefix (filtered in TypeScript) · off.
  Three positions rather than one, because **whether `0x1826` is advertised is itself
  an open question** (`PHASE-00-FINDINGS.md` V4) and this screen is what answers it.
  Ship the diagnostic with all three; the product later picks one.
- Show the **raw advertisement bytes** for the selected device
  (`peripheral.advertising.rawData.bytes`, as hex). This answers V4. The **address
  type** (public vs. random) isn't exposed by the library; the runbook reads it from
  nRF Connect instead.
- Long-press a device → "record to capture" with its full advertisement.

**Trap:** Android throttles to ~5 scan starts per 30 s window and then returns nothing
with **no error**. Debounce; never auto-restart in a tight loop.

---

## 0.4: Connect and service discovery

**Creates:** `src/features/bluetooth/bleClient.ts`,
`src/features/bluetooth/treadmillConnection.ts`, `src/app/diagnostics/gatt-tree.tsx`

`bleClient.ts` is the only file that imports `react-native-ble-manager` (plus
`bleScanner.ts`). It normalises UUIDs to the capture format (`2ACD`), converts
`number[]` to `Uint8Array`, and logs every call and result.

Sequence (order matters, see `README.md`'s "Connection sequence" reference):

1. `connect(id, { autoconnect: false })`.
2. Wait ~200 ms.
3. `retrieveServices(id)` (discovery).
4. `requestMTU(id, 517)`. **Record the negotiated value**; it is an open question (A7).

GATT tree screen shows every service → characteristic → properties
(Read/Write/Notify/Indicate) and every descriptor. Tap a characteristic to act on it.

- `treadmillConnection.ts` is a small state machine:
  `disconnected → connecting → discovering → ready`, with an `onStateChange`
  subscription. It serialises its own calls with a promise chain, so two taps can't
  start two connects.
- No threading to think about: all of your TypeScript runs on React Native's one
  JavaScript thread, and native BLE events are delivered to it one at a time.
- **No auto-reconnect in this phase.** Manual connect/disconnect only. Reconnect logic
  is Phase 02 and mixing them here makes failures unreadable.
- **No bond handling.** Bonding is confirmed not required (`PHASE-00-FINDINGS.md`).

**Trap, GATT 133:** ble-manager already closes the GATT object on every disconnect
(check: `Peripheral.java`'s `onConnectionStateChange`), which is the main mitigation.
Expect to see 133 regardless; log every disconnect event's numeric `status`.

---

## 0.5: Read dump screen

**Creates:** `src/app/diagnostics/read-dump.tsx`, `src/features/diagnostics/useReadDump.ts`

- "Dump all" button: read every characteristic with the Read property, across **all**
  services including `1800`, `180A`, `FFE0`, `FFF0`, one at a time.
- Display `uuid | name-if-known | length | hex bytes`, space-separated, uppercase,
  **leading zeros preserved**. `02 00` and `2 0` are not the same thing and the second
  is unusable as a fixture.
- Per-row copy button and a copy-all button (`expo-clipboard`).
- Auto-write every dump into the capture file (task 0.8).

Priority targets, because they are needed as Phase 01 test fixtures:
`0x2ACC`, `0x2AD4`, `0x2AD3`, plus `180A` firmware/model strings for `PHASE-00-FINDINGS.md`.

**Do not decode any of it.** Hex only.

---

## 0.6: Notification log screen

**Creates:** `src/app/diagnostics/notification-log.tsx`,
`src/features/diagnostics/useNotificationLog.ts`

- Per-characteristic subscribe toggles for: `0x2ACD`, `0x2ADA`, `0x2AD3`, `0x2A37`
  (Heart Rate Measurement, in service `180D`), via `startNotification` /
  `stopNotification` and the `onDidUpdateValueForCharacteristic` event.
- Live rolling list (`FlatList`), newest at top, capped at ~500 rows in the UI while
  writing **all** rows to the capture file.
- Row format: `HH:mm:ss.SSS | uuid | len | hex`.
- A **rate counter** per characteristic: packets in the last 10 s, shown as Hz. This
  is the measurement for open question A8; expect ~1 Hz per the FTMS spec, and the
  original "5–10/sec" figure is almost certainly wrong.
- A **flags tracker** for `0x2ACD`: show the distinct set of first-two-byte values seen
  this session, with a count each. If this set has more than one member, the device is
  varying its packet layout mid-session and Phase 01's parser must handle it.
- A **gap meter** (feeds task 0.12 / runbook Part H): the largest gap between two
  consecutive `0x2ACD` packets this session, and the largest gap between two
  heartbeat ticks (0.12). Both reset with a button.
- Pause/resume the display without unsubscribing. The operator needs to read a packet
  while walking.
- Tap a row → "confirm + note" (task 0.8).

**Do not decode.** The flags tracker shows raw two-byte values, not field names.

---

## 0.7: Control point console ← the centrepiece

**Creates:** `src/app/diagnostics/control-console.tsx`,
`src/features/bluetooth/controlPointClient.ts`, `src/core/ftms/ftmsCommands.ts`,
`src/core/ftms/controlPointResponse.ts`, `src/core/ftms/hex.ts`

This is the screen that answers *"what data should be sent to the treadmill"*.

### Mandatory sequence, enforced by the UI

1. **Enable indications** on `0x2AD9`: `startNotification` on it (ble-manager writes
   `0x0002` to the CCCD for an indicate-only characteristic). Do this automatically
   on entering the screen, and show whether it succeeded.
2. **Request Control** `00`, a prominent button. Nothing else is enabled until it
   returns a result, and the raw response is displayed either way.
3. Everything else.
4. Re-issue Request Control after any reconnect and after any `0xFF`
   (control permission lost) event on `0x2ADA`.

### Preset command buttons

| Button | Bytes | Notes |
|--------|-------|-------|
| Request Control | `00` | Must succeed first |
| Reset | `01` | |
| Set Target Speed | `02 LL HH` | uint16 LE, 0.01 km/h. Numeric entry in km/h; show the bytes before sending |
| Start / Resume | `07` | **Confirmation dialog: this moves the belt** |
| Pause | `08 02` | Parameter byte mandatory |
| Stop | `08 01` | Parameter byte mandatory |

Set Target Speed worked example: 6.5 km/h → 650 → `0x028A` → bytes `02 8A 02`.
Display the computed bytes next to the entry field **before** the send button is
pressed. The operator must be able to see what will go out.

### Free hex field

A text input that accepts arbitrary whitespace-separated hex and sends it verbatim.
No clamping, no validation beyond "is this parseable hex". This is deliberate: the
whole point is to discover what the shim accepts, including malformed and
out-of-range input, which is Probe Part D5.

### Response panel

For each write, log and display:

```
→ 02 8A 02                       (sent 14:22:31.402)
← 80 02 01                       (indication 14:22:31.588, +186 ms)
   Response Code   0x80
   Request Op      0x02  Set Target Speed
   Result          0x01  Success
```

Result code lookup, the only decoding permitted this phase:

| Result | Meaning |
|--------|---------|
| `0x01` | Success |
| `0x02` | Op Code not supported |
| `0x03` | Invalid Parameter |
| `0x04` | Operation Failed |
| `0x05` | Control Not Permitted |

If no indication arrives within **3 s**, log a timeout row explicitly. Silence is a
finding, not a missing log line.

### Implementation requirements

- **Write With Response** (`BleManager.write`), always.
- **One outstanding command at a time.** `controlPointClient.ts` queues writes on a
  promise chain and resolves each one on its indication (matched by the echoed op
  code) or on the 3 s timeout, before starting the next. Concurrent writes get dropped
  or error, and the resulting confusion is indistinguishable from the device not
  working.
- Round-trip latency measured and displayed. Probe D4 asks for it.
- Every send and every response goes to the capture file with both timestamps.

### Safety

- Confirmation dialog (`Alert.alert` with Cancel/Confirm) on `07` Start and on `02`
  Set Target Speed while the belt is stopped.
- Permanent banner: *"The physical safety key is the emergency stop. This app is not."*
- Do not offer a "send repeatedly" or macro feature. One command, one deliberate tap.

---

## 0.8: Capture recorder

**Creates:** `src/core/capture/captureEvent.ts`, `src/features/capture/captureRecorder.ts`,
`src/app/diagnostics/captures.tsx`, `../../captures/` output

### Format: JSONL, one event per line, append-only

```json
{"id":41,"t":"2026-07-31T14:22:31.402Z","kind":"write","uuid":"2AD9","hex":"02 8A 02"}
{"id":42,"t":"2026-07-31T14:22:31.588Z","kind":"indicate","uuid":"2AD9","hex":"80 02 01"}
{"id":43,"t":"2026-07-31T14:22:33.101Z","kind":"notify","uuid":"2ACD","hex":"08 00 ..."}
{"id":44,"t":"2026-07-31T14:22:40.000Z","kind":"note","ref":42,"ok":true,
 "text":"belt actually sped up, console showed 6.5"}
```

Append-only and written per line. A crash must cost the last line at most, never the
file. JSONL rather than JSON for exactly this reason.

- `captureEvent.ts` (pure, in `core/`) defines the event types as a discriminated
  union on `kind` and formats one line; it's what the tests cover.
- `captureRecorder.ts` appends each line with `file.write(line + '\n', { append: true })`
  (`expo-file-system`'s `File`). Each call opens, writes and closes, so a line is on
  disk the moment the call returns.
- `id` is a per-session counter, so a `note` can point at the row it annotates.
- One file per session: `<documents>/captures/session-YYYY-MM-DD-HHmm.jsonl`.
- **Confirm + note:** any row on any screen can be tapped to attach
  `{ok: true|false, text: "..."}`. This is the "record what is correct" mechanism:
  the operator marks, in the moment, that a specific byte sequence produced the
  intended physical result.
- **Console-value capture:** a dedicated quick-entry (speed / distance / time as shown
  on the treadmill's own display) that writes a `console` event with the current
  timestamp. This is what makes matched console-vs-hex pairs possible, and without at
  least four of those there is no way to prove the Phase 01 parser is correct.
- Share button → Android share sheet (`Sharing.shareAsync(file.uri)`). No storage
  permissions needed.
- Session list screen: previous captures, size, event count, share, delete.

---

## 0.9: Guided probe checklist

**Creates:** `src/app/diagnostics/probe-checklist.tsx`,
`src/features/diagnostics/probeChecklist.json`, `src/features/diagnostics/useProbeChecklist.ts`

Turn `HUMAN-RUNBOOK.md` Parts A–H into an in-app form so the operator is not holding
a phone, a treadmill handrail, and a markdown file at the same time.

- One step per screen, ordered, with the procedure's own text.
- Each step's answer fields typed appropriately: yes/no, number, hex, free text.
- Steps that the app can answer itself are **pre-filled from live data** and marked as
  such: negotiated MTU, notification rate, flags seen, raw hex for `0x2ACC` / `0x2AD4`,
  advertisement contents, the gap meter's numbers. The operator confirms rather than
  transcribes.
- Progress persists across app restarts (`expo-sqlite/kv-store`, one JSON value). The
  session will not be completed in one sitting.
- **Export** → markdown matching the shape of `PHASE-00-FINDINGS.md`, ready to paste, plus
  the raw answers as JSON into the capture folder.

Highest-priority steps, flagged in the UI as blocking:

| Step | Question | Blocks |
|------|----------|--------|
| C7 | Counters per-session or cumulative? | Phase 06 entirely |
| D1–D6 | Does the control point honour commands? | Whether Phase 05 exists |
| C3 | Four matched console-vs-hex pairs | Phase 01 parser correctness |
| G5 | Heart rate: usable / marginal / cut | Phase 03 scope |
| H2–H3 | Does the keep-alive hold with the screen off and in split screen? | Whether BLE stays in TypeScript (Phase 07 design) |

---

## 0.10: Logging

**Creates:** `src/features/shared/logger.ts`

- Every log line goes to `console` (visible in `adb logcat` under the `ReactNativeJS`
  tag) **and** a rolling file under `<documents>/logs/` (keep ~5 files of ~1 MB).
- Log every BLE call, result and disconnect with its numeric status:
  `GATT disconnect status=133`. Make it greppable rather than a hidden string.
- Share log button next to the capture share button.
- Document the two logcat filters that matter, in the home screen's footer:
  `adb logcat -s ReactNativeJS RNBleManager`.

---

## 0.11: SQLite opener and migration runner

**Creates:** `src/core/data/db.ts`, `src/core/data/migrationRunner.ts`,
`src/core/data/migrations.ts` (empty list), `src/core/data/testing/nodeSqliteDb.ts`,
`src/features/data/expoDb.ts`, `src/features/data/openAppDatabase.ts`

Small and cheap now so Phase 06 only writes migrations, not plumbing.

- `db.ts`: the seam both sides implement.
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
- `expoDb.ts` wraps one `expo-sqlite` connection (`openDatabaseAsync('myhi.db')`) and
  pushes **every** call through a single serial queue. `transaction()` holds the queue
  for its whole duration (`BEGIN` … `COMMIT`, `ROLLBACK` on throw) and hands `work` a
  `tx` that bypasses the queue. Two reasons, both real:
  1. `expo-sqlite`'s `withTransactionAsync` lets unrelated queries interleave into
     your transaction across an `await`.
  2. Its `withExclusiveTransactionAsync` avoids that by opening a **second
     connection**, which never received the PRAGMAs below, so `ON DELETE CASCADE`
     would silently do nothing inside it.
- `nodeSqliteDb.ts` implements the same interface over `node:sqlite`'s
  `DatabaseSync`, for tests only. It's never imported by app code, so Metro never
  bundles it.
- Apply these PRAGMAs right after opening. The app has exactly one connection
  (above), so "every connection" means this one place. `foreign_keys` in particular is
  off by default in SQLite and `ON DELETE CASCADE` silently does nothing without it:
  ```sql
  PRAGMA journal_mode = WAL;
  PRAGMA foreign_keys = ON;
  PRAGMA synchronous = NORMAL;
  PRAGMA busy_timeout = 5000;
  ```
- `SchemaVersion` table per `../phase-06-recording-schema/README.md`. Forward-only,
  applied at startup inside a transaction. Empty migration set is correct for this
  phase.
- Tests (`migrationRunner.test.ts`, against `nodeSqliteDb`): `SchemaVersion` exists
  after a run, a second run applies nothing, and a cascade-delete check proves
  `foreign_keys` is on.

**Done when:** the database file exists after first launch and `SchemaVersion` is
queryable; the migration runner tests pass.

---

## 0.12: Keep-alive spike ← the platform question

**Creates:** `src/modules/recording-service/` (local Expo module, Kotlin),
`src/features/background/keepAlive.ts`, `src/index.ts` (custom entry point)

This is a deliberately minimal version of Phase 07's foreground service, built here
so runbook Part H can answer "does JavaScript keep running with the screen off?" on
the real treadmill. Phase 07 takes it over and finishes it. Read
`../../docs/learning/00-Expo-And-React-Native.md`'s "background" section first; it
explains the mechanism in plain terms.

**Why it's needed at all.** React Native's timer module stops firing `setTimeout` /
`setInterval` once the activity is paused (screen off, app backgrounded) **unless a
headless JS task is running**. BLE events still reach JavaScript, but anything that
waits on a timer (the control point's 3 s timeout, Phase 02's reconnect backoff,
Phase 06's flush timer) stalls until the screen comes back on. This was confirmed as
recently as SDK 57 in expo/expo#51304, where a headless task that finished early let
the timers stop. So the spike needs two things: a foreground service (so Android
doesn't kill the process) and a headless JS task that **never finishes** until
recording stops (so the timers keep running).

```powershell
cd src
npx create-expo-module@latest --local recording-service
```

- **`WorkoutRecordingService.kt`** extends React Native's `HeadlessJsTaskService`.
  That one base class does three jobs: it starts the JS task named in
  `getTaskConfig()` (`"RecordingKeepAlive"`, timeout `0` = none, allowed in
  foreground), it holds a **partial wake lock** while the task runs (CPU stays on
  through Doze), and it stops itself when the task finishes. In `onStartCommand`,
  post the notification and call
  `ServiceCompat.startForeground(this, id, notification, FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)`
  *before* delegating to `super.onStartCommand`.
- **Module manifest** (`src/modules/recording-service/android/src/main/AndroidManifest.xml`,
  merged into the app at build time): `FOREGROUND_SERVICE`,
  `FOREGROUND_SERVICE_CONNECTED_DEVICE`, `WAKE_LOCK`, `POST_NOTIFICATIONS`, and the
  `<service android:foregroundServiceType="connectedDevice" android:exported="false">`
  entry.
- **`RecordingServiceModule.kt`** (Expo Modules API) exposes `start(title, text)` and
  `stop()` to TypeScript. Notification: one low-importance channel, a fixed title and
  "Keep-alive spike". No actions yet; those are Phase 07.
- **`keepAlive.ts`** registers the headless task with
  `AppRegistry.registerHeadlessTask('RecordingKeepAlive', ...)`, whose function returns
  a promise that only resolves when `stopKeepAlive()` is called. That never-settling
  promise *is* the keep-alive.
- **`src/index.ts`** becomes the entry point (`"main": "index.ts"` in `package.json`):
  it imports `keepAlive.ts` (so the task is registered before anything else runs),
  then `expo-router/entry`.
- **A 5-second heartbeat** `setInterval` that writes `{"kind":"heartbeat"}` to the
  capture file. Gaps in heartbeats show timers stalled; gaps in `0x2ACD` notifies
  show BLE delivery stalled. Part H needs both numbers.
- A "Keep-alive" toggle on the Notification Log screen starts and stops it.
  `POST_NOTIFICATIONS` is requested from that tap
  (`PermissionsAndroid.request(PERMISSIONS.POST_NOTIFICATIONS)`). On Android 14+,
  starting a `connectedDevice` service also requires `BLUETOOTH_CONNECT` to be
  granted already, or `startForeground` throws, so the toggle is disabled until 0.2's
  permissions are granted.

**Done when:** with the toggle on and the screen locked for two minutes,
`adb shell dumpsys activity services com.myhi.companion` lists the service as a
foreground service, and the capture file shows heartbeats at ~5 s intervals
straight through the locked period. (Run that check unplugged over wireless adb:
Android doesn't enter Doze while charging.)
