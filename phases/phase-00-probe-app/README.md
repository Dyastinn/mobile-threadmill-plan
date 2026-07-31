# Phase 00 — Probe App

> **This phase exists to make the treadmill answer questions, not to build a product.**
> Every screen here is a lab instrument. None of it ships in the final UI, and none of
> it needs to be pretty. It needs to be *correct about bytes*.

**Hardware:** required, at the end · **Size:** L · **Blocks:** everything

---

## Goal

Produce an installable Android app that, on the operator's phone, can:

1. Find the treadmill and connect to it.
2. Show every service and characteristic, and dump every readable one as raw hex.
3. Subscribe to every notifying characteristic and log each packet as
   `timestamp | uuid | hex`.
4. **Send arbitrary bytes to the control point `0x2AD9` and show the raw response** —
   both via preset command buttons and via a free-text hex field.
5. **Record what happened**: export the session as a file, and let the operator mark
   an observation as confirmed-correct with a note.

After this phase, the question "what data should we send to the treadmill?" is
answered by experiment rather than by reading a spec and hoping the vendor followed it.

## Why it is shaped this way

The device is a **FitShow BLE module** presenting an FTMS shim over a transparent
UART bridge (`../DEVICE.md`). Its own feature declaration `0x2ACC` is **provably
false** — it claims incline target setting on a machine with no incline. A shim that
lies about itself is not a thing to build three phases of product on top of.

So the first deliverable is the instrument, and the instrument's output becomes the
specification.

---

## Reference docs

Read these. Do not read other phase folders.

- `../../05-FTMS-Protocol.md` — §1 characteristics, §7 control point commands and
  response format, §8 connection sequence. **§7 is the one this phase is built around.**
- `../../05a-FTMS-Probe-Procedure.md` — the manual procedure. Screen 6 automates it.
- `../../DEVICE.md` — what is already measured. Everything marked NEEDED is your target.
- `../../00-Project-Plan.md` — stack table, permissions.

---

## Scope

**In:** scaffolding, permissions, scan, connect, GATT tree, hex read dump,
notification log, control-point console, capture export, guided probe checklist,
SQLite factory + empty migration runner.

**Out:** parsers (Phase 01 — this phase shows *hex*, it does not decode it),
auto-reconnect (Phase 02), dashboard, workouts, persistence of workout data.

> **The one decoding exception:** the control-point *response* is decoded, because an
> operator staring at `80 00 01` needs to be told that means "Request Control →
> Success" to make a decision in the next thirty seconds. That is three lines of
> lookup, not a parser. Everything else stays hex.

---

## Tasks

Work `TASKS.md` in order. Summary:

| # | Task | Output |
|---|------|--------|
| 0.1 | Project scaffold | Buildable MAUI Android app, DI, logging, Shell, dark theme |
| 0.2 | Permissions + adapter state | `BLUETOOTH_SCAN` / `BLUETOOTH_CONNECT`, enable prompt |
| 0.3 | Scan screen | Device list, RSSI, filter toggle |
| 0.4 | Connect + discovery | GATT tree screen, MTU request |
| 0.5 | Read dump screen | Every readable characteristic as hex, copy button |
| 0.6 | Notification log screen | Live `timestamp \| uuid \| hex`, per-characteristic toggles |
| 0.7 | **Control point console** | Preset buttons + free hex, decoded result code |
| 0.8 | Capture recorder | JSONL session file, share/copy, confirm-and-annotate |
| 0.9 | Guided probe checklist | Parts A–G as a form; exports pasteable markdown |
| 0.10 | Rolling file log | logcat + file, shareable |
| 0.11 | SQLite factory + migration runner | Empty migration set, DB file created on first run |

---

## Deliverables

1. **Installable APK** the operator can sideload.
2. **Six working screens**: Scan, GATT Tree, Read Dump, Notification Log, Control
   Console, Probe Checklist.
3. **`captures/` output**: at least one real session file committed to the repo after
   the `[HUMAN]` run.
4. **`../../DEVICE.md` updated** with every field the run answered.
5. **`../../ASSUMPTIONS.md` updated**: entries resolved, new ones added.
6. **`PHASE-00-FINDINGS.md`** in this folder — the operator's filled-in results, and
   the verdicts that decide later phases.

---

## Automated tests

There is very little to unit-test here, and that is expected — this phase is I/O.
Test what is pure:

- Hex string ↔ byte array round-trip, including odd-length and whitespace input.
- Control-point command builder: `SetSpeed(6.5)` → `02 8A 02`. Table-drive it against
  the examples in `../../05-FTMS-Protocol.md` §7.
- Control-point response decoder: `80 00 01` → `(RequestControl, Success)`;
  `80 02 03` → `(SetTargetSpeed, InvalidParameter)`; short buffer → rejected, not
  crashed.
- Capture file writer produces valid JSONL and survives a mid-write kill.

## `[HUMAN]` tests

See `HUMAN-RUNBOOK.md`. The agent stops here and requests results.

---

## Acceptance

- [ ] Build is clean, zero warnings.
- [ ] App launches; DB file created on first run.
- [ ] Treadmill discovered on 5 of 5 scans.
- [ ] Connects and reaches a discovered-services state.
- [ ] Every readable characteristic dumps hex; the value is copyable.
- [ ] Notification log records `0x2ACD` packets with timestamps.
- [ ] Control console sends `00` (Request Control) and displays the raw response
      **and** its decoded meaning.
- [ ] A session capture file exists in `../../captures/` and is committed.
- [ ] `PHASE-00-FINDINGS.md` answers, unambiguously:
      - **Counter semantics** — per-session or cumulative? (blocks Phase 06 entirely)
      - **Control point verdict** — does it honour commands? (decides whether Phase 05 exists)
      - **Heart rate verdict** — usable / marginal / cut
      - Raw hex for `0x2ACC` and `0x2AD4`
      - Notification rate, negotiated MTU, MAC + address type, is `0x1826` advertised
- [ ] `../../DEVICE.md` has no remaining NEEDED field that this run could have answered.

---

## What "record what is correct" means concretely

Three levels, all delivered by task 0.8:

| Level | Mechanism |
|-------|-----------|
| Raw | Every byte in and out, timestamped, to a JSONL file. No interpretation. |
| Annotated | Operator taps an entry → "Confirm" + free-text note ("belt actually started here"). Stored alongside the raw entry. |
| Concluded | Probe Checklist screen (0.9) → verdicts → exported markdown → pasted into `PHASE-00-FINDINGS.md` and `../../DEVICE.md`. |

The raw layer is the one that matters most. Notes and verdicts can be re-derived from
raw bytes; raw bytes cannot be re-derived from a note that says "worked".

---

## Traps that will otherwise be hit

- **Subscribe to indications on `0x2AD9` before writing to it.** Write the CCCD
  `0x2902` value `0x0002`. Some machines silently drop writes otherwise. This is the
  single most common cause of "the control point does nothing".
- **`Request Control` (`00`) must be written and acknowledged before any other
  command**, and re-issued after every reconnect.
- **Serialise control point writes.** One outstanding command; wait for the
  indication (3 s timeout) before sending the next.
- **Use Write With Response**, not write-without-response.
- **`Stop or Pause` needs its parameter byte.** `08 01` stop, `08 02` pause. Bare
  `08` is malformed.
- **GATT 133** will happen. Always `close()` the `BluetoothGatt` before reconnecting,
  connect with `autoConnect: false`, and put ~200 ms between connect and
  `discoverServices()`.
- **Scan throttling**: Android allows ~5 scan starts per 30 s window, then silently
  returns nothing with no error. Debounce the scan button; never auto-restart in a loop.
- **Do not decode `0x2ACD` in this phase.** Showing a decoded speed the operator
  half-believes is worse than showing hex they must read. Decoding is Phase 01, after
  fixtures exist.

---

## Safety

The control console can start the belt. Put a **confirmation dialog on any command
that can cause motion** (`07` Start, and `02` Set Target Speed when the belt is
stopped), and a permanent banner on the console screen:

> The physical safety key is the emergency stop. This app is not.

---

## Definition of done

The operator can hand back a file that says what this treadmill actually does, and
Phase 01 can be written against bytes instead of against hope.
