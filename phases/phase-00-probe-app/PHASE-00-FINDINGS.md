# Phase 00 — Findings

> Filled in by the operator after the `[HUMAN]` run. **Measured values only.**
> Anything still uncertain belongs in `../../ASSUMPTIONS.md`, not here.
> Every `TBD` below is a real unknown — **do not invent a value for one.**

Run date: ____________  ·  App version: ____________  ·  Capture file(s): ____________

---

## Verdicts — the four that decide later phases

### V1 · Counter semantics — **blocks Phase 06 entirely**

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

### V2 · Control point — **decides whether Phase 05 exists**

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
into the running state — `Set Target Speed` must be **re-issued after Start**, not
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
3. Note what "all lights" means precisely — every speed indicator LED lit simultaneously?
   A specific fault/ramp icon? This matters for whether it's a ramp-up animation
   (harmless, just re-set the speed) or a genuine fault state.

A read-only app is still worth shipping. "Void" here is a finding, not a failure.
This finding currently points to Phase 05 existing but needing a specific command
*order*, not the naive one — see `../../ASSUMPTIONS.md` A2.

### V3 · Heart rate

```
[ ] Usable   → 0x2A37, show in dashboard and charts
[ ] Marginal → record to WorkoutSample, hide from UI
[ ] Unusable → cut from the app entirely

0x2A37 vs manual count: ______ vs ______ bpm    Stability: ______
FTMS 0x2ACD HR field agrees? yes / no / field absent
```

### V4 · Scan filter — production choice

```
Is 0x1826 in the advertisement?  [ ] yes → filter on service UUID
                                 [ ] no  → filter on the FS- name prefix
```

Never fall back to unfiltered scanning. Ship one filter, not both.

---

## Raw hex — Phase 01 parser fixtures

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

### Matched console-vs-hex pairs — minimum four

| Console speed | Console distance | Console time | Raw `0x2ACD` hex |
|---------------|------------------|--------------|------------------|
| | | | |
| | | | |
| | | | |
| | | | |

These are the parser unit-test fixtures. Without matched pairs there is no way to
prove the decoder is correct — only that it does not crash.

---

## Machine status (`0x2ADA`)

| Console action | Op code emitted |
|----------------|-----------------|
| Press Start | |
| Press Stop | |
| Change speed | |
| Pull safety key | |

If nothing is emitted, machine state must be inferred from `0x2ACD` speed values —
workable but less precise. Record that in `../../ASSUMPTIONS.md`.

---

## BLE identity

```
MAC address:   ____________________
Address type:  [ ] public  [ ] random resolvable
Advertised name: ____________________
```

Random resolvable → the MAC is not stable and `UX_Device_MacAddress` in
`../../14-Database.md` is wrong; match on name instead.

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

RSSI at the walking position is ≈ −49 dBm — strong. **Any disconnect here is a
software problem, not a radio one.** Useful framing for Phase 02.

---

## Still unresolved after this run

| # | Question | Blocks | Why it wasn't answered |
|---|----------|--------|------------------------|
| | | | |

Copy each row into `../../ASSUMPTIONS.md` with its phase number.
