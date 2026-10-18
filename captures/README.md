# Captures

Raw BLE session captures produced by the Phase 00 probe app. **Committed to the
repository.** These are the project's primary evidence and the source of every
parser test fixture.

## Format

One file per session: `session-YYYY-MM-DD-HHmm.jsonl`. One JSON object per line,
append-only, written per line so a crash costs the last line at most.

```json
{"id":41,"t":"2026-07-31T14:22:31.402Z","kind":"write","uuid":"2AD9","hex":"02 8A 02"}
{"id":42,"t":"2026-07-31T14:22:31.588Z","kind":"indicate","uuid":"2AD9","hex":"80 02 01"}
{"id":43,"t":"2026-07-31T14:22:33.101Z","kind":"notify","uuid":"2ACD","hex":"08 00 ..."}
{"id":44,"t":"2026-07-31T14:22:35.000Z","kind":"read","uuid":"2AD4","hex":"64 00 40 06 0A 00"}
{"id":45,"t":"2026-07-31T14:22:40.000Z","kind":"console","speedKph":6.5,"distanceM":420,"timeSec":310}
{"id":46,"t":"2026-07-31T14:22:41.000Z","kind":"note","ref":42,"ok":true,
 "text":"belt actually sped up; console showed 6.5"}
{"id":47,"t":"2026-07-31T14:22:45.000Z","kind":"heartbeat"}
```

`id` is a counter that restarts at 1 for each session file; a `note`'s `ref` points
at the `id` of the line it annotates.

| kind | Meaning |
|------|---------|
| `read` | Characteristic read result |
| `write` | Bytes the app sent |
| `notify` / `indicate` | Bytes the device pushed |
| `console` | What the treadmill's own display showed, entered by the operator |
| `note` | Operator annotation on an earlier event: was it correct, and what happened physically |
| `adv` | Raw advertisement bytes for a scanned device |
| `gatt` | Connection lifecycle events and numeric GATT status codes |
| `heartbeat` | A JavaScript timer tick every 5 s while the keep-alive is on (Phase 00 task 0.12). A gap between heartbeats means JS timers stalled; a gap between `notify` lines means BLE delivery stalled |

## Rules

- **Hex is space-separated, uppercase, with leading zeros preserved.** `02 00` and
  `2 0` are not the same thing and the second is unusable as a fixture.
- Never edit a capture file by hand. If something is wrong, capture again and note why.
- `console` events are the highest-value lines in the file. They are the only thing
  that can prove a decoder correct rather than merely non-crashing.
- `note` events with `ok: true` are the record of what is *right*: this exact byte
  sequence produced this exact physical result.

## Getting a capture off the phone

The app's capture screen has a share button (Drive, email, Nearby Share). With the
phone on adb, a debug build's files can also be pulled directly:

```powershell
adb exec-out run-as com.myhi.companion cat files/captures/session-2026-07-31-1422.jsonl > captures/session-2026-07-31-1422.jsonl
```

(`run-as` only works on debuggable builds, i.e. the development build, not the
release APK.)

## Deriving fixtures

Phase 01 turns these into unit test fixtures. Keep the original file alongside the
extracted fixture so the provenance of every test vector is traceable to a timestamp
in a real session.
