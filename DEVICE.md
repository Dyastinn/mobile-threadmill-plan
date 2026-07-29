# Device Facts

> Measured facts only. Anything not measured belongs in `ASSUMPTIONS.md`, not here.
> Fields marked **NEEDED** are still outstanding — see `05a-FTMS-Probe-Procedure.md`.

Last updated: **2026-07-28** (first nRF Connect capture)

---

## Phone

| Field | Value |
|-------|-------|
| Model | Poco X6 Pro 5G |
| OS | Android 16 (HyperOS) |
| API level | 36 |
| Manufacturer | Xiaomi (POCO) |
| Dedicated device | Yes — no other users, no competing apps |

**Decisions this drives:**

- **`minSdkVersion` 31, `targetSdkVersion` 36.** The entire legacy Bluetooth permission
  path is out of scope. Do not implement it.
- **Xiaomi requires more than standard battery optimisation.** See the checklist below.

### HyperOS background-execution checklist — `[HUMAN]`

Standard Android battery optimisation is **not sufficient** on Xiaomi. These are
separate controls and they are the ones that actually kill long-running services.

| Control | Where | Status |
|---------|-------|--------|
| Android battery optimisation disabled | Android app info | ✅ Done |
| **Autostart** enabled | HyperOS per-app menu | ⬜ NEEDED |
| **Battery saver → "No restrictions"** | HyperOS app info (distinct from the above) | ⬜ NEEDED |
| **Locked in Recents** (padlock) | Task switcher | ⬜ NEEDED |

*Confidence: high that these controls exist and matter; moderate on exact menu names,
which Xiaomi renames between HyperOS versions.*

**Verify all four before Phase 15.** Skipping them means endurance tests fail for
reasons unrelated to the code.

---

## Treadmill

| Field | Value |
|-------|-------|
| Model | MY-HI Q8Y |
| Type | Folding treadmill |
| Maximum speed | 16.0 km/h (confirmed against `0x2AD4`) |
| Incline | **No** |
| Handgrip heart rate | Yes — usability unverified |
| Magnetic safety key | Yes |
| Firmware version | NEEDED — try `180A` Device Information |

The absence of incline is load-bearing: it is what proves the `0x2ACC` feature bitmask
is lying (see below).

---

## BLE identity

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
transparent-UART modules for treadmills, bikes and rowers. Architecture:

```
treadmill motor board ←UART→ FitShow module ←BLE→ phone
```

FTMS is a vendor shim over that bridge, not a native implementation. Expect spec
deviations; verify everything against hex.

**Why the outstanding items matter:**

- **Address type:** if random resolvable, the MAC is not stable between sessions and
  `UX_Device_MacAddress` in the schema is wrong — match on name instead.
- **`0x1826` advertised:** decides the Phase 1 scan filter. Fallback is the `FS-` name
  prefix, not unfiltered scanning.

---

## Services discovered

| UUID | Name | Used |
|------|------|------|
| `1800` | Generic Access | No |
| `180A` | Device Information | Firmware version only |
| `180D` | **Heart Rate** | **Yes — preferred HR source** |
| `FFE0` | Vendor (FitShow transparent serial) | No |
| `FFF0` | Vendor (FitShow transparent serial) | No |
| `1826` | **Fitness Machine** | **Yes — primary** |

`FFE0`/`FFF0` are recorded for completeness. They are **not a fallback plan** — the
FitShow UART protocol is undocumented and prior public decoding attempts have not
succeeded. If FTMS proves unusable, that is a project-level decision, not a workaround.

---

## FTMS characteristics

All six present.

| UUID | Characteristic | Properties |
|------|---------------|------------|
| `2ACC` | Fitness Machine Feature | Read |
| `2ACD` | Treadmill Data | Notify |
| `2AD3` | Training Status | Read, Notify |
| `2AD4` | Supported Speed Range | Read |
| `2AD9` | Fitness Machine Control Point | Write, Indicate |
| `2ADA` | Machine Status | Notify |

**Presence is not function.** `2AD9` exposing Write and Indicate says nothing about
whether it honours commands. Unverified — see Probe Part D.

---

## Capabilities (`0x2ACC`) — ⚠️ UNRELIABLE

```
Raw hex: NEEDED
```

**Claimed machine features:** Total Distance, Step Count, Resistance Level, Expended
Energy, Heart Rate Measurement, Elapsed Time, Power Measurement

**Claimed target features:** Speed Target Setting, Inclination Target Setting,
Resistance Target Setting, Power Target Setting

### Why this is not trustworthy

| Claim | Reality |
|-------|---------|
| Resistance Level | Treadmills have no resistance mechanism |
| Power Measurement | This treadmill has no power meter |
| **Inclination Target Setting** | **The machine has no incline** |

The last is decisive: inclination is absent from machine features but present in target
settings. A device cannot support setting a target for a capability it does not report
having. The bitmask is a stock value in FitShow module firmware, not a description of
this treadmill. The four claimed target bits are also 0–3 = `0x0000000F`, a suspiciously
round "all of them".

**Consequence:** `0x2ACC` is advisory. Dashboard fields derive from observed `0x2ACD`
flags; speed control derives from the live control point handshake. See plan Phases 3,
4 and 6.

---

## Speed range (`0x2AD4`) — VERIFIED

```
Min: 1.0 km/h    Max: 16.0 km/h    Increment: 0.1 km/h
Raw hex: NEEDED (predicted 64 00 40 06 0A 00 — confirm)
```

Presets should be generated from this range, not hardcoded. Suggested: 2, 4, 6, 8, 10,
12 km/h.

---

## Training status (`0x2AD3`)

Reports `Idle` (`0x01`) at rest. Populated rather than stubbed, which is mildly
encouraging for shim quality. Whether it transitions during a session is unverified.

---

## Data stream (`0x2ACD`) — NOT YET CAPTURED

```
Flags: NEEDED
Packet length: NEEDED
Fields present: NEEDED
Notification rate: NEEDED (expect ~1 Hz)
Flags constant throughout a session? NEEDED
Device ever splits records (More Data bit)? NEEDED
```

### Matched console-vs-hex captures — NEEDED (minimum four)

| Console speed | Console distance | Console time | Raw hex |
|---------------|------------------|--------------|---------|
| | | | |
| | | | |
| | | | |
| | | | |

These become the parser unit test fixtures. Without them there is no way to prove the
decoder is correct.

---

## Counter semantics — NEEDED — HIGHEST PRIORITY

```
[ ] Per-session (counters reset between sessions)
[ ] Cumulative since power-on
[ ] Mixed: ______________________________________________

Evidence:
  Distance end of session 1:   ______ m
  Distance start of session 2: ______ m
  Distance after power cycle:  ______ m
```

**This determines the entire Phase 7 recording implementation.** Fifteen minutes on the
belt. Do not guess.

---

## Machine status events (`0x2ADA`) — NOT YET CAPTURED

| Console action | Op code emitted |
|----------------|-----------------|
| Press Start | NEEDED |
| Press Stop | NEEDED |
| Change speed | NEEDED |
| Pull safety key | NEEDED |

If nothing is emitted, machine state must be inferred from `0x2ACD` speed values.

---

## Control point (`0x2AD9`) — NOT YET TESTED — DECIDES WHETHER PHASE 6 EXISTS

| Test | Result |
|------|--------|
| Request Control (`00`) | NEEDED |
| Set speed while stopped | NEEDED |
| Start (`07`) | NEEDED |
| Set speed while running | NEEDED |
| Out-of-range speed (expect `0x03`) | NEEDED |
| Pause (`08 02`) | NEEDED |
| Stop (`08 01`) | NEEDED |
| Control after 5 min idle | NEEDED |
| Control after reconnect, no re-request | NEEDED |

The `Speed Target Setting` feature bit is set — but given the bitmask over-claims
elsewhere, that is weak evidence. Only the live handshake counts.

---

## Heart rate — NOT YET TESTED

Two sources available: `0x2A37` in `180D` (preferred) and the FTMS field in `0x2ACD`.

| Item | Result |
|------|--------|
| Does `0x2A37` notify? | NEEDED |
| Sensor contact detection supported? | NEEDED |
| Accuracy vs. manually counted pulse | NEEDED |
| Time from grip to first reading | NEEDED |
| Behaviour on release | NEEDED |
| Agreement with FTMS field | NEEDED |
| **Verdict: usable / marginal / cut** | NEEDED |

"Cut it" is a valid and acceptable outcome. A metric that is wrong half the time will
permanently pollute the average and maximum HR columns of every stored workout.

---

## Resilience — NOT YET TESTED

| Test | Result |
|------|--------|
| Time to detect disconnect (out of range) | NEEDED |
| Auto-reconnect time | NEEDED |
| Counters after reconnect | NEEDED |
| Bluetooth off/on recovery | NEEDED |
| Treadmill power cycle recovery | NEEDED |
| Screen off 5 min (pre-Phase 8) | NEEDED |
| GATT error codes seen | NEEDED |

RSSI of −49 dBm is strong, so any disconnect observed here is a software problem rather
than a radio one. Useful framing when debugging Phase 2.
