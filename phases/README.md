# Phases

One folder per phase. Each folder has everything that phase needs; you don't have to
read ahead.

- **Phase 00** is built by the agent. It's a test app for poking the treadmill, not
  the real product.
- **From Phase 01 on, you build the app.** The agent teaches and reviews your logic,
  and writes the UI code for you.

Learning material lives in [`../docs/learning/`](../docs/learning/).

> The plan was first written for .NET MAUI and moved to Expo in October 2026, before
> any code existed. The phases, their order and every treadmill finding stayed the
> same. The old version is in git history.

---

## How a phase works

### Logic: you write it, the agent teaches

Logic means Bluetooth code, parsers, services, hooks, state machines, and the Kotlin
service. For each task:

1. **Concept.** The agent explains the idea and points you to the right doc. Ask
   anything here.
2. **Spec.** The agent tells you what the file should do and how you'll know it's
   done, with a small code outline. Never the full code.
3. **You write it.**
4. **Review.** The agent reads your file and explains any problems instead of
   silently fixing them.
5. **Check together.** Run the tests and the app before moving on.

"Hooks" here means one hook per screen (`useDashboard`, `useSpeedControl`, ...). A
hook holds the screen's state and talks to the services. It's logic, so you write it.

### UI: the agent writes it

Screens and components come with **full code**, built on the shared gray theme
([`04-Monochrome-Theme.md`](../docs/learning/04-Monochrome-Theme.md)). You paste it
in and adjust it to match your hook. The agent still explains how it works.

**The dividing line:** a screen calls one hook and shows what the hook returns. The
screen is the agent's. The hook, and everything it calls, is yours.

When a new word comes up, it goes into
[`02-Glossary.md`](../docs/learning/02-Glossary.md).

## Who does what

| | Can do | Can't do |
|---|---|---|
| Agent | Explain, plan tasks, **write UI code**, review your code, write docs, run tests, builds and `adb` commands | Write your logic code, touch the treadmill |
| You | Write the logic, paste in the UI, run the app on the phone, walk on the belt, capture logs, report what happened | — |

With the phone connected over `adb`, the agent can install builds, read logs and pull
files. Anything physical is yours.

---

## Phase order

| # | Phase | Needs the treadmill? | Size | What it unlocks |
|---|-------|---------------------|------|-----------------|
| [00](phase-00-probe-app/) | **Probe app** | **Yes** | **L** | Everything. Also tests that the app keeps running with the screen off (Part H) |
| [01a](phase-01-protocol-decode/) | Decode the Bluetooth data | No | M | **Blocked** until Phase 00 captures the data it needs |
| [01b](phase-01-protocol-decode/) | `TreadmillService` + a fake treadmill | No | S | **Start here** once Phase 00's project exists. Unblocks 03, 04, 06 |
| [02](phase-02-connection-hardening/) | Stay connected | **Yes** | M | Reconnecting on its own |
| [03](phase-03-live-dashboard/) | Dashboard + workout calendar | No | M | The first real screen |
| [04](phase-04-workout-engine/) | Workout engine | No | M | Start/pause/stop logic |
| [05](phase-05-treadmill-control/) | Speed control | **Yes** | M | May be skipped, depending on Phase 00 |
| [06](phase-06-recording-schema/) | Saving workouts | No | M | The database |
| [07](phase-07-foreground-service/) | Background service | **Yes** | M | Recording with the screen off |
| [08](phase-08-settings/) | Settings | No | S | |
| [09](phase-09-backup-minimal/) | Backup | No | M | Your data can't be lost |
| [10](phase-10-statistics/) | Statistics and history | No | M | |
| [11](phase-11-split-screen/) | Split screen | No | M | Use it next to YouTube |
| [12](phase-12-performance/) | Performance | **Yes** | S | |
| [13](phase-13-ui-polish/) | Polish | No | M | Release candidate |
| [14](phase-14-endurance/) | Endurance tests | **Yes** | M | Proves it beats FitShow |
| [15](phase-15-backup-polish/) | Backup extras (optional) | No | M | Only once you use it daily |

Finish each phase before starting the next, with two exceptions:

- **Phase 01b** needs no treadmill data. It can start as soon as Phase 00's task 0.1
  has created the project.
- **Phase 01a** stays blocked until runbook Part C (four console-vs-hex readings) and
  C7 (do the counters reset?) are done. See
  [`HUMAN-RUNBOOK.md`](phase-00-probe-app/HUMAN-RUNBOOK.md).

Phase 03 only needs 01b, not 01a. It uses the fake treadmill.

---

## Where reference material lives

Most reference material sits in the phase that uses it:

- The Bluetooth protocol: Phase 01 (reading data), Phase 02 (connecting), Phase 05
  (sending commands).
- The database tables: Phase 06.
- Why each library was chosen: the phase that adds it.
- What's been measured on the treadmill, and what's still unknown:
  [`PHASE-00-FINDINGS.md`](phase-00-probe-app/PHASE-00-FINDINGS.md).

Project-wide files:

| File | What it is |
|-----|-----------|
| `phase-01-protocol-decode/treadmillService.ts` | The interface most phases build against. Moves into `src/core/treadmill/` in Phase 01b |
| `../captures/` | Raw Bluetooth logs from the test app |
| `../docs/learning/00-Expo-And-React-Native.md` | How Expo works underneath, and why background work is the hard part |
| `../docs/learning/00a-React-And-TypeScript-Essentials.md` | The React and TypeScript patterns this project uses, explained |
| `../docs/learning/03-Doc-Links.md` | Checked links to official docs |
| `../docs/learning/04-Monochrome-Theme.md` | The gray theme every screen uses |

---

## Risk register

| Risk | Chance | What it breaks | Plan |
|------|--------|----------------|------|
| **The treadmill's FTMS layer doesn't follow the spec** | Medium-high | Decoding and control need rework | Phase 00 captures real bytes before any code relies on them |
| The treadmill ignores speed commands | Medium | Phase 05 can't happen | Only trust a command that actually worked (Phase 05). Early sign: it works, but `Start` forgets the target speed (Finding V2). A read-only app is still useful |
| Distance/calorie counters don't reset between sessions | Medium | Phase 06 must subtract a starting value | Runbook Part C7. **Still unknown, top priority** (Finding V1) |
| **JavaScript stops running with the screen off** | Medium | Recording and reconnecting stall mid-workout | React Native pauses timers in the background. Phase 00 task 0.12 builds a fix (background service + keep-alive task) and runbook Part H tests it on the treadmill. If it fails, Bluetooth moves into Kotlin |
| **HyperOS kills the app** | Medium-high | Long workouts stop | The four HyperOS settings. Check them before Part H and Phase 14 |
| A future Expo upgrade breaks the Bluetooth library | Low-medium | Build or Bluetooth breaks | Upgrade one Expo version at a time and test the probe app after. Only `src/features/bluetooth/` touches the library |
| GATT error 133 when reconnecting | High | Flaky connections | The library already closes old connections. Phase 00/02 add the other known fixes. Signal is strong at −49 dBm, so a drop means a software problem, not range |
| Handgrip heart rate is unusable | Medium | Heart rate gets cut | Runbook Part G decides. Cutting it is fine (Finding V3) |
| Data arrives much faster than once a second | Low | Laggy screen | Phase 03 limits updates to 4 per second |
| The treadmill's Bluetooth address changes | Low | Remembering the device by address fails | Runbook Part F checks. Match by name if so |

---

## Rules for every phase

- `npx tsc --noEmit` and `npx expo lint` show nothing: no errors, no warnings.
- Every earlier test still passes (`npm test`).
- Nothing in `src/core/` imports `react`, `react-native` or `expo-*`. If lint
  complains, move the code; don't turn the rule off.
- Save metric values only. Convert to other units only on screen.
- Save times in UTC, plus the local time-zone offset.
- Never decide what the app can do from `0x2ACC`: it's known to lie. Decide from what
  the treadmill actually does.
- Never call a Bluetooth stop an "emergency stop". The safety key is the emergency
  stop.
- Never edit `src/android/`. It's regenerated on every build. Change `app.json`, a
  config plugin, or a Kotlin module instead.
