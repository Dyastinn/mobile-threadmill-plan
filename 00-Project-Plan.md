# MyHi Companion — Project Plan

> Personal Android companion app for the MY-HI Q8Y treadmill over Bluetooth FTMS.
> Plan version 2.2, updated 2026-07-31. Phases now live in `phases/`, and Phase 0
> was rebuilt into a working probe app. Supersedes the original phase list.

> ## Phase execution now lives in [`phases/`](phases/)
>
> **This document is context now: vision, stack, risk register, and the reasoning
> behind each phase. It's no longer the work order.** Each phase is its own
> self-contained folder with tasks, traps, tests and acceptance criteria, sized to
> hand to an implementing agent one at a time.
>
> Start at [`phases/README.md`](phases/README.md).
>
> **v2.2 changes:**
> - **Phase 0 is now a working diagnostic app**, not scaffolding. It merges the old
>   Phases 0-2 and the tooling half of Phase 3, and adds a **control-point console**
>   where the operator sends bytes to the treadmill by hand and sees the raw
>   response, plus a **capture recorder** that logs which byte sequences produced
>   which physical result. That way "what should we send to the treadmill, and what
>   is correct?" gets answered on day one with real bytes, instead of after four
>   phases of building on assumptions nobody verified.
> - Everything after that is unchanged in substance, only renumbered. See the
>   mapping table in [`phases/README.md`](phases/README.md#renumbering--what-changed-and-why).
> - New [`captures/`](captures/) folder for raw JSONL BLE session logs, committed to
>   the repo. They're the project's primary evidence and the source of every parser
>   test fixture.

**v2.1 changes** (from the first nRF Connect capture, see `DEVICE.md`):
- Device identified as a **FitShow BLE module**, not a MY-HI-native implementation.
  FTMS is a vendor shim over a transparent UART bridge, so expect spec deviations.
- **`0x2ACC` feature flags proven unreliable.** The device claims incline target
  setting on a machine with no incline. Downgraded to advisory; Phases 4 and 6 now
  derive capability from observed behaviour instead.
- **`180D` Heart Rate Service found** — likely a better HR source than the FTMS
  field.
- **minSdk resolved: 31, target 36.** Legacy Bluetooth permission path deleted.
- **Phase 8 gains a HyperOS checklist.** Standard Android battery optimisation isn't
  enough on Xiaomi.
- Speed range 1.0-16.0 km/h **confirmed correct.** v2.0's scepticism about that
  figure was wrong; this is a full folding treadmill.

---

## How to read this document

This plan is written to be handed to an implementing agent (Claude Sonnet) working
in a repository, alongside a human operator (the project owner) who has physical
access to the treadmill.

**Division of labour is not optional and must be respected:**

| Actor | Can do | Cannot do |
|-------|--------|-----------|
| Implementing agent | Write code, write tests, write docs, run unit tests | Connect to Bluetooth, observe the treadmill, verify live metrics |
| Human operator | Run the app on a device, walk on the treadmill, capture logs, report observations | — |

Any task whose acceptance depends on observing real hardware is marked
**`[HUMAN]`**. The agent stops at those points and asks for results rather than
assuming them.

Any value in this plan or in `05-FTMS-Protocol.md` marked **`TBD`** is unknown
until it's measured on the real device. **The agent must not invent a value for a
`TBD` field.** If a `TBD` blocks progress, the correct move is to make it
configurable, read it from the device at runtime, or stop and ask.

---

## Vision

Replace FitShow for daily use with something meaningfully better.

- Connect directly to the treadmill over Bluetooth FTMS
- Display live workout metrics
- Control treadmill speed
- Record workout history, including per-workout telemetry
- Charts and statistics
- Work reliably in Android split screen alongside video apps
- Survive screen lock via a foreground service
- Fully offline; no account, no network calls

**Non-goals** (writing these down so they don't creep in): cloud sync, social
features, multi-user, iOS, Strava/Garmin export, training plans, coaching.

---

## Technology stack

| Component | Choice | Licence | Note |
|-----------|--------|---------|------|
| Framework | .NET MAUI (Android only) | MIT | Single target keeps conditional compilation out |
| Language | C# / .NET 10 | MIT | LTS; .NET 9 (STS) reached end of support 2026-05-12 |
| Bluetooth | Android BLE APIs via MAUI platform code | — | See BLE library decision below |
| Protocol | Bluetooth FTMS (service `0x1826`) | — | |
| Database | SQLite (`sqlite-net-pcl` or `Microsoft.Data.Sqlite`) | MIT | See decision below |
| Charts | LiveCharts2 | MIT | |
| MVVM | CommunityToolkit.Mvvm | MIT | |
| File dialogs | CommunityToolkit.Maui | MIT | `FileSaver`, `FilePicker` |
| Logging | Microsoft.Extensions.Logging | MIT | |
| DI | Built-in MAUI DI | MIT | |

All dependencies must be MIT / Apache-2.0 / BSD. No GPL, no commercial-licence
packages, no "free for personal use" terms.

### Stack decisions have moved

Every technology in the table above, including the two decisions that used to sit
inline here (BLE library, SQLite library), now has a full decision record in
[`02-Technology-Stack.md`](02-Technology-Stack.md): the problem it solves, why it
fits this project, at least three alternatives with pros, cons and learning curve,
why the alternatives got rejected, and long-term considerations. That document also
flags one open risk the original plan missed: **`LiveCharts2` hasn't reached a
stable 1.0 release after years in beta/RC**, with real 2026 maintenance-concern
reports worth reading before Phase 10 starts.

---

## Development rules

Every phase must:

- Compile with zero warnings
- Pass all prior phase tests (regression)
- Include the documentation listed in its Deliverables
- Have every `[HUMAN]` test executed and recorded before the phase is closed

No phase begins until the previous phase's acceptance criteria are met.

**Additional rule for this revision:** any assumption an implementer makes to get
unblocked must be written into `docs/ASSUMPTIONS.md` with the phase number, and
resolved before the phase closes.

---

## What changed from plan v1, and why

| Change | Reason |
|--------|--------|
| Phase 3 is now **Protocol Discovery**, not "read every characteristic" | The device's actual behaviour is unknown. Its deliverable is a hex-dump diagnostic screen + a filled-in protocol doc, not a feature. |
| Phase 3 also delivers a **fake treadmill service** | Decouples Phases 4-7 from having the hardware in the room. Roughly 40 lines, saves the whole project. |
| Phase 7 adds **`WorkoutId` GUID** and a **`WorkoutSample` table** | Portable identity is required for any backup/restore. Telemetry is required for per-workout charts. Both are schema changes and get cheaper the earlier they land. |
| **Foreground Service moved earlier** (Phase 8) | Every long-running test from Phase 7 onward is unreliable without it. |
| **Backup split**: minimal at Phase 10, polish deferred to Phase 16 | Protects data during the schema-churn phases without spending a week on merge/CSV/migration before anything is proven. |
| **Merge mode cut** from v1 backup | Costs 4x the test matrix for invented conflict semantics and a rare benefit. The GUID keeps the door open for later. |
| **Connection and workout state machines separated** | One diagram couldn't express "connection lost mid-workout", which is the most likely real failure. |
| Original "Phase 13 — Backup" collision resolved | v1 had two Phase 13s. |

---

## Phase overview — superseded by [`phases/`](phases/)

The v2.1 numbering below is kept only so the rest of this document still reads
correctly. **The authoritative phase list, with the actual work orders, is
[`phases/README.md`](phases/README.md).** The old-to-new mapping is there too.

<details>
<summary>v2.1 phase list (historical)</summary>

| # | Phase | Hardware needed | Rough size |
|---|-------|-----------------|------------|
| 0 | Project initialisation | No | S |
| 1 | Bluetooth discovery | **Yes** | S |
| 2 | Device connection | **Yes** | M |
| 3 | FTMS protocol discovery | **Yes** | L |
| 4 | Live dashboard | No (fake service) | M |
| 5 | Workout engine | No (fake service) | M |
| 6 | Treadmill control | **Yes** | M |
| 7 | Workout recording + schema | No (fake service) | M |
| 8 | Foreground service | **Yes** | M |
| 9 | Settings | No | S |
| 10 | Backup (minimal) | No | M |
| 11 | Statistics | No | M |
| 12 | Split screen | No | M |
| 13 | Performance | **Yes** | S |
| 14 | UI polish | No | M |
| 15 | Endurance testing | **Yes** | M |
| 16 | Backup polish (optional) | No | M |

</details>

The v2.2 order, in one line: **00 Probe App** then 01 Protocol Decode, 02 Connection
Hardening, 03 Dashboard, 04 Workout Engine, 05 Control, 06 Recording, 07 Foreground
Service, 08 Settings, 09 Backup, 10 Statistics, 11 Split Screen, 12 Performance, 13
Polish, 14 Endurance, 15 Backup Polish.

---

The old phase-by-phase task lists (Phase 0 through Phase 16) used to be written out
in full below this point. They've been removed: they duplicated `phases/` under a
different numbering scheme and had started drifting out of sync with it, which is
worse than not having them at all. For any phase's actual tasks, tests, and
acceptance criteria, go to [`phases/README.md`](phases/README.md) and the phase's
own folder.

## Documentation set

```
docs/
├── 00-Project-Plan.md            (this file)
├── 01-Architecture.md
├── 02-Technology-Stack.md        (exists — full decision record, see root)
├── 03-Project-Structure.md
├── 04-Bluetooth-LE.md
├── 05-FTMS-Protocol.md           ← byte-level spec, TBDs resolved in Phase 3
├── 05a-FTMS-Probe-Procedure.md   ← [HUMAN] procedure
├── 06-Connection-Management.md
├── 07-Workout-Engine.md
├── 08-Dashboard.md
├── 09-Treadmill-Control.md
├── 10-History.md
├── 11-Statistics.md
├── 12-Foreground-Service.md
├── 13-Split-Screen.md
├── 14-Database.md                ← schema
├── 15-Backup-Restore.md
├── 16-Testing.md
├── 17-Troubleshooting.md
├── 18-Release.md
├── ASSUMPTIONS.md                ← every unresolved guess, with phase number
└── DEVICE.md                     ← measured facts about the Q8Y and the phone
```

---

## Risk register

Updated in v2.1. Two risks got re-rated upward after the first capture.

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| **FTMS shim is incomplete or deviates from spec** | **Medium-High** ↑ | Parsers and control need rework | Confirmed FitShow module, not a native implementation. Phase 3 hex capture reveals it before code depends on it. |
| Control point doesn't actually honour commands | **Medium** ↑ | Phase 6 is void | Feature bit is untrustworthy; gate on live handshake. Read-only app is still worth shipping. |
| Counters are cumulative, not per-session | Medium | Phase 7 logic changes | Probe Part C7 — **still unanswered, highest-priority unknown** |
| **HyperOS kills the foreground service** | **Medium-High** ↑ | Long workouts unreliable | Xiaomi-specific checklist in Phase 8; verify all four boxes before Phase 15 |
| GATT 133 on reconnect | High | Flaky connections | Phase 2 mitigations; empirical. RSSI -49 dBm means any disconnect is software, not radio. |
| Handgrip heart rate is unusable in practice | Medium | HR features cut | Decide in Phase 3; cutting it is an acceptable outcome |
| Notification rate much higher than 1 Hz | Low | UI jank | Phase 4 throttling already specified |
| MAUI Android BLE background reliability | Medium | Core use case | Phase 8 early, Phase 15 validates |
| Device uses a random resolvable BLE address | Low | Device persistence by MAC breaks | Probe Part F2; match on name if so |

---

## Immediate next actions

**Done (2026-07-28):** initial nRF Connect capture — device identity, service list,
characteristic list, decoded feature flags, speed range. See `DEVICE.md`.

**Still blocking, in priority order** (also tracked in `ASSUMPTIONS.md`):

| # | Item | Blocks | Effort |
|---|------|--------|--------|
| 1 | Counter reset semantics (Probe C7) | Phase 7 entirely | 15 min on the belt |
| 2 | Does the control point work (Probe D1-D6) | Whether Phase 6 exists | 15 min on the belt |
| 3 | Raw hex for `0x2ACC` and `0x2AD4` | Parser unit test fixtures | 2 min, nRF Connect |
| 4 | Walking capture, matched console values (Probe C3) | Phase 4 correctness | 15 min on the belt |
| 5 | Is `0x1826` advertised? | Phase 1 scan filter | 1 min, nRF Connect |
| 6 | MAC address + address type | Device persistence, schema | 1 min, nRF Connect |
| 7 | Notification rate | Phase 4 throttling | 2 min |
| 8 | `0x2A37` vs FTMS heart rate comparison | Phase 4, whether HR ships | 5 min |

Items 3, 5, 6, 7 need no walking and take under ten minutes together. Do those next
even if the full walking session has to wait — either in nRF Connect now, or from
the Phase 00 app once it builds, which is faster and records them automatically.

**Then:** build [Phase 00](phases/phase-00-probe-app/), run
[`HUMAN-RUNBOOK.md`](phases/phase-00-probe-app/HUMAN-RUNBOOK.md) on the belt, fill
in [`PHASE-00-FINDINGS.md`](phases/phase-00-probe-app/PHASE-00-FINDINGS.md), then
Phase 01 turns the captures into parsers.

Phase 00 can start immediately. None of the outstanding unknowns affect building
the instrument that measures them — that's the whole point of the restructure: the
app that answers these questions is the first thing built, not the fourth.
