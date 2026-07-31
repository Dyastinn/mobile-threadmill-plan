# Phase 05 — Treadmill Control

> **This phase may not exist.** Phase 00 verdict V2 decides. If the control point does
> not honour commands, skip to Phase 06 — a read-only app is still worth shipping, and
> "void" here is a finding, not a failure.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 04 · **Gated on:** V2

---

## Goal

Change treadmill speed from the app, reliably. Everything here was already proven by
hand in the Phase 00 control console; this phase turns that into product UI.

## Reference docs

- `../../05-FTMS-Protocol.md` §7 — commands, response format, result codes
- `../phase-00-probe-app/PHASE-00-FINDINGS.md` V2 — the exact byte sequences that
  worked, with the operator's notes on what physically happened

---

## Features

- Increase / decrease by the device's minimum increment (0.1 km/h)
- Preset speed buttons **generated from the range read from `0x2AD4`**, not hardcoded.
  For 1.0–16.0: 2, 4, 6, 8, 10, 12 km/h. Generated, so the code survives a firmware
  change or a different treadmill
- Stop

## Availability is decided by behaviour, not by a feature bit

The `Speed Target Setting` bit is set on this device — and the same bitmask claims
incline support on a machine with no incline, so it carries little weight. Gate on the
live handshake:

1. `0x2AD9` was discovered
2. `Request Control` (`00`) returned `0x01`
3. The first `Set Target Speed` returned `0x01`

Any step fails → disable speed controls **for the session**, log the result code, show
the user a plain explanation.

---

## Implementation requirements

- **Debounce and coalesce.** Rapid +/- taps produce **one** write of the final target,
  not one per tap. ~300 ms debounce.
- Clamp to the `0x2AD4` range; round to the device increment.
- **Serialise writes.** One outstanding command, wait for the indication (3 s timeout)
  before the next. Concurrent writes get dropped or error, and the result is
  indistinguishable from a broken device.
- Surface failures with the **actual result code meaning** — `Control Not Permitted`
  and `Invalid Parameter` need different messages, because they need different user
  actions.
- `0x05 Control Not Permitted` → re-issue `Request Control`, then retry once.
- `0xFF` on `0x2ADA` → disable controls, re-request control before re-enabling.
- **Re-issue `Request Control` after every reconnect** before enabling controls.
- Optimistic UI is fine, but **reconcile against the next `0x2ACD` notification** and
  revert if the machine did not comply.
- If V2 found control permission expires after idle, refresh it before a command rather
  than letting the first tap after a pause fail.

---

## Safety — not pedantry

"Emergency Stop" was in an earlier draft. **Do not name it that.** A Bluetooth stop
command over an unreliable link is not an emergency stop; the physical safety key is.

- Label it **"Stop"**.
- State in the UI that the safety key is the emergency stop.
- Never send `Start` (`07`) without a deliberate on-screen user action. **Not** from a
  notification action, **not** from restored state, **not** from auto-reconnect.

---

## Tests

| Test | Expected | |
|------|----------|---|
| Increase speed | Belt actually speeds up | `[HUMAN]` |
| Decrease speed | Belt slows | `[HUMAN]` |
| Rapid 10× tap on + | **One** write; final speed correct | `[HUMAN]` |
| Set speed below minimum | Clamped, no error | |
| Set speed above maximum | Clamped, no error | |
| Stop | Belt stops | `[HUMAN]` |
| Control without Request Control | Fails gracefully, clear message | |
| Set speed after reconnect | Works — control re-requested | `[HUMAN]` |
| Result code 0x05 handling | Re-requests control, retries once | |
| Indication timeout | Surfaces after 3 s, does not hang the queue | |

## Acceptance

- [ ] 20 consecutive speed changes succeed
- [ ] No command ever sent outside the device's range
- [ ] Rapid tapping produces one write
- [ ] Nothing in the app can start the belt without a deliberate on-screen tap
