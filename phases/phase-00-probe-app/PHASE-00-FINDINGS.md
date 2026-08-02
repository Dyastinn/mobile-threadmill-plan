# Phase 00 — Findings

> Filled in by the operator after the `[HUMAN]` run. **Measured values only.**
> Anything still uncertain stays blank here, tagged with the phase it blocks,
> rather than guessed. Every blank below is a real unknown. **Do not invent a
> value for one.**

Run date: ____________  ·  App version: ____________  ·  Capture file(s): ____________

---

## Known facts (measured 2026-07-28, first nRF Connect capture)

These don't need re-measuring; they're the starting point for everything below.

### Phone

| Field | Value |
|-------|-------|
| Model | Poco X6 Pro 5G |
| OS | Android 16 (HyperOS) |
| API level | 36 |
| Manufacturer | Xiaomi (POCO) |
| Dedicated device | Yes — no other users, no competing apps |

**Decisions this drives:** `minSdkVersion` 31, `targetSdkVersion` 36 — the legacy
Bluetooth permission path is out of scope, do not implement it. Xiaomi requires
more than standard battery optimisation (see the HyperOS checklist below).

**HyperOS background-execution checklist (`[HUMAN]`, needed before Phase 14):**
standard Android battery optimisation is **not sufficient** on Xiaomi.

| Control | Where | Status |
|---------|-------|--------|
| Android battery optimisation disabled | Android app info | ✅ Done |
| **Autostart** enabled | HyperOS per-app menu | ⬜ NEEDED |
| **Battery saver → "No restrictions"** | HyperOS app info (distinct from the above) | ⬜ NEEDED |
| **Locked in Recents** (padlock) | Task switcher | ⬜ NEEDED |

*Confidence: high that these controls exist and matter; moderate on exact menu
names, which Xiaomi renames between HyperOS versions.* **Verify all four before
Phase 14.** Skipping them means endurance tests fail for reasons unrelated to the
code.

### Treadmill

| Field | Value |
|-------|-------|
| Model | MY-HI Q8Y |
| Type | Folding treadmill |
| Maximum speed | 16.0 km/h (confirmed against `0x2AD4`) |
| Incline | **No** |
| Handgrip heart rate | Yes — usability unverified, see V3 below |
| Magnetic safety key | Yes |
| Firmware version | NEEDED — try `180A` Device Information |

The absence of incline is load-bearing: it is what proves the `0x2ACC` feature
bitmask is lying (see "Capabilities" below).

### BLE identity

| Field | Value |
|-------|-------|
| Advertised name | `FS-9F4235` |
| **Module vendor** | **FitShow (Xiamen) Information Technology** |
| Requires bonding | No |
| RSSI at walking position | ≈ −49 dBm (strong) |
| MAC address | **NEEDED** |
| Address type | **NEEDED** — public vs. random resolvable |
| `0x1826` in advertisement? | **NEEDED** |
| Negotiated MTU | **NEEDED** |

**The `FS-` prefix identifies the BLE module, not the treadmill.** FitShow makes
transparent-UART modules for treadmills, bikes and rowers. FTMS is a vendor shim
over that bridge, not a native implementation — expect spec deviations, verify
everything against hex (see `../phase-01-protocol-decode/README.md`'s FTMS
protocol reference).

**Why the outstanding items matter:** if address type is random resolvable, the
MAC is not stable between sessions and `UX_Device_MacAddress` in
`../phase-06-recording-schema/README.md`'s schema is wrong — match on name
instead. Whether `0x1826` is advertised decides the Phase 02 production scan
filter.

### Services discovered

All six FTMS characteristics present, plus `1800`, `180A`, `180D` (Heart Rate —
preferred HR source), and vendor `FFE0`/`FFF0` (FitShow transparent serial,
recorded only — **not a fallback plan**, the protocol is undocumented and prior
public decoding attempts have not succeeded).

### Capabilities (`0x2ACC`): ⚠️ UNRELIABLE

```
Raw hex: NEEDED
```

**Claimed machine features:** Total Distance, Step Count, Resistance Level,
Expended Energy, Heart Rate Measurement, Elapsed Time, Power Measurement

**Claimed target features:** Speed Target Setting, Inclination Target Setting,
Resistance Target Setting, Power Target Setting

**Why this is not trustworthy:**

| Claim | Reality |
|-------|---------|
| Resistance Level | Treadmills have no resistance mechanism |
| Power Measurement | This treadmill has no power meter |
| **Inclination Target Setting** | **The machine has no incline** |

The last is decisive: inclination is absent from machine features but present in
target settings. A device cannot support setting a target for a capability it
does not report having. The bitmask is a stock value in FitShow module firmware,
not a description of this treadmill. The four claimed target bits are also 0–3 =
`0x0000000F`, a suspiciously round "all of them".

**Consequence:** `0x2ACC` is advisory. Dashboard fields derive from observed
`0x2ACD` flags; speed control derives from the live control point handshake. See
`../phase-01-protocol-decode/README.md` and `../phase-05-treadmill-control/README.md`.

### Speed range (`0x2AD4`): VERIFIED

```
Min: 1.0 km/h    Max: 16.0 km/h    Increment: 0.1 km/h
Raw hex: NEEDED (predicted 64 00 40 06 0A 00 — confirm)
```

Presets should be generated from this range, not hardcoded. Suggested: 2, 4, 6, 8,
10, 12 km/h.

### Training status (`0x2AD3`)

Reports `Idle` (`0x01`) at rest. Populated rather than stubbed, which is mildly
encouraging for shim quality. Whether it transitions during a session is
unverified.

---

## Verdicts: the four that decide later phases

### V1 · Counter semantics (**blocks Phase 06 entirely**)

```
[ ] Per-session — counters reset between sessions
[ ] Cumulative since power-on
[ ] Mixed: ______________________________________________

Evidence
  Distance   end s1: ______ m     start s2: ______ m     after power cycle: ______ m
  Elapsed    end s1: ______ s     start s2: ______ s
  Calories   end s1: ______ kcal  start s2: ______ kcal
```

**Per-session** → record reported values directly.
**Cumulative** → every workout value is a delta against a workout-start baseline, and
the engine must detect a mid-workout decrease and re-baseline.

### V2 · Control point (**decides whether Phase 05 exists**)

| Command | Bytes sent | Response | Result | Physical effect |
|---------|-----------|----------|--------|-----------------|
| Request Control | `00` | | 0x__ | — |
| Set speed, stopped | | | 0x__ | belt moved? |
| Start | `07` | | 0x__ | belt started? |
| Set speed, running | | | 0x__ | speed changed? latency ___ s |
| Out of range | | | 0x__ | expect 0x03 |
| Pause | `08 02` | | 0x__ | |
| Stop | `08 01` | | 0x__ | |
| After 5 min idle | | | 0x__ | permission expired? |
| After reconnect, no re-request | | | 0x__ | expect 0x05 |

```
Verdict:
[ ] Control works — Phase 05 proceeds as planned
[x] Control partially works — specify: Request Control, Set Target Speed, and Start
    all produced a physical effect (belt moved) via the Control Console. BUT: after
    Start (07), the belt does not resume at the previously-set target speed — it
    ramps at a slow/default speed and the console shows an unusual "all lights"
    indicator state, not the numeric target (e.g. target 7 km/h did not result in
    7 km/h after Start).
[ ] Control does not work — Phase 05 is void, app is read-only
```

**Preliminary, not yet confirmed via the full structured D1–D8 matrix above.** Working
theory (operator's, 2026-07-31): the device does not carry a pre-Start target speed
into the running state. `Set Target Speed` must be **re-issued after Start**, not
only before it, to actually reach the desired pace. This reframes the expected
command sequence as `Request Control → Start → Set Target Speed` rather than
`Request Control → Set Target Speed → Start`.

**To fully confirm** (do this in the Control Console when possible, so bytes and
timestamps land in the capture file):
1. Request Control, then Start, then immediately Set Target Speed to a specific
   value (e.g. 7.0 km/h) *while already running*. Does it now hold at 7.0?
2. Compare against Set Target Speed *before* Start (the D2/D3 order in the table
   above) to confirm that path really is the one that produces the "all lights /
   wrong speed" behaviour.
3. Note what "all lights" means precisely. Every speed indicator LED lit simultaneously?
   A specific fault/ramp icon? This matters for whether it's a ramp-up animation
   (harmless, just re-set the speed) or a genuine fault state.

A read-only app is still worth shipping. "Void" here is a finding, not a failure.
This finding currently points to Phase 05 existing but needing a specific command
*order*, not the naive one.

### V3 · Heart rate

```
[ ] Usable   → 0x2A37, show in dashboard and charts
[ ] Marginal → record to WorkoutSample, hide from UI
[ ] Unusable → cut from the app entirely

0x2A37 vs manual count: ______ vs ______ bpm    Stability: ______
FTMS 0x2ACD HR field agrees? yes / no / field absent
```

### V4 · Scan filter: production choice

```
Is 0x1826 in the advertisement?  [ ] yes → filter on service UUID
                                 [ ] no  → filter on the FS- name prefix
```

Never fall back to unfiltered scanning. Ship one filter, not both.

---

## Raw hex: Phase 01 parser fixtures

```
0x2ACC (Feature):            ________________________________
0x2AD4 (Speed Range):        ____________________
   predicted 64 00 40 06 0A 00 — matches? [ ] yes [ ] no
0x2AD3 (Training Status):    ____________
180A firmware / model:       ____________________
```

If `0x2AD4` differs from the prediction, the documented decode is wrong and everything
downstream inherits the error. Say so loudly.

---

## Treadmill Data (`0x2ACD`) stream

```
Notifies while belt stopped?   [ ] yes  [ ] no
Notification rate:             ______ Hz
Packet length:                 ______ bytes
Distinct flags values seen:    ____________________________
Flags constant through session? [ ] yes  [ ] no
Device ever splits records?    [ ] yes  [ ] no
Negotiated MTU:                ______
```

### Matched console-vs-hex pairs (minimum four)

| Console speed | Console distance | Console time | Raw `0x2ACD` hex |
|---------------|------------------|--------------|------------------|
| | | | |
| | | | |
| | | | |
| | | | |

These are the parser unit-test fixtures. Without matched pairs there is no way to
prove the decoder is correct, only that it does not crash.

---

## Machine status (`0x2ADA`)

| Console action | Op code emitted |
|----------------|-----------------|
| Press Start | |
| Press Stop | |
| Change speed | |
| Pull safety key | |

If nothing is emitted, machine state must be inferred from `0x2ACD` speed values.
That's workable but less precise. Note it in the "Still unresolved" table below.

---

## Resilience

| Test | Result |
|------|--------|
| Time to detect disconnect | ______ s |
| Manual reconnect time | ______ s |
| Counters after reconnect | continue / reset |
| Bluetooth off/on recovery | |
| Treadmill power-cycle recovery | |
| Screen off 5 min | (failure expected pre-Phase 07) |
| GATT error codes seen | |

RSSI at the walking position is ≈ −49 dBm, strong. **Any disconnect here is a
software problem, not a radio one.** Useful framing for Phase 02.

---

## Still unresolved after this run

| # | Question | Blocks | Why it wasn't answered |
|---|----------|--------|------------------------|
| | | | |

Each row here is a live open question, tagged with the phase it blocks. Update this
table every time a new one surfaces or an old one resolves — this is the project's
single register of open assumptions; there is no separate assumptions file.
