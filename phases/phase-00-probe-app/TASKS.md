# Phase 00 — Tasks

Work in order. Each task lists the files it creates or touches. Commit after each.

---

## 0.1 — Project scaffold

**Creates:** `src/MyHi.Companion/` (MAUI Android head), `src/MyHi.Companion.Tests/`

- .NET 10 MAUI, **Android only**. Single TFM `net10.0-android`. Do not add other heads —
  conditional compilation is a non-goal.
- `minSdkVersion` **31**, `targetSdkVersion` **36**.
- **The legacy Bluetooth permission path is out of scope.** No `BLUETOOTH`,
  `BLUETOOTH_ADMIN`, `ACCESS_FINE_LOCATION`. It is dead code on this device and an
  extra permission prompt for nothing. Do not add it "just in case".
- Feature-folder layout:
  ```
  Features/Bluetooth/     scan, connect, GATT access
  Features/Diagnostics/   the five probe screens
  Features/Shared/        converters, hex helpers, base VM
  Platforms/Android/
  Data/                   SQLite factory + migrations
  ```
- DI via built-in MAUI container. `CommunityToolkit.Mvvm` for MVVM.
- Shell navigation, dark theme, no splash work (Phase 13).

**Done when:** app builds with zero warnings and launches to a home page listing the
five probe screens.

---

## 0.2 — Permissions and adapter state

**Creates:** `Features/Bluetooth/BluetoothPermissions.cs`, `AndroidManifest.xml` edits

```xml
<uses-permission android:name="android.permission.BLUETOOTH_SCAN"
                 android:usesPermissionFlags="neverForLocation" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
```

`neverForLocation` is correct — the app never derives location from scan results —
and it avoids the location prompt entirely.

- Request at first scan, not at launch.
- Denied → explanatory message plus a button that opens app settings. Never a dead end.
- Adapter off → clear prompt to enable. Never crash, never silently return zero results.

**Done when:** all four states (granted, denied, permanently denied, adapter off)
produce a distinct, useful screen.

---

## 0.3 — Scan screen

**Creates:** `Features/Bluetooth/ScanPage.xaml(.cs)`, `ScanViewModel.cs`,
`Features/Bluetooth/IBleScanner.cs` + implementation

- `Plugin.BLE` (MIT). Rationale in `../../00-Project-Plan.md`; revisit only if a
  reconnect or threading bug can't be reached through it.
- Start/stop scan with a **30 s timeout** and a **debounced button**.
- Device rows: name, MAC, RSSI, live-updating, **de-duplicated by MAC**.
- Filter toggle with three positions: `0x1826` service UUID · `FS-` name prefix · off.
  Three positions rather than one, because **whether `0x1826` is advertised is itself
  an open question** (`../../ASSUMPTIONS.md` A5) and this screen is what answers it.
  Ship the diagnostic with all three; the product later picks one.
- Show the **raw advertisement bytes** for the selected device. This answers A5 and the
  address-type question in one place.
- Long-press a device → "record to capture" with its full advertisement.

**Trap:** Android throttles to ~5 scan starts per 30 s window and then returns nothing
with **no error**. Debounce; never auto-restart in a tight loop.

---

## 0.4 — Connect and service discovery

**Creates:** `Features/Bluetooth/TreadmillConnection.cs`, `Features/Diagnostics/GattTreePage.xaml(.cs)`

Sequence — order matters, see `../../05-FTMS-Protocol.md` §8:

1. Connect with `autoConnect: false`.
2. Wait ~200 ms.
3. `discoverServices()`.
4. Request MTU 517. **Record the negotiated value** — it is an open question (A7).

GATT tree screen shows every service → characteristic → properties
(Read/Write/Notify/Indicate) and every descriptor. Tap a characteristic to act on it.

- Serialise all GATT operations through a single queue. Never issue from arbitrary
  threads.
- Marshal all callbacks to the UI thread once, at this boundary.
- **No auto-reconnect in this phase.** Manual connect/disconnect only. Reconnect logic
  is Phase 02 and mixing them here makes failures unreadable.
- **No bond handling.** Bonding is confirmed not required (`../../DEVICE.md`).

**Trap — GATT 133:** always `close()` the gatt object before reconnecting, not just
`disconnect()`. Expect to see 133 regardless; log every GATT status code numerically.

---

## 0.5 — Read dump screen

**Creates:** `Features/Diagnostics/ReadDumpPage.xaml(.cs)`

- "Dump all" button: read every characteristic with the Read property, across **all**
  services including `1800`, `180A`, `FFE0`, `FFF0`.
- Display `uuid | name-if-known | length | hex bytes`, space-separated, uppercase,
  **leading zeros preserved**. `02 00` and `2 0` are not the same thing and the second
  is unusable as a fixture.
- Per-row copy button and a copy-all button.
- Auto-write every dump into the capture file (task 0.8).

Priority targets, because they are needed as Phase 01 test fixtures:
`0x2ACC`, `0x2AD4`, `0x2AD3`, plus `180A` firmware/model strings for `DEVICE.md`.

**Do not decode any of it.** Hex only.

---

## 0.6 — Notification log screen

**Creates:** `Features/Diagnostics/NotificationLogPage.xaml(.cs)`

- Per-characteristic subscribe toggles for: `0x2ACD`, `0x2ADA`, `0x2AD3`, `0x2A37`
  (Heart Rate Measurement, in service `180D`).
- Live rolling list, newest at top, capped at ~500 rows in the UI while writing **all**
  rows to the capture file.
- Row format: `HH:mm:ss.fff | uuid | len | hex`.
- A **rate counter** per characteristic — packets in the last 10 s, shown as Hz. This
  is the measurement for open question A8; expect ~1 Hz per the FTMS spec, and the
  original "5–10/sec" figure is almost certainly wrong.
- A **flags tracker** for `0x2ACD`: show the distinct set of first-two-byte values seen
  this session, with a count each. If this set has more than one member, the device is
  varying its packet layout mid-session and Phase 01's parser must handle it.
- Pause/resume the display without unsubscribing — the operator needs to read a packet
  while walking.
- Tap a row → "confirm + note" (task 0.8).

**Do not decode.** The flags tracker shows raw two-byte values, not field names.

---

## 0.7 — Control point console ← the centrepiece

**Creates:** `Features/Diagnostics/ControlConsolePage.xaml(.cs)`,
`Features/Bluetooth/ControlPointClient.cs`, `Features/Bluetooth/FtmsCommands.cs`

This is the screen that answers *"what data should be sent to the treadmill"*.

### Mandatory sequence, enforced by the UI

1. **Enable indications** on `0x2AD9` — write `0x0002` to its CCCD `0x2902`. Do this
   automatically on entering the screen, and show whether it succeeded.
2. **Request Control** `00` — a prominent button. Nothing else is enabled until it
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
| Start / Resume | `07` | **Confirmation dialog — this moves the belt** |
| Pause | `08 02` | Parameter byte mandatory |
| Stop | `08 01` | Parameter byte mandatory |

Set Target Speed worked example: 6.5 km/h → 650 → `0x028A` → bytes `02 8A 02`.
Display the computed bytes next to the entry field **before** the send button is
pressed. The operator must be able to see what will go out.

### Free hex field

An entry that accepts arbitrary whitespace-separated hex and sends it verbatim.
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

Result code lookup — the only decoding permitted this phase:

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

- **Write With Response**, always.
- **One outstanding command at a time.** Queue writes; wait for the indication or the
  3 s timeout before sending the next. Concurrent writes get dropped or error, and the
  resulting confusion is indistinguishable from the device not working.
- Round-trip latency measured and displayed — Probe D4 asks for it.
- Every send and every response goes to the capture file with both timestamps.

### Safety

- Confirmation dialog on `07` Start and on `02` Set Target Speed while the belt is
  stopped.
- Permanent banner: *"The physical safety key is the emergency stop. This app is not."*
- Do not offer a "send repeatedly" or macro feature. One command, one deliberate tap.

---

## 0.8 — Capture recorder

**Creates:** `Features/Diagnostics/CaptureRecorder.cs`, `../../captures/` output

### Format — JSONL, one event per line, append-only

```json
{"t":"2026-07-31T14:22:31.402Z","kind":"write","uuid":"2AD9","hex":"02 8A 02"}
{"t":"2026-07-31T14:22:31.588Z","kind":"indicate","uuid":"2AD9","hex":"80 02 01"}
{"t":"2026-07-31T14:22:33.101Z","kind":"notify","uuid":"2ACD","hex":"08 00 ..."}
{"t":"2026-07-31T14:22:40.000Z","kind":"note","ref":"<event id>","ok":true,
 "text":"belt actually sped up, console showed 6.5"}
```

Append-only and flushed per line. A crash must cost the last line at most, never the
file. JSONL rather than JSON for exactly this reason.

- One file per session: `captures/session-YYYY-MM-DD-HHmm.jsonl`.
- **Confirm + note:** any row on any screen can be tapped to attach
  `{ok: true|false, text: "..."}`. This is the "record what is correct" mechanism —
  the operator marks, in the moment, that a specific byte sequence produced the
  intended physical result.
- **Console-value capture:** a dedicated quick-entry (speed / distance / time as shown
  on the treadmill's own display) that writes a `console` event with the current
  timestamp. This is what makes matched console-vs-hex pairs possible, and without at
  least four of those there is no way to prove the Phase 01 parser is correct.
- Share button → Android share sheet (`Share.Default.RequestAsync`). No storage
  permissions needed; goes through FileProvider.
- Session list screen: previous captures, size, event count, share, delete.

---

## 0.9 — Guided probe checklist

**Creates:** `Features/Diagnostics/ProbeChecklistPage.xaml(.cs)`, `ProbeChecklist.json`

Turn `../../05a-FTMS-Probe-Procedure.md` Parts A–G into an in-app form so the operator
is not holding a phone, a treadmill handrail, and a markdown file at the same time.

- One step per screen, ordered, with the procedure's own text.
- Each step's answer fields typed appropriately: yes/no, number, hex, free text.
- Steps that the app can answer itself are **pre-filled from live data** and marked as
  such — negotiated MTU, notification rate, flags seen, raw hex for `0x2ACC` / `0x2AD4`,
  advertisement contents. The operator confirms rather than transcribes.
- Progress persists across app restarts. The session will not be completed in one sitting.
- **Export** → markdown matching the shape of `../../DEVICE.md`, ready to paste, plus
  the raw answers as JSON into the capture folder.

Highest-priority steps, flagged in the UI as blocking:

| Step | Question | Blocks |
|------|----------|--------|
| C7 | Counters per-session or cumulative? | Phase 06 entirely |
| D1–D6 | Does the control point honour commands? | Whether Phase 05 exists |
| C3 | Four matched console-vs-hex pairs | Phase 01 parser correctness |
| G5 | Heart rate: usable / marginal / cut | Phase 03 scope |

---

## 0.10 — Logging

**Creates:** `Features/Shared/Logging.cs`

- `Microsoft.Extensions.Logging` to logcat **and** a rolling file (keep ~5 files).
- Log every GATT callback with its numeric status code. `133` will appear; make it
  greppable rather than a hidden inner-exception string.
- Share log button next to the capture share button.

---

## 0.11 — SQLite factory and migration runner

**Creates:** `Data/SqliteConnectionFactory.cs`, `Data/MigrationRunner.cs`,
`Data/Migrations/` (empty)

Small and cheap now so Phase 06 only writes migrations, not plumbing.

- `Microsoft.Data.Sqlite`. Rationale in `../../00-Project-Plan.md`.
- Apply these PRAGMAs on **every** connection — `foreign_keys` in particular is off by
  default in `Microsoft.Data.Sqlite` and `ON DELETE CASCADE` silently does nothing
  without it:
  ```sql
  PRAGMA journal_mode = WAL;
  PRAGMA foreign_keys = ON;
  PRAGMA synchronous = NORMAL;
  PRAGMA busy_timeout = 5000;
  ```
- `SchemaVersion` table per `../../14-Database.md`. Forward-only, applied at startup
  inside a transaction. Empty migration set is correct for this phase.

**Done when:** the database file exists after first launch and `SchemaVersion` is
queryable.
