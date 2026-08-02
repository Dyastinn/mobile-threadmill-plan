# Flutter track: implementation index

Same phase order and goals as the original plan (see
[`../../phases/README.md`](../../phases/README.md)); this index only differs
where the UI framework actually changes how a phase runs. Everything about
*why* the phases are ordered this way, the risk register, and the standing
rules is not repeated here — read the original for that, it doesn't depend on
Flutter vs. MAUI.

Every phase 00–15 is written out here. Depth is deliberately uneven: Phase 00
and the seam (01b) are closest to the original track's density since they're
what actually gets built first; phases 02–15 are a leaner spec+tutorial
hybrid — goal, concept, concrete Dart/Flutter shapes, tests, acceptance —
since the exact Dart idioms for anything downstream of Phase 01a's real
parsers will get refined once real code exists to refine against, the same
reason `FakeTreadmillService` (Phase 01b) exists at all: build against a seam
now, treat the detail as revisable once it's actually next in line.

---

## How a phase actually runs (the collaboration model)

Unchanged in spirit from the original track, adapted for Flutter's own split
between logic and UI:

### Logic (BLE, parsers, providers, state notifiers): you write it, the agent teaches

1. **Concept first.** The agent explains the pattern, links the real
   [dart.dev](https://dart.dev/guides) or [flutter.dev](https://docs.flutter.dev)
   doc, flags anything genuinely new.
2. **Spec, not code.** A description of what the file/class should do, an
   interface to satisfy, a short illustrative snippet showing the *shape* of
   the solution, never the whole thing.
3. **You write it.**
4. **Review.** The agent reads the real file, flags bugs, explains better
   patterns where they exist.
5. **Verify together**: run `dart test` / `flutter test`, run the app.

### UI (widgets, screens, theming): the agent writes it

Every phase that produces a screen or a reusable widget includes the **full
widget code**, built on the shared theme
(`docs/learning/`, once the monochrome theme is ported — see Phase 00's UI
tasks). You paste it in, wire it to your provider's actual names, and run it.
This is a deliberate exception to "you write it": UI layout isn't what this
project is trying to teach; BLE, protocol parsing, state machines, and data
modelling are — identical reasoning to the original track.

## Division of labour

| Actor | Can do | Cannot do |
|-------|--------|-----------|
| Agent | Explain concepts, design the shape of a logic task, **write widget code directly**, review code, write docs, run tests you ask it to run | Write your *logic* code for you (BLE, parsers, providers, state machines), connect Bluetooth, observe the treadmill |
| Project owner | Write the logic code, wire up and adjust the agent's widget code, run the app on the phone, walk on the belt, capture logs, report observations | — |

---

## Phase order

Identical order and gates to the original track — see
[`../../phases/README.md#phase-order`](../../phases/README.md#phase-order)
for the full table with hardware/size/gate columns. Status in **this** track:

| # | Phase | Status here |
|---|-------|--------------|
| [00](phase-00-probe-app/) | Probe App | **Written. Start here.** |
| 01a | Protocol Decode & Fixtures | Blocked — same reason as the original track: needs Probe Part C + C7, not yet captured |
| 01b | `TreadmillService` seam + fake | **Not blocked — seam already written**, see [`treadmill_service.dart`](../packages/myhi_companion_core/lib/treadmill/treadmill_service.dart). `FakeTreadmillService` itself is still to write |
| [02](phase-02-connection-hardening/) | Connection Hardening | Written |
| [03](phase-03-live-dashboard/) | Live Dashboard + contribution graph | Written |
| [04](phase-04-workout-engine/) | Workout Engine | Written |
| [05](phase-05-treadmill-control/) | Treadmill Control | Written — may be void, decided by Phase 00 V2 |
| [06](phase-06-recording-schema/) | Recording & Schema | Written |
| [07](phase-07-foreground-service/) | Foreground Service | Written — flags an open isolate-boundary risk to settle early |
| [08](phase-08-settings/) | Settings | Written |
| [09](phase-09-backup-minimal/) | Backup (minimal) | Written |
| [10](phase-10-statistics/) | Statistics | Written |
| [11](phase-11-split-screen/) | Split Screen | Written |
| [12](phase-12-performance/) | Performance | Written |
| [13](phase-13-ui-polish/) | UI Polish | Written |
| [14](phase-14-endurance/) | Endurance Testing | Written |
| [15](phase-15-backup-polish/) | Backup Polish (optional) | Written |

Phase 01a and 01b split for the same reason as the original: the real parsers
need physical treadmill access (a scarce resource), the seam interface
doesn't. Phase 03 depends only on 01b.

---

## Root reference docs (shared, not forked)

| Doc | What it is |
|-----|-----------|
| [`../../phases/phase-01-protocol-decode/README.md`](../../phases/phase-01-protocol-decode/README.md) | FTMS protocol reference — bytes, flags, traps. Framework-independent |
| [`../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md) | Measured device facts and open verdicts |
| [`../../phases/phase-00-probe-app/HUMAN-RUNBOOK.md`](../../phases/phase-00-probe-app/HUMAN-RUNBOOK.md) | The manual operator procedure — same steps regardless of what wrote the app running them |
| [`../../captures/`](../../captures/) | Raw BLE capture files |
| [`../../phases/README.md#risk-register`](../../phases/README.md#risk-register) | Risk register |

`../packages/myhi_companion_core/lib/treadmill/treadmill_service.dart` is this
track's equivalent of the original's
`phase-01-protocol-decode/ITreadmillService.cs`: the seam Phases 03–11 build
against instead of against BLE directly.
