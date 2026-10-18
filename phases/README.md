# Phases: implementation index

One folder per phase. Phase 00 is built by an implementing agent working alone,
appropriate for a throwaway diagnostic instrument. **Every phase from here on is
built by the project owner, learning React Native, BLE and Android as they go,
with the agent teaching rather than implementing, except UI code, which the agent
writes directly.** See "How a phase actually runs" below for what that split means
day-to-day, and `../docs/learning/` for the standing teaching material (Expo/React
Native primer, React and TypeScript essentials, device setup, glossary, doc links,
the monochrome theme).

A phase folder plus the root reference docs it names is everything needed for that
phase; you don't need to read the other phase folders ahead of time. Every phase
below is written as a concrete walkthrough: numbered steps, not just a feature list,
with a real documentation link next to anything genuinely new rather than "go read
the docs."

> **History.** This plan was first written for .NET MAUI and moved to Expo in
> October 2026, before any app code existed. The phases, their order, and every
> protocol finding are unchanged; only the code shapes, file paths and
> technology records were rewritten. The MAUI version is in git history if you
> ever want to compare.

---

## How a phase actually runs (the collaboration model)

**Two different rules, by kind of code:**

### Logic (BLE, parsers, services, hooks, state machines, Kotlin): you write it, the agent teaches

For each step in a phase's task list:

1. **Concept first.** The agent explains what you're about to build and why: the
   pattern, the relevant part of `docs/learning/`, a link to the real Expo/React/
   Android doc (see `docs/learning/03-Doc-Links.md`), anything genuinely new. Ask
   questions here; this is the part that's supposed to be slow.
2. **Spec, not code.** The agent describes what the file/function should do and what
   "done" looks like: files touched, an interface to satisfy, a short illustrative
   snippet showing the *shape* of the solution (never the whole thing), an
   acceptance bullet. It never hands over the actual implementation.
3. **You write it.** In your own editor, at your own pace.
4. **Review.** The agent reads the real file you wrote, flags bugs, explains better
   patterns where they exist, and answers "why is this wrong" rather than just
   fixing it silently.
5. **Verify together**: run the tests, run the app, before moving to the next
   task.

"Hooks" here means the per-screen hooks (`useDashboard`, `useTreadmillControl`, ...)
that hold a screen's state and talk to the services. They do the job a ViewModel
does in other frameworks, and they're logic, so they're on your side of the line.

### UI (screens, components, styles): the agent writes it

Every phase that produces a screen or a visual component includes the **full TSX
code** for it, built on the shared monochrome theme
(`docs/learning/04-Monochrome-Theme.md`, implemented in
`src/features/shared/theme/` during Phase 00). You paste it in, adjust it to match
your hook's actual return values, and run it. You are not expected to design
layouts or pick colors. The agent still explains what the code does and why it's
structured that way (so the *concepts*, like flexbox layout, `FlatList`, and
reading the theme from a hook, still transfer), but you don't have to produce the
TSX yourself. This is a deliberate exception to "you write it": UI layout is not
what this project is trying to teach; BLE, protocol parsing, state machines, and
data modelling are.

The line between the two is the hook call. A screen component calls one hook and
renders what it returns. Everything above the hook call is UI (agent); the hook
and everything it calls is logic (you).

New vocabulary gets added to `docs/learning/02-Glossary.md` as it comes up, not
dumped all at once.

## Division of labour: not optional

| Actor | Can do | Cannot do |
|-------|--------|-----------|
| Agent | Explain concepts, design the shape of a logic task, **write UI/TSX code directly**, review code, write docs, run tests, builds and `adb` commands you ask it to run | Write your *logic* code for you (BLE, parsers, services, hooks, state machines, the Kotlin service), connect Bluetooth, observe the treadmill |
| Project owner | Write the logic code, wire up and adjust the agent's UI code, run the app on the phone, walk on the belt, capture logs, report observations | — |

When the phone is plugged in (or paired over wireless adb), the agent can install
builds, read `adb logcat`, check `dumpsys` output and pull capture files. It still
can't see the treadmill, so anything physical stays yours.

---

## Phase order

| # | Phase | Hardware | Size | Gate it opens |
|---|-------|----------|------|---------------|
| [00](phase-00-probe-app/) | **Probe App** | **Yes** | **L** | Everything. Nothing real is known until this runs. Also proves JS stays alive with the screen off (Part H) |
| [01a](phase-01-protocol-decode/) | Protocol Decode & Fixtures | No | M | **Blocked**: needs Probe Part C (matched pairs) + C7 (counter semantics), not yet done |
| [01b](phase-01-protocol-decode/) | `TreadmillService` contract + `FakeTreadmillService` | No | S | **Not blocked: start here once Phase 00's scaffold exists.** Unblocks 03, 04, 06 |
| [02](phase-02-connection-hardening/) | Connection Hardening | **Yes** | M | Reliable link for every later phase |
| [03](phase-03-live-dashboard/) | Live Dashboard + contribution graph | No | M | First real feature |
| [04](phase-04-workout-engine/) | Workout Engine | No | M | Lifecycle + connection-loss policy |
| [05](phase-05-treadmill-control/) | Treadmill Control | **Yes** | M | May be void: decided in Phase 00 |
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

No phase begins until the previous phase's acceptance criteria are met, with two
exceptions:

- **Phase 01b** (`TreadmillService` contract + `FakeTreadmillService`) needs no
  probe data at all: the contract already exists at
  `phase-01-protocol-decode/treadmillService.ts`. It can start as soon as Phase
  00's task 0.1 scaffold exists, even before the treadmill session.
- **Phase 01a** (the actual parsers) is still blocked: Probe Part C (four-plus
  matched console-vs-hex pairs) and C7 (counter reset semantics) haven't been done
  yet. See `phase-00-probe-app/HUMAN-RUNBOOK.md`.

Phase 03 depends only on 01b, not 01a. It builds against `FakeTreadmillService`.

---

## Renumbering: what changed and why

Plan v2.1 had Phase 0 as bare scaffolding, Phase 1 scan, Phase 2 connect, Phase 3
protocol discovery. Four phases had to complete before anyone could send a single
byte to the treadmill and see what came back.

**Phase 00 now delivers a working, installable diagnostic app.** It merges the
scaffold, scan, connect, hex dump, notification log, and a **control-point console**
where the operator sends commands to the treadmill by hand and records the raw
response. The point is to answer "what data should be sent to the treadmill, and what
is correct" on day one, with real bytes, instead of building three phases of product
on unverified assumptions.

Everything after that is unchanged in substance, only renumbered.

| New | Old | Note |
|-----|-----|------|
| 00 Probe App | 0 + 1 + part of 2 + tooling half of 3 | Merged and expanded |
| 01 Protocol Decode | analysis half of 3 | Desk work against captured hex |
| 02 Connection Hardening | 2 | Reconnect, backoff, GATT 133: now separate from "connect once" |
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

Most technical reference content lives inside the phase that owns it, not at the
repo root: the FTMS protocol spec splits across Phase 01 (parsing), Phase 02
(connection sequence and error codes), and Phase 05 (control point); the database
schema lives in Phase 06; each technology's decision record lives in the phase
that introduced it (see the Stack table in `../README.md`); measured device facts
and open assumptions live in `phase-00-probe-app/PHASE-00-FINDINGS.md`. What
remains genuinely project-wide:

| Doc | What it is |
|-----|-----------|
| `phase-01-protocol-decode/treadmillService.ts` | The seam. Phases 03–11 build against this, not against BLE. Lives here until Phase 01b moves it into `src/core/treadmill/` |
| `../captures/` | Raw capture files produced by the Phase 00 app |
| `../docs/learning/00-Expo-And-React-Native.md` | How the pieces fit: dev build vs. Expo Go, the JS thread vs. native code, prebuild, why background work is the hard part |
| `../docs/learning/00a-React-And-TypeScript-Essentials.md` | The React and TypeScript patterns this codebase leans on, explained properly rather than copied: effects and cleanup, refs vs. state, `useSyncExternalStore`, discriminated unions |
| `../docs/learning/03-Doc-Links.md` | Verified external documentation URLs, grouped by topic: every phase's "Reference docs" links come from here |
| `../docs/learning/04-Monochrome-Theme.md` | The shared theme every phase's UI code is built on, implemented in `src/features/shared/theme/` during Phase 00 |

---

## Risk register

Two risks were re-rated upward after the first Phase 00 capture, and one was added
by the move to Expo. Carried forward from the original project plan, updated to
current phase numbers and findings.

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| **FTMS shim is incomplete or deviates from spec** | **Medium-High** ↑ | Parsers and control need rework | Confirmed FitShow module, not a native implementation. Phase 00's hex capture reveals it before code depends on it. |
| Control point doesn't actually honour commands | **Medium** ↑ | Phase 05 is void | Feature bit is untrustworthy; gate on live handshake (see Phase 05). Preliminary Phase 00 finding: it does honour commands, but `Start` may not preserve a pre-set target speed. See `phase-00-probe-app/PHASE-00-FINDINGS.md` V2. Read-only app is still worth shipping if it turns out void. |
| Counters are cumulative, not per-session | Medium | Phase 06 logic changes | Probe Part C7: **still unanswered, highest-priority unknown**. See `phase-00-probe-app/PHASE-00-FINDINGS.md` V1. |
| **JavaScript stops running with the screen off** | **Medium** (new) | Recording and reconnect stall mid-workout | React Native pauses JS timers when the app is in the background unless a headless JS task is active. Phase 00 task 0.12 builds a minimal foreground service + keep-alive task, and runbook Part H tests it on the treadmill before any product code depends on it. Fallback if it fails: move BLE and recording into the Kotlin module. |
| **HyperOS kills the foreground service** | **Medium-High** ↑ | Long workouts unreliable | Xiaomi-specific checklist in Phase 07; verify all four boxes before Part H and before Phase 14 |
| `react-native-ble-manager` breaks on a future React Native / Expo upgrade | Low-Medium (new) | Build or BLE breaks after an upgrade | Pin the Expo SDK; upgrade deliberately, one SDK at a time, with a probe-app smoke test after each. Everything above `TreadmillService` is library-free, so a swap touches `src/features/bluetooth/` only. |
| GATT 133 on reconnect | High | Flaky connections | ble-manager already closes the GATT object on every disconnect; Phase 00/02 add `autoconnect: false` and the discovery delay. Empirical. RSSI −49 dBm means any disconnect is software, not radio. |
| Handgrip heart rate is unusable in practice | Medium | HR features cut | Decided via Probe Part G: see `phase-00-probe-app/PHASE-00-FINDINGS.md` V3; cutting it is an acceptable outcome |
| Notification rate much higher than 1 Hz | Low | UI jank | Phase 03 throttling already specified |
| Device uses a random resolvable BLE address | Low | Device persistence by MAC breaks | Probe Part F2; match on name if so |

---

## Standing rules for every phase

- `npx tsc --noEmit` and `npx expo lint` both clean: zero errors, zero warnings.
- All prior phase tests still pass (`npm test`).
- Nothing in `src/core/` imports `react`, `react-native` or any `expo-*` package.
  The lint rule enforces it; don't disable it, move the code instead.
- Metric in the database, always. Convert at display time only.
- All timestamps UTC plus a stored local offset.
- Never gate a feature on `0x2ACC`: it is proven to over-claim. Gate on observed
  behaviour. See `phase-01-protocol-decode/README.md`'s `0x2ACC` section.
- Never call a Bluetooth stop command an emergency stop. The safety key is the
  emergency stop.
- Never hand-edit `src/android/`. It's regenerated by `npx expo prebuild`. Native
  configuration goes in `app.json`, a config plugin, or a local module.
