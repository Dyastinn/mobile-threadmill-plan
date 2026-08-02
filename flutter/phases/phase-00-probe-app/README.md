# Phase 00 — Probe App (Flutter track)

> Same goal as the original track's Phase 00: make the treadmill answer
> questions, not build a product. Every screen here is a lab instrument, none
> of it ships in the final UI, and none of it needs to be pretty. It needs to
> be *correct about bytes*.

**Hardware:** required, at the end · **Size:** L · **Blocks:** everything

---

## Goal

Produce an installable Android app that, on the operator's phone, can:

1. Find the treadmill and connect to it.
2. Show every service and characteristic, and dump every readable one as raw hex.
3. Subscribe to every notifying characteristic and log each packet as
   `timestamp | uuid | hex`.
4. **Send arbitrary bytes to the control point `0x2AD9` and show the raw
   response**, both via preset command buttons and a free-text hex field.
5. **Record what happened**: export the session as a file, and let the
   operator mark an observation as confirmed-correct with a note.

Identical goal, identical deliverable shape, to
[`../../../phases/phase-00-probe-app/README.md`](../../../phases/phase-00-probe-app/README.md).
Only the implementation stack differs.

## Reference docs

Read these. This folder does not repeat protocol facts already written down
elsewhere:

- [`../../../phases/phase-00-probe-app/README.md`](../../../phases/phase-00-probe-app/README.md)
  — the protocol reference (characteristics, control point commands and
  response format, connection sequence). The tables below are copied from
  there because Phase 00's tasks reference them constantly; the source of
  truth is that file.
- [`../../../phases/phase-00-probe-app/HUMAN-RUNBOOK.md`](../../../phases/phase-00-probe-app/HUMAN-RUNBOOK.md)
  — the manual procedure the Probe Checklist screen automates.
- [`../../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](../../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md)
  — what is already measured. Everything marked NEEDED is your target.
- [`../../../README.md`](../../../README.md) — the non-goals and safety rules.

### FTMS characteristics

| UUID | Name | Properties |
|------|------|------------|
| `0x2ACC` | Fitness Machine Feature | Read |
| `0x2ACD` | Treadmill Data | Notify |
| `0x2AD3` | Training Status | Read, Notify |
| `0x2AD4` | Supported Speed Range | Read |
| `0x2AD9` | Fitness Machine Control Point | Write, Indicate |
| `0x2ADA` | Machine Status | Notify |

Plus `1800` Generic Access, `180A` Device Information, `180D` Heart Rate, and
the vendor `FFE0`/`FFF0` transparent-serial services (recorded only).

### Control point (`0x2AD9`) commands

Mandatory sequence, same as the original track — omitting any step is the
usual cause of silent failure:

1. **Enable indications** on `0x2AD9` (write `0x0002` to CCCD `0x2902`).
2. **Write `Request Control` (`00`)** and wait for a successful indication.
3. Only then send any other command. Use **Write With Response**.
4. **Serialise commands**: one outstanding, wait for the indication
   (3 s timeout) before the next.

| Command | Bytes | Notes |
|---------|-------|-------|
| Request Control | `00` | Must succeed first |
| Reset | `01` | |
| Set Target Speed | `02 LL HH` | uint16 LE, 0.01 km/h |
| Set Target Inclination | `03 LL HH` | sint16, 0.1 % — not applicable, no incline |
| Start or Resume | `07` | |
| Stop or Pause | `08 01` (stop) / `08 02` (pause) | **Parameter byte is mandatory** |

---

## Technology decisions

> Verified against package status as of August 2026. This project has one
> developer, indefinitely, working on Linux. Every dependency here is
> justified on that basis, not theoretical best-practice.

### Flutter (Android-only)

**What problem does it solve?** The app needs a native-feeling Android UI,
background BLE handling that survives screen lock, and local SQLite storage,
developed on a Linux workstation with a fast, reliable edit loop.

**Why are we using it, over the original plan's .NET MAUI pick?** The
original decision record (still in
[`../../../phases/phase-00-probe-app/README.md#technology-decisions`](../../../phases/phase-00-probe-app/README.md#technology-decisions))
picked MAUI specifically because the developer already knew C# and MAUI
avoided learning a new language *and* UI toolkit *and* BLE story
simultaneously. That reasoning held for a Windows/macOS dev host. It doesn't
hold on Linux: MAUI's Hot Reload is IDE-provided (Visual Studio on Windows,
Visual Studio for Mac), and even JetBrains Rider — a genuinely cross-platform
IDE — documents Hot Reload support for Windows, macOS, and iOS only; Linux is
absent from its own support table. Building/deploying still works from the
CLI on Linux, but every UI change becomes a full rebuild-and-redeploy cycle,
indefinitely — a recurring daily cost, not the one-time cost of learning Dart.

Flutter's hot reload is implemented in the Flutter engine itself, not an IDE
feature, so it works identically on Linux, macOS, and Windows — full DevTools,
widget inspector, and reload, in VS Code or Android Studio, on Linux, out of
the box.

**Alternatives considered:**

1. **.NET MAUI (the original pick)** — genuinely the better choice if
   developing on Windows or macOS: zero new language, and this project's
   Android-specific needs are narrow enough that MAUI's platform escape
   hatches cover them. Rejected specifically and only because of the Linux
   Hot Reload gap above, confirmed against current JetBrains documentation,
   not a general MAUI weakness.
2. **Native Android (Kotlin + Jetpack Compose)** — full Linux tooling parity
   (Android Studio is Linux-native), zero abstraction between you and the
   platform. Rejected for the same reason it lost the first time: a new
   language *and* UI framework learned at the same time as BLE/FTMS, which is
   already the hard part of this project. Worth reconsidering only if
   Flutter's BLE story turns out to be the weak link, not the UI layer.
3. **React Native** — JavaScript/TypeScript, Linux-native tooling, but BLE
   support is entirely third-party and historically inconsistent across
   Android versions, and this project has no existing JS investment to offset
   learning a new language, the same argument that ruled it out originally.

**Why not the alternatives?** MAUI loses on a concrete, checkable, and
current fact (Rider's own Hot Reload table), not a vague "worse ecosystem"
claim. Kotlin/Compose is the one alternative with a real argument (maximum
platform control, native Linux tooling) but re-introduces the "two new things
at once" problem Flutter avoids by at least keeping the BLE plugin story
(`flutter_blue_plus`) mature and widely used. React Native was already the
weakest of the three original alternatives and Linux tooling doesn't change
that.

**Long-term considerations.** Flutter is Google-maintained, has a large
independent community, and its BLE plugin ecosystem (`flutter_blue_plus`
specifically) is actively maintained. Performance is adequate for this app's
actual workload (a dashboard updating at ≤4 Hz, not a game) — the same bar the
MAUI decision record used.

**Practical example:** `flutter/myhi_companion/`, once Task 0.1 scaffolds it.

### `flutter_blue_plus`

**What problem does it solve?** Raw Android BLE (`BluetoothGatt` and its
callback interface) is verbose and callback-heavy, and GATT error 133 /
connect-timing quirks are the single biggest source of undocumented
device-specific pain in any BLE app. Something has to own that plumbing.

**Why are we using it?** It's the most widely used, actively maintained
Flutter BLE plugin, with the GATT 133 mitigations this specific treadmill
needs (`autoConnect: false`-equivalent behavior, close-before-reconnect,
delay-before-discovery) achievable through its API. It becomes the foundation
of `TreadmillConnection`/`BleScanner`/`ControlPointClient` (Task 0.3 onward),
same role `Plugin.BLE` played in the original track.

**Alternatives considered:**

1. **`flutter_reactive_ble`** — another actively maintained option, a more
   explicitly reactive (`Stream`-based) API. Genuinely competitive;
   `flutter_blue_plus`'s API shape (imperative calls returning `Future`s, plus
   `Stream`s for notifications) was picked for being the more direct mental
   model for someone new to Dart, not because `flutter_reactive_ble` is
   worse. Worth revisiting if `flutter_blue_plus` maintenance ever visibly
   stalls.
2. **Direct platform channel to `android.bluetooth.*`** — full control, no
   abstraction between you and the bug, but reinvents everything a
   maintained plugin already solved: threading, state machine, MTU
   negotiation. Worth it only if a plugin bug ever actively blocks a fix a
   later phase needs.

**Why not the alternatives?** `flutter_reactive_ble` is a defensible
alternate pick, not a rejected-on-merit one; this project standardizes on one
to avoid re-deriving GATT 133 mitigations against two different API shapes.
A hand-rolled platform channel is strictly more code for equivalent behavior
with nothing written yet.

**Long-term considerations.** Not currently isolated behind a clean seam any
more than `Plugin.BLE` was in the original track — the diagnostic screens
will deliberately reach through to raw characteristic objects, because a
diagnostic tool needs that. **The actual seam is `ITreadmillService`**
(`treadmill_service.dart`, Phase 01b): nothing above that interface touches
`flutter_blue_plus` types at all.

### `go_router`

**What problem does it solve?** The app needs to move between screens (scan →
dashboard → diagnostics → settings → history → backup) with back-stack
behavior and deep-linkable, URI-style routes.

**Why are we using it?** It's the Flutter team's own recommended routing
package (maintained under the `flutter` GitHub org), declarative and
URI-based — the closest Flutter equivalent to MAUI Shell's routing model that
the original decision record picked for the same reasons (flat set of
screens, no deep multi-level flows).

**Alternatives considered:**

1. **Plain `Navigator` push/pop** — simpler mental model, zero dependency,
   but manual route management and no URI-style navigation. Worth naming
   honestly: this app's screen count is small enough that this stays
   competitive for a while.
2. **`auto_route`** — code-generation-based, type-safe routes. Rejected for
   the same reason `riverpod_generator` was rejected in the root README: one
   more `build_runner` watcher in the edit loop, for a routing need this
   app's screen count doesn't actually require.

**Why not the alternatives?** Plain `Navigator` is the only genuinely
competitive option, and `go_router` is thin enough over it that the added
dependency cost is small. `auto_route`'s codegen tax isn't worth paying for
this app's flat navigation shape.

**Long-term considerations.** Actively maintained by the Flutter team,
tracks Flutter's own release cadence.

---

## Scope

Identical to the original track:

**In:** scaffolding, permissions, scan, connect, GATT tree, hex read dump,
notification log, control-point console, capture export, guided probe
checklist, SQLite factory + empty migration runner.

**Out:** parsers (Phase 01: this phase shows *hex*, it does not decode it),
auto-reconnect (Phase 02), dashboard, workouts, persistence of workout data.

> **The one decoding exception:** the control-point *response* is decoded — an
> operator staring at `80 00 01` needs to be told that means "Request Control
> → Success." That's a lookup table, not a parser. Everything else stays hex.

---

## Tasks

Work [`TASKS.md`](TASKS.md) in order. Summary:

| # | Task | Output |
|---|------|--------|
| 0.1 | Project scaffold | Buildable Flutter Android app, Riverpod, `go_router`, dark theme |
| 0.2 | Permissions + adapter state | `BLUETOOTH_SCAN` / `BLUETOOTH_CONNECT`, enable prompt |
| 0.3 | Scan screen | Device list, RSSI, filter toggle |
| 0.4 | Connect + discovery | GATT tree screen, MTU request |
| 0.5 | Read dump screen | Every readable characteristic as hex, copy button |
| 0.6 | Notification log screen | Live `timestamp \| uuid \| hex`, per-characteristic toggles |
| 0.7 | **Control point console** | Preset buttons + free hex, decoded result code |
| 0.8 | Capture recorder | JSONL session file, share/copy, confirm-and-annotate |
| 0.9 | Guided probe checklist | Parts A–G as a form; exports pasteable markdown |
| 0.10 | Rolling file log | Logging package + file, shareable |
| 0.11 | SQLite factory + migration runner | Empty migration set, DB file created on first run |

## Deliverables

1. **Installable APK** the operator can sideload.
2. **Six working screens**: Scan, GATT Tree, Read Dump, Notification Log,
   Control Console, Probe Checklist.
3. **`../../../captures/` output**: at least one real session file committed
   after the `[HUMAN]` run (shared with the original track — one capture
   folder, not two).
4. **`PHASE-00-FINDINGS.md` updated**: same file the original track uses.
   Measured hardware facts don't fork by framework.

## Traps that will otherwise be hit

Identical to the original track's, because they're properties of the device
and of Android BLE, not of the framework:

- **Subscribe to indications on `0x2AD9` before writing to it.**
- **`Request Control` (`00`) must be written and acknowledged before any
  other command**, and re-issued after every reconnect.
- **Serialise control point writes.** One outstanding command; wait for the
  indication (3 s timeout) before sending the next.
- **Use Write With Response**, not write-without-response.
- **`Stop or Pause` needs its parameter byte.** `08 01` stop, `08 02` pause.
- **GATT 133** will happen. Close/dispose the connection before reconnecting,
  connect non-auto, and put ~200 ms between connect and service discovery.
- **Scan throttling**: Android allows ~5 scan starts per 30 s window, then
  silently returns nothing. Debounce the scan button.
- **Do not decode `0x2ACD` in this phase.** Decoding is Phase 01.

## Safety

The control console can start the belt. Put a **confirmation dialog on any
command that can cause motion** (`07` Start, and `02` Set Target Speed when
the belt is stopped), and a permanent banner:

> The physical safety key is the emergency stop. This app is not.

## Definition of done

The operator can hand back a file that says what this treadmill actually
does, and Phase 01 can be written against bytes instead of against hope —
identical bar to the original track.
