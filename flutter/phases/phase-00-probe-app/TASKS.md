# Phase 00 — Tasks (Flutter track)

Work in order. Each task lists the files it creates or touches. Commit after
each. Same eleven deliverables as the original track's `TASKS.md`; this file
differs only in *how*, per Flutter's own idiom rather than a reskin of the
MAUI project shape.

---

## 0.1: Project scaffold

**Creates:** `flutter/myhi_companion/` (the app), `flutter/packages/myhi_companion_core/` (pure Dart package)

- `flutter create --platforms=android myhi_companion` inside `flutter/`. Do
  not add other platforms — Android only, per the root non-goals. Delete
  `ios/`, `linux/`, `macos/`, `windows/`, `web/` if `flutter create` still
  generated them (recent Flutter versions accept `--platforms` and skip them
  entirely; confirm before deleting).
- `minSdkVersion` **31**, `targetSdkVersion` **36** in
  `android/app/build.gradle`.
- **The legacy Bluetooth permission path is out of scope.** No `BLUETOOTH`,
  `BLUETOOTH_ADMIN`, `ACCESS_FINE_LOCATION`. Do not add it "just in case."
- Create the pure Dart package: `dart create --template=package
  myhi_companion_core` inside `flutter/packages/`. Add it as a path
  dependency in `myhi_companion/pubspec.yaml`:
  ```yaml
  dependencies:
    myhi_companion_core:
      path: ../packages/myhi_companion_core
  ```
- Feature-first layout inside `myhi_companion/lib/`:
  ```
  features/
    bluetooth/     scan, connect, GATT access
    diagnostics/    the five probe screens
    shared/         theme, shared widgets, router config
  main.dart
  ```
  and inside `myhi_companion_core/lib/`:
  ```
  treadmill/    the ITreadmillService seam (Phase 01b)
  ftms/         parsers (Phase 01a) — empty for now
  capture/      the JSONL capture format
  data/         SQLite factory + migrations
  ```
- `flutter_riverpod` for state/DI (`ProviderScope` wrapping `main.dart`'s
  `runApp`), `go_router` for navigation, dark theme only (no splash work —
  Phase 13 equivalent, not yet written for this track).

**Done when:** `flutter run` builds with zero analyzer warnings and launches
to a home screen listing the five probe screens.

---

## 0.2: Permissions and adapter state

**Creates:** `features/bluetooth/bluetooth_permissions.dart`,
`android/app/src/main/AndroidManifest.xml` edits

```xml
<uses-permission android:name="android.permission.BLUETOOTH_SCAN"
                 android:usesPermissionFlags="neverForLocation" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
```

`neverForLocation` is correct: the app never derives location from scan
results, and it avoids the location prompt entirely.

- Use [`permission_handler`](https://pub.dev/packages/permission_handler) to
  request `Permission.bluetoothScan` / `Permission.bluetoothConnect` at first
  scan, not at launch.
- Denied → explanatory message plus a button that calls
  `openAppSettings()`. Never a dead end.
- Adapter off → `flutter_blue_plus`'s `FlutterBluePlus.adapterState` stream;
  show a clear prompt to enable. Never crash, never silently return zero
  results.

**Done when:** all four states (granted, denied, permanently denied, adapter
off) produce a distinct, useful screen.

---

## 0.3: Scan screen

**Creates:** `features/bluetooth/scan_screen.dart`,
`features/bluetooth/scan_notifier.dart`, `features/bluetooth/ble_scanner.dart`

- `flutter_blue_plus`. Rationale in `README.md`'s Technology decisions
  section.
- A `Notifier`/`StateNotifier` (Riverpod) owns scan state: start/stop with a
  **30 s timeout** and a **debounced button** — same trap as the original
  track, Android throttles scan starts regardless of framework.
- Device rows: name, MAC, RSSI, live-updating, **de-duplicated by MAC**.
- Filter toggle with three positions: `0x1826` service UUID · `FS-` name
  prefix · off. Three positions rather than one, because **whether `0x1826`
  is advertised is itself an open question**
  (`../../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md` V4) and this
  screen is what answers it.
- Show the **raw advertisement bytes** for the selected device
  (`ScanResult.advertisementData` in `flutter_blue_plus`).
- Long-press a device → "record to capture" with its full advertisement.

**Trap:** Android throttles to ~5 scan starts per 30 s window and then
returns nothing with **no error**. Debounce; never auto-restart in a tight
loop.

---

## 0.4: Connect and service discovery

**Creates:** `features/bluetooth/treadmill_connection.dart`,
`features/diagnostics/gatt_tree_screen.dart`

Sequence (order matters, see the connection sequence reference in
`../../../phases/phase-00-probe-app/README.md`):

1. `device.connect(autoConnect: false)`.
2. Wait ~200 ms.
3. `device.discoverServices()`.
4. `device.requestMtu(517)`. **Record the negotiated value**; open question A7.

GATT tree screen shows every service → characteristic → properties
(Read/Write/Notify/Indicate) and every descriptor. Tap a characteristic to
act on it.

- Serialise all GATT operations through a single queue (an `async` lock, e.g.
  a simple `Future`-chaining mutex) — Dart's single-threaded event loop
  avoids the "arbitrary thread" callback problem MAUI/Kotlin BLE code has to
  guard against explicitly, but concurrent *awaited* GATT calls can still
  race at the platform-channel level, so still serialise.
- **No auto-reconnect in this phase.** Manual connect/disconnect only.
  Reconnect logic is Phase 02.
- **No bond handling.** Confirmed not required
  (`PHASE-00-FINDINGS.md`).

**Trap, GATT 133:** always `device.disconnect()` **and** let the plugin fully
tear down before reconnecting; expect to see 133 regardless — log every GATT
status code numerically via `flutter_blue_plus`'s connection state stream.

---

## 0.5: Read dump screen

**Creates:** `features/diagnostics/read_dump_screen.dart`

- "Dump all" button: read every characteristic with the Read property, across
  **all** services including `1800`, `180A`, `FFE0`, `FFF0`.
- Display `uuid | name-if-known | length | hex bytes`, space-separated,
  uppercase, **leading zeros preserved**.
- Per-row copy button (`Clipboard.setData`) and a copy-all button.
- Auto-write every dump into the capture file (task 0.8).

Priority targets, needed as Phase 01 test fixtures: `0x2ACC`, `0x2AD4`,
`0x2AD3`, plus `180A` firmware/model strings for `PHASE-00-FINDINGS.md`.

**Do not decode any of it.** Hex only.

---

## 0.6: Notification log screen

**Creates:** `features/diagnostics/notification_log_screen.dart`

- Per-characteristic subscribe toggles for: `0x2ACD`, `0x2ADA`, `0x2AD3`,
  `0x2A37` (Heart Rate Measurement, service `180D`). `flutter_blue_plus`
  exposes each subscribed characteristic's value as a `Stream<List<int>>`.
- Live rolling list, newest at top, capped at ~500 rows in the UI (a
  `ListView.builder` with a bounded backing list) while writing **all** rows
  to the capture file.
- Row format: `HH:mm:ss.fff | uuid | len | hex`.
- A **rate counter** per characteristic: packets in the last 10 s, shown as
  Hz — the measurement for open question A8.
- A **flags tracker** for `0x2ACD`: distinct set of first-two-byte values
  seen this session, with a count each.
- Pause/resume the display without unsubscribing.
- Tap a row → "confirm + note" (task 0.8).

**Do not decode.** The flags tracker shows raw two-byte values, not field
names.

---

## 0.7: Control point console ← the centrepiece

**Creates:** `features/diagnostics/control_console_screen.dart`,
`features/bluetooth/control_point_client.dart`, `features/bluetooth/ftms_commands.dart`

Full mandatory-sequence, preset buttons, free hex field, and response panel
spec is identical to the original track's — see
`../../../phases/phase-00-probe-app/README.md`'s Control point section and
`../../../phases/phase-00-probe-app/TASKS.md` task 0.7 for the exact command
table, worked example, and result-code lookup; nothing about the wire
protocol changes with the framework.

Implementation notes specific to this track:

- Enable indications by subscribing to `0x2AD9` (`characteristic.setNotifyValue(true)`
  writes the CCCD `0x2902` automatically in `flutter_blue_plus`) on entering
  the screen, and show whether it succeeded.
- **One outstanding command at a time.** Queue writes with an `async` mutex;
  wait for the indication (`Stream` event) or a 3 s timeout
  (`Future.any([indicationFuture, Future.delayed(...)])`) before sending the
  next.
- **Write With Response**: `characteristic.write(bytes, withoutResponse: false)`.
- Round-trip latency measured and displayed.
- Confirmation dialog (`showDialog`) on `07` Start and on `02` Set Target
  Speed while the belt is stopped.

---

## 0.8: Capture recorder

**Creates:** `packages/myhi_companion_core/lib/capture/capture_recorder.dart`,
`../../../captures/` output (**shared with the original track — one folder**)

Format: JSONL, one event per line, append-only, identical schema to the
original track's (see `../../../phases/phase-00-probe-app/TASKS.md` task
0.8) — this is a data format, not framework-specific, and both tracks should
be able to read each other's capture files.

- Lives in `myhi_companion_core` (pure Dart, no Flutter dependency): file I/O
  via `dart:io`'s `File.openWrite(mode: FileMode.writeOnlyAppend)`, flushed
  per line. A crash must cost the last line at most, never the file.
- One file per session: `captures/session-YYYY-MM-DD-HHmm.jsonl`. Use
  [`path_provider`](https://pub.dev/packages/path_provider) from the app side
  to resolve a writable directory, pass the resolved path into the
  `myhi_companion_core` recorder — keeps the pure package platform-agnostic.
- **Confirm + note:** any row on any screen taps to attach
  `{ok: true|false, text: "..."}`.
- **Console-value capture:** a dedicated quick-entry writes a `console`
  event, for matched console-vs-hex pairs.
- Share button → [`share_plus`](https://pub.dev/packages/share_plus), the
  Flutter equivalent of MAUI's `Share.Default.RequestAsync`, same
  FileProvider-backed share sheet under the hood, no storage permissions
  needed.
- Session list screen: previous captures, size, event count, share, delete.

---

## 0.9: Guided probe checklist

**Creates:** `features/diagnostics/probe_checklist_screen.dart`,
`probe_checklist.json` (bundled asset)

Turns `../../../phases/phase-00-probe-app/HUMAN-RUNBOOK.md` Parts A–G into an
in-app form, identical structure and priority steps (C7, D1–D6, C3, G5) to
the original track — see that file for the exact procedure text.

- One step per screen (`PageView` or `go_router` sub-routes), ordered.
- Steps the app can answer itself are **pre-filled from live data**:
  negotiated MTU, notification rate, flags seen, raw hex for `0x2ACC` /
  `0x2AD4`, advertisement contents.
- Progress persists across app restarts —
  [`shared_preferences`](https://pub.dev/packages/shared_preferences) is
  enough for form-answer persistence at this size.
- **Export** → markdown matching `PHASE-00-FINDINGS.md`'s shape, ready to
  paste, plus the raw answers as JSON into the capture folder.

---

## 0.10: Logging

**Creates:** `features/shared/logging.dart`

- The [`logging`](https://pub.dev/packages/logging) package to stdout (visible
  via `flutter logs` / `adb logcat`, since Flutter's own print output goes
  there) **and** a rolling file (keep ~5 files) via `dart:io`.
- Log every GATT callback/stream event with its numeric status code. `133`
  will appear; make it greppable.
- Share log button next to the capture share button (`share_plus`).

---

## 0.11: SQLite factory and migration runner

**Creates:** `packages/myhi_companion_core/lib/data/sqlite_connection_factory.dart`,
`packages/myhi_companion_core/lib/data/migration_runner.dart`,
`packages/myhi_companion_core/lib/data/migrations/` (empty)

Small and cheap now so Phase 06 only writes migrations, not plumbing.

- [`sqflite`](https://pub.dev/packages/sqflite). Rationale in `../../../README.md`'s
  root Stack table (this track's flutter/README.md) and Phase 06's technology
  decision section once that phase is written for this track.
- Apply these PRAGMAs on **every** connection — `sqflite`, like
  `Microsoft.Data.Sqlite`, does not turn on `foreign_keys` by default, and
  `ON DELETE CASCADE` silently does nothing without it:
  ```sql
  PRAGMA journal_mode = WAL;
  PRAGMA foreign_keys = ON;
  PRAGMA synchronous = NORMAL;
  PRAGMA busy_timeout = 5000;
  ```
- `SchemaVersion` table, forward-only, applied at startup inside a
  transaction — schema itself is unchanged from
  `../../../phases/phase-06-recording-schema/README.md`. Empty migration set
  is correct for this phase.

**Done when:** the database file exists after first launch and
`SchemaVersion` is queryable.
