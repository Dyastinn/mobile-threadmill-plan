# Phases — implementation index

One folder per phase. Each folder is a **self-contained work order** for an
implementing agent (Claude Sonnet). A phase folder plus the root reference docs it
names is everything needed; do not read the other phase folders.

---

## How to work a phase

1. Read `phases/phase-NN-name/README.md` end to end before writing code.
2. Read only the root docs listed under **Reference docs** in that README.
3. Work `TASKS.md` in order where one exists; otherwise work the README's task list
   in order. Each task names the files it touches.
4. Stop at every **`[HUMAN]`** gate. Do not assume a result. Do not invent a value
   for a `TBD`. If a `TBD` blocks you, make it configurable, read it from the device
   at runtime, or stop and ask.
5. Any assumption made to get unblocked goes into `../ASSUMPTIONS.md` with the phase
   number, before the phase closes.
6. A phase closes only when every acceptance item is met, including `[HUMAN]` ones.

## Division of labour — not optional

| Actor | Can do | Cannot do |
|-------|--------|-----------|
| Implementing agent | Write code, tests, docs; run unit tests | Connect Bluetooth, observe the treadmill, verify live metrics |
| Human operator | Run the app on the phone, walk on the belt, capture logs, report observations | — |

---

## Phase order

| # | Phase | Hardware | Size | Gate it opens |
|---|-------|----------|------|---------------|
| [00](phase-00-probe-app/) | **Probe App** | **Yes** | **L** | Everything. Nothing real is known until this runs. |
| [01](phase-01-protocol-decode/) | Protocol Decode & Fixtures | No | M | Parsers + `FakeTreadmillService` → unblocks 03, 04, 06 |
| [02](phase-02-connection-hardening/) | Connection Hardening | **Yes** | M | Reliable link for every later phase |
| [03](phase-03-live-dashboard/) | Live Dashboard | No | M | First real feature |
| [04](phase-04-workout-engine/) | Workout Engine | No | M | Lifecycle + connection-loss policy |
| [05](phase-05-treadmill-control/) | Treadmill Control | **Yes** | M | May be void — decided in Phase 00 |
| [06](phase-06-recording-schema/) | Recording & Schema | No | M | Persistence; blocked by counter semantics |
| [07](phase-07-foreground-service/) | Foreground Service | **Yes** | M | All long-running tests |
| [08](phase-08-settings/) | Settings | No | S | |
| [09](phase-09-backup-minimal/) | Backup (minimal) | No | M | Data becomes survivable |
| [10](phase-10-statistics/) | Statistics | No | M | |
| [11](phase-11-split-screen/) | Split Screen | No | M | Core use case |
| [12](phase-12-performance/) | Performance | **Yes** | S | |
| [13](phase-13-ui-polish/) | UI Polish | No | M | Release candidate |
| [14](phase-14-endurance/) | Endurance Testing | **Yes** | M | Decides if it beats FitShow |
| [15](phase-15-backup-polish/) | Backup Polish (optional) | No | M | Only after daily use |

No phase begins until the previous phase's acceptance criteria are met. The one
exception is Phase 01, which is pure desk work and can start the moment Phase 00's
capture files land.

---

## Renumbering — what changed and why

Plan v2.1 had Phase 0 as bare scaffolding, Phase 1 scan, Phase 2 connect, Phase 3
protocol discovery. Four phases had to complete before anyone could send a single
byte to the treadmill and see what came back.

**Phase 00 now delivers a working, installable diagnostic app.** It merges the
scaffold, scan, connect, hex dump, notification log, and a **control-point console**
where the operator sends commands to the treadmill by hand and records the raw
response. The point is to answer "what data should be sent to the treadmill, and what
is correct" on day one, with real bytes, instead of building three phases of product
on unverified assumptions.

Everything after that is unchanged in substance — only renumbered.

| New | Old | Note |
|-----|-----|------|
| 00 Probe App | 0 + 1 + part of 2 + tooling half of 3 | Merged and expanded |
| 01 Protocol Decode | analysis half of 3 | Desk work against captured hex |
| 02 Connection Hardening | 2 | Reconnect, backoff, GATT 133 — now separate from "connect once" |
| 03 Live Dashboard | 4 | |
| 04 Workout Engine | 5 | |
| 05 Treadmill Control | 6 | |
| 06 Recording & Schema | 7 | |
| 07 Foreground Service | 8 | |
| 08 Settings | 9 | |
| 09 Backup (minimal) | 10 | |
| 10 Statistics | 11 | |
| 11 Split Screen | 12 | |
| 12 Performance | 13 | |
| 13 UI Polish | 14 | |
| 14 Endurance | 15 | |
| 15 Backup Polish | 16 | |

---

## Root reference docs

| Doc | What it is |
|-----|-----------|
| `../00-Project-Plan.md` | Vision, stack, non-goals, risk register |
| `../05-FTMS-Protocol.md` | Byte-level spec. Source of truth for every parser |
| `../05a-FTMS-Probe-Procedure.md` | `[HUMAN]` procedure the Phase 00 app automates |
| `../14-Database.md` | Schema, PRAGMAs, write strategy, query patterns |
| `../ITreadmillService.cs` | The seam. Phases 03–11 build against this, not against BLE |
| `../DEVICE.md` | Measured facts only. Never write a guess here |
| `../ASSUMPTIONS.md` | Every guess, with the phase it blocks |
| `../captures/` | Raw capture files produced by the Phase 00 app |

---

## Standing rules for every phase

- Compile with zero warnings.
- All prior phase tests still pass.
- Metric in the database, always. Convert at display time only.
- All timestamps UTC plus a stored local offset.
- Never gate a feature on `0x2ACC` — it is proven to over-claim. Gate on observed
  behaviour. See `../05-FTMS-Protocol.md` §2.
- Never call a Bluetooth stop command an emergency stop. The safety key is the
  emergency stop.
