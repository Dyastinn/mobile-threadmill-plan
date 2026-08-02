# Assumptions Register

> Every unresolved guess, with the phase it blocks and how it gets resolved.
> An entry leaves this file only when it is **measured**, not when it feels settled.

Last updated: 2026-07-28

---

## Rules

- Any assumption made to get unblocked goes here immediately, with a phase number.
- Nothing in `DEVICE.md` may be an assumption. Measured facts there, guesses here.
- An implementing agent that needs a value not yet measured must add an entry rather
  than picking a plausible number.

---

## Open: blocking

### A1. Counter reset semantics
**Blocks:** Phase 7 (entirely) · **Resolve:** Probe Part C7 · **Effort:** 15 min

Unknown whether the treadmill's distance / calories / elapsed-time counters reset
between sessions or accumulate since power-on.

- Per-session → record reported values directly.
- Cumulative → every value is a delta against the workout-start baseline, and the
  engine must detect mid-workout resets (value decreases) and re-baseline.

Guessing wrong makes every stored workout wrong. **Do not start Phase 7 without this.**

### A2. Does the control point honour commands?
**Blocks:** whether Phase 6 exists · **Resolve:** Probe Part D · **Effort:** 15 min

`0x2AD9` is present with Write + Indicate, and the Speed Target Setting feature bit is
set. Neither is strong evidence given that the same bitmask claims incline support on a
machine with no incline (see A3).

**Update 2026-07-31 (operator, via the Control Console, ad hoc, not yet the full D1–D8
matrix):** Request Control, Set Target Speed, and Start all produced a physical effect.
So the control point does honour commands. The open question has narrowed from
*"does it work"* to *"in what order."* Observed: setting a target speed and then
starting does **not** bring the belt to that speed. It ramps at a slow/default pace
with an unusual "all lights" console indicator. Working theory: the target speed must
be (re-)set **after** `Start`, not only before. If confirmed, Phase 05's `StartAsync`
must internally re-issue the last requested `SetSpeedAsync` once the belt is
confirmed running, rather than assuming Start preserves a pre-set target.

**Still needed before this is a resolved fact, not a working theory:** the structured
D1–D8 sequence from the Control Console, with bytes/timestamps landing in a capture
file, and a precise description of what "all lights" actually means on the console
(ramp animation vs. a fault indicator).

Outcome is binary: the app either controls speed or is read-only. Both are shippable.

### A3. `0x2ACC` decode is unconfirmed
**Blocks:** parser tests, capability logic · **Resolve:** capture raw hex · **Effort:** 2 min

The feature list was decoded but the raw bytes were not recorded. The decode is already
known to describe an impossible device, so it cannot be checked for self-consistency,
only against the actual bytes.

*Working assumption:* the bitmask over-claims and must be treated as advisory. Confidence
high, but the specific bit positions are unverified.

### A4. Treadmill Data packet layout
**Blocks:** Phase 4 correctness · **Resolve:** Probe Part C · **Effort:** 15 min

Which flag bits this device sets, in what packet length, at what rate, and whether the
flags change mid-session. The parser must be flag-driven regardless, but without
matched console-vs-hex pairs there is no way to prove it decodes correctly.

---

## Open: non-blocking but cheap

### A5. Is `0x1826` in the advertisement?
**Blocks:** Phase 1 scan filter · **Effort:** 1 min

*Working assumption:* yes. If wrong, fall back to the `FS-` name prefix, not to
unfiltered scanning, which is slower and drains more battery.

### A6. BLE address type
**Blocks:** device persistence, schema · **Effort:** 1 min

*Working assumption:* public static. If it is random resolvable, the MAC is not stable
between sessions and `UX_Device_MacAddress` in `14-Database.md` is wrong. Match on
device name instead.

### A7. Negotiated MTU and record splitting
**Blocks:** parser edge case · **Effort:** 2 min

*Working assumption:* requesting MTU 517 succeeds and records arrive whole. The parser
must handle the More Data split correctly regardless, so this is informational rather
than load-bearing.

### A8. Notification rate
**Blocks:** Phase 4 UI throttling · **Effort:** 2 min

*Working assumption:* ~1 Hz, per the FTMS specification's recommendation. The original
project spec claimed 5–10 Hz, which is almost certainly wrong. If the measured rate is
above 1 Hz, UI updates must be throttled to 4 Hz.

### A9. Heart rate usability
**Blocks:** Phase 4 scope · **Resolve:** Probe Part G · **Effort:** 10 min

`180D` is present and preferred over the FTMS field. Whether handgrip readings are
accurate and stable enough to be worth showing is unknown.

*Working assumption:* marginal. Plan for the possibility of cutting HR entirely; that is
a valid outcome, not a failure.

---

## Open: design decisions not yet validated

### A10. `Plugin.BLE` vs. direct Android bindings
**Affects:** Phases 2, 8 · **Resolve:** experience during Phase 2

Chosen `Plugin.BLE` for boilerplate reduction. The risk is that Android BLE bugs live in
exactly the layer being abstracted. Switching costs about a day and `ITreadmillService`
insulates everything above it.

*Confidence: moderate. This is a judgement call, not a fact.*

### A11. 5-second telemetry cadence
**Affects:** Phase 7, Phase 11 · **Resolve:** after real workouts

Chosen over 1 Hz (storage) and on-change (HR varies continuously, so on-change
degenerates to per-sample anyway while adding interpolation complexity).

Revisit if per-workout speed curves look too coarse in Phase 11.

### A12. HyperOS control names
**Affects:** Phase 8 checklist · **Resolve:** on the device

Autostart, per-app Battery saver, and Recents lock are believed to exist and matter on
Xiaomi. Exact menu names vary between HyperOS versions.

*Confidence: high on existence and importance, moderate on naming.*

### A13. `connectedDevice` foreground service has no timeout
**Affects:** Phase 8, Phase 15 · **Resolve:** validated by Phase 15 Test 2

Android 15's 6-hour foreground service limit applies only to `dataSync` and
`mediaProcessing`. A two-hour workout should be unaffected and no `onTimeout()` handling
is needed.

*Confidence: high (current Android documentation).* The two-hour endurance test is the
empirical check.

---

## Resolved

| # | Assumption | Resolution | Date |
|---|-----------|------------|------|
| R1 | `minSdkVersion` — unknown target device | Android 16 / API 36, dedicated → min 31, target 36 | 2026-07-28 |
| R2 | Speed range might be a walking-pad ~6 km/h | 1.0–16.0 km/h confirmed; full folding treadmill. The earlier scepticism was wrong. | 2026-07-28 |
| R3 | Which FTMS characteristics exist | All six present | 2026-07-28 |
| R4 | Whether bonding is required | Not required | 2026-07-28 |
| R5 | Whether a vendor protocol fallback exists | `FFE0`/`FFF0` are FitShow transparent serial; undocumented, not a viable fallback | 2026-07-28 |
| R6 | Whether HR has a dedicated service | `180D` present; preferred over the FTMS field | 2026-07-28 |
| R7 | Whether `0x2ACC` could be trusted | **No** — claims incline target setting on a machine with no incline | 2026-07-28 |

---

## Note on R7

R7 is the most useful finding so far and the one most likely to have caused a subtle
bug. An implementation that gated features on `0x2ACC` would have shown incline
controls on a treadmill with no incline, and would have trusted a Speed Target Setting
bit that may well be equally fictional.

The general lesson, which applies to the rest of this device: **a vendor shim's
self-description is a marketing claim, not an API contract.** Verify against observed
behaviour.
