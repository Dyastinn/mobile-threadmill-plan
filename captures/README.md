# Captures

Raw BLE session captures produced by the Phase 00 probe app. **Committed to the
repository** — these are the project's primary evidence and the source of every
parser test fixture.

## Format

One file per session: `session-YYYY-MM-DD-HHmm.jsonl`. One JSON object per line,
append-only, flushed per line so a crash costs the last line at most.

```json
{"t":"2026-07-31T14:22:31.402Z","kind":"write","uuid":"2AD9","hex":"02 8A 02"}
{"t":"2026-07-31T14:22:31.588Z","kind":"indicate","uuid":"2AD9","hex":"80 02 01"}
{"t":"2026-07-31T14:22:33.101Z","kind":"notify","uuid":"2ACD","hex":"08 00 ..."}
{"t":"2026-07-31T14:22:35.000Z","kind":"read","uuid":"2AD4","hex":"64 00 40 06 0A 00"}
{"t":"2026-07-31T14:22:40.000Z","kind":"console","speedKph":6.5,"distanceM":420,"timeSec":310}
{"t":"2026-07-31T14:22:41.000Z","kind":"note","ref":"<event id>","ok":true,
 "text":"belt actually sped up; console showed 6.5"}
```

| kind | Meaning |
|------|---------|
| `read` | Characteristic read result |
| `write` | Bytes the app sent |
| `notify` / `indicate` | Bytes the device pushed |
| `console` | What the treadmill's own display showed, entered by the operator |
| `note` | Operator annotation on an earlier event: was it correct, and what happened physically |
| `adv` | Raw advertisement bytes for a scanned device |
| `gatt` | Connection lifecycle events and numeric GATT status codes |

## Rules

- **Hex is space-separated, uppercase, with leading zeros preserved.** `02 00` and
  `2 0` are not the same thing and the second is unusable as a fixture.
- Never edit a capture file by hand. If something is wrong, capture again and note why.
- `console` events are the highest-value lines in the file — they are the only thing
  that can prove a decoder correct rather than merely non-crashing.
- `note` events with `ok: true` are the record of what is *right*: this exact byte
  sequence produced this exact physical result.

## Deriving fixtures

Phase 01 turns these into unit test fixtures. Keep the original file alongside the
extracted fixture so the provenance of every test vector is traceable to a timestamp
in a real session.
