# Phase 03 — Live Dashboard

**Hardware:** none for development (`FakeTreadmillService`), yes to verify
**Size:** M · **Blocked by:** Phase 01

---

## Goal

Display live metrics, smoothly, without blocking. Built entirely against
`../../ITreadmillService.cs` — develop and test with `FakeTreadmillService`, verify
with hardware at the end.

## Reference docs

- `../../ITreadmillService.cs`
- `../../05-FTMS-Protocol.md` §4 (fields), §4a (heart rate)
- `../phase-00-probe-app/PHASE-00-FINDINGS.md` — notification rate, flags observed,
  V3 heart rate verdict

---

## Features

- Speed, distance, calories, elapsed time, machine status
- Heart rate **only if V3 said usable**
- Connection indicator
- **Fields the device does not actually send are hidden, not shown as `--`.** A row of
  dashes is a promise the app can't keep.

### Field visibility

Comes from the **union of observed `0x2ACD` flag bits** (Phase 01's capability
tracker), **not** from `0x2ACC`. The feature bitmask on this device over-claims — it
advertises incline target setting on a machine with no incline. Log it; never branch
on it.

### Heart rate source

`0x2A37` from `180D` if usable; the FTMS field only if `180D` is dead; **removed from
the UI entirely if V3 said unusable.** If V3 said marginal, keep recording it (Phase
06) and hide it here.

---

## Implementation requirements

- Notification callbacks are already marshalled to the UI thread at the service
  boundary — do not marshal again, and do not assume they aren't.
- **Throttle UI updates to at most 4 Hz** even if notifications arrive faster. Above
  that, MAUI janks in split screen for no visible benefit.
- Do not allocate per notification beyond what's necessary; the parser writes into a
  struct, so keep it that way through the binding layer.
- Format at display time only. **Never convert units before storage** — metric in the
  database, always.

## Tests

- Fake service: 10-minute stream renders continuously, no frozen UI
- Sparse-field scenario: absent fields are hidden, present ones render
- Dropout scenario: connection indicator reflects it within a second
- `[HUMAN]` Walk 5 minutes; every field updates and matches the treadmill console
- `[HUMAN]` Rotate the device mid-workout; no crash, no reset

## Acceptance

- [ ] Smooth updates for 10 minutes with no UI stall
- [ ] `[HUMAN]` Displayed values match the console at three different speeds
- [ ] No field shows a placeholder for data the device never sends
