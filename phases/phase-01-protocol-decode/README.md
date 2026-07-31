# Phase 01 — Protocol Decode & Fixtures

> Pure desk work. No hardware in the loop — the hardware already spoke, in Phase 00,
> and its words are in `../../captures/`.

**Hardware:** none · **Size:** M · **Blocked by:** Phase 00 findings
**Unblocks:** Phases 03, 04, 06 (via `FakeTreadmillService`)

---

## Goal

Turn captured hex into parsers that are provably correct, and build the seam that lets
every later phase be developed without the treadmill in the room.

---

## Reference docs

- `../../05-FTMS-Protocol.md` — §2 `0x2ACC`, §3 `0x2AD4`, §4 `0x2ACD` **and its three
  traps**, §4a `0x2A37`, §5 `0x2ADA`, §6 `0x2AD3`, §7 control point response
- `../../ITreadmillService.cs` — the interface to implement, and the types
- `../phase-00-probe-app/PHASE-00-FINDINGS.md` — the measured truth
- `../../captures/` — raw sessions

---

## Do not start until

- [ ] Raw hex for `0x2ACC` and `0x2AD4` exists
- [ ] At least four matched console-vs-hex pairs exist
- [ ] V1 (counter semantics) has an answer

Without these you are writing a parser you cannot test, which is the same as not
having one.

---

## Tasks

### 1.1 — Extract fixtures from captures

**Creates:** `tests/Fixtures/*.json`

Pull every distinct packet out of `../../captures/` into named fixtures, each carrying
its provenance: source file, timestamp, and — for `0x2ACD` — the paired console values.

Include the ugly ones: short packets, unexpected lengths, anything the log flagged.
A parser that only sees clean input is untested.

### 1.2 — `0x2ACD` Treadmill Data parser ← the one that matters

**Creates:** `Features/Ftms/TreadmillDataParser.cs`

**Cursor-based and flag-driven. Never fixed-offset.** Field order is fixed; presence is
per-packet.

**The three traps, all of which will otherwise be hit:**

1. **Bit 0 is inverted.** Bit 0 is *More Data*. Instantaneous Speed is present when
   bit 0 is **`0`**. This is the opposite of every other bit in the field. Decoding it
   as a normal presence bit shifts every subsequent field and corrupts the whole packet.
2. **Total Distance is uint24** — 3 bytes, little-endian. There is no `BitConverter`
   overload; assemble it manually:
   ```csharp
   uint distance = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16));
   i += 3;
   ```
3. **Expended Energy is one flag bit but three fields**, 5 bytes total: Total Energy
   (uint16 kcal), Energy Per Hour (uint16 kcal), Energy Per Minute (uint8 kcal).
   Advancing 2 instead of 5 misaligns everything after it.

Also: bits 3 and 4 each gate **two** fields (4 bytes each).

**Length validation is mandatory.** Compute the expected byte count from the flags;
if it doesn't match, **reject the packet, log it with its hex, and never read past the
buffer.** Field table: `../../05-FTMS-Protocol.md` §4.

Return a `TreadmillSample` struct — no heap allocation on the notification hot path.
Every field nullable: presence is per-packet, not per-device.

### 1.3 — Remaining parsers

**Creates:** `Features/Ftms/` — one file each

| Parser | Notes |
|--------|-------|
| `0x2ACC` Feature | Decode and expose the raw uint32s. **Never branch on it** — see below |
| `0x2AD4` Speed Range | 3× uint16 LE in 0.01 km/h units |
| `0x2ADA` Machine Status | Op code + parameters. `0xFF` and `0x03` matter most |
| `0x2AD3` Training Status | Display only |
| `0x2A37` Heart Rate | Flags bit 0 selects uint8 vs uint16; bits 1–2 are sensor contact |
| Control point response | `80 | reqOp | result` |

### 1.4 — Capability derivation

**Creates:** `Features/Ftms/CapabilityTracker.cs`

**`0x2ACC` is advisory. Log it, never gate on it.** This device claims Resistance
Level, Power Measurement, and **Inclination Target Setting on a machine with no
incline** — while omitting inclination from the machine-features word. That internal
contradiction proves the bitmask is stock module firmware, not a description of this
treadmill.

Instead: accumulate the **union of observed `0x2ACD` flag bits over the first ~30 s**
after connecting. A field is real if it arrives in packets. Persist the derived set per
device so the UI is not rebuilding itself on every connect.

### 1.5 — `ITreadmillService` real implementation

**Creates:** `Features/Treadmill/TreadmillService.cs`

Implements `../../ITreadmillService.cs` over the Phase 00 BLE layer. Non-negotiables,
all carried over from Phase 00's console:

- Connection sequence per `../../05-FTMS-Protocol.md` §8. Read `0x2ACC` and `0x2AD4`
  **before** subscribing, so the UI is configured by the time data arrives.
- Subscribe to `0x2AD9` indications **before** writing to it.
- `Request Control` before any other command; re-issue after reconnect and after any
  `ControlPermissionLost`.
- **Serialise control point writes**: one outstanding, 3 s timeout.
- Clamp target speed to the range read from `0x2AD4` and round to the device increment.
  **Never hardcode a range.**
- Events raised on the UI thread — marshal once, here at the boundary, so no consumer
  has to think about it.
- Control methods return `ControlResult`, never throw. Control point failures are
  expected operating conditions.

### 1.6 — `FakeTreadmillService`

**Creates:** `Features/Treadmill/FakeTreadmillService.cs`

~40 lines of real logic that saves the whole project. Must simulate on demand:

- Normal session: warm-up, steady, cool-down
- Interval session
- **Mid-session dropout and recovery** — exercises Phase 04's grace window
- **Sparse fields** — packets with fields absent, exercising nullable handling
- **Control rejected** — `ControlNotPermitted` responses
- **Counter reset mid-session** — only if V1 came back cumulative

Register behind a debug flag or build configuration so it swaps in without editing
consumer code.

### 1.7 — Update the protocol doc

**Touches:** `../../05-FTMS-Protocol.md`, `../../DEVICE.md`, `../../ASSUMPTIONS.md`

Replace every `TBD` with a measured value or an explicit *"not supported by this
device"*. Anything still unknown moves to `ASSUMPTIONS.md` with the phase it blocks —
it does not stay as a `TBD` in a doc claiming to be a spec.

---

## Tests — this is where the project's real test suite lives

Everything else in this app is I/O and UI. These are the only meaningful automated
tests, so write them properly.

- Every fixture decodes to its expected values.
- **Console-matched fixtures decode to the console's numbers**, within rounding. This
  is the test that proves correctness rather than self-consistency.
- Malformed input: truncated packet, length/flags mismatch, empty buffer, length
  exceeding the buffer. All must be **rejected and logged**, never crash, never read
  out of bounds.
- Bit 0 specifically: a packet with More Data set and one without, asserting the speed
  field's presence flips the way the spec says and not the intuitive way.
- uint24 distance at boundary values (`0x00FFFF`, `0x010000`).
- Expended Energy advancing exactly 5 bytes — assert a following field still lands
  correctly.
- `SpeedRange.Clamp` at min, max, below min, above max, and mid-increment.
- Control point response decode for all five result codes, plus a short buffer.

## Acceptance

- [ ] Every `TBD` in `../../05-FTMS-Protocol.md` resolved or marked unsupported
- [ ] Console-matched fixtures decode to the console's values at all captured speeds
- [ ] Malformed-input tests pass — nothing crashes, everything logs
- [ ] `FakeTreadmillService` produces a realistic 10-minute stream and can trigger a
      dropout, sparse fields, and a control rejection
- [ ] Zero warnings; all Phase 00 tests still pass
