# Phases — implementation index

One folder per phase. Phase 00 was built by an implementing agent working alone —
appropriate for a throwaway diagnostic instrument. **Every phase from here on is
built by the project owner, learning MAUI as they go, with the agent teaching rather
than implementing — except UI code, which the agent writes directly.** See "How a
phase actually runs" below for what that split means day-to-day, and
`../docs/learning/` for the standing teaching material (MAUI/.NET primer, emulator
setup, glossary, doc links, the monochrome theme).

A phase folder plus the root reference docs it names is everything needed for that
phase; you don't need to read the other phase folders ahead of time. Every phase
below is written as a concrete walkthrough: numbered steps, not just a feature list,
with a real documentation link next to anything genuinely new rather than "go read
the docs."

---

## How a phase actually runs (the collaboration model)

**Two different rules, by kind of code:**

### Logic — BLE, parsers, services, ViewModels, state machines: you write it, the agent teaches

For each step in a phase's task list:

1. **Concept first.** The agent explains what you're about to build and why — the
   pattern, the relevant part of `docs/learning/`, a link to the real Microsoft/
   vendor doc (see `docs/learning/03-Doc-Links.md`), anything genuinely new. Ask
   questions here; this is the part that's supposed to be slow.
2. **Spec, not code.** The agent describes what the file/class should do and what
   "done" looks like — files touched, an interface to satisfy, a short illustrative
   snippet showing the *shape* of the solution (never the whole thing), an
   acceptance bullet — but never hands over the actual implementation.
3. **You write it.** In your own editor, at your own pace.
4. **Review.** The agent reads the real file you wrote, flags bugs, explains better
   patterns where they exist, and answers "why is this wrong" rather than just
   fixing it silently.
5. **Verify together** — run the tests, run the app — before moving to the next
   task.

### UI — XAML pages, styles, widgets, converters: the agent writes it

Every phase that produces a screen or a visual widget includes the **full XAML/C#
code** for it, built on the shared monochrome theme
(`docs/learning/04-Monochrome-Theme.md`, already implemented in
`src/MyHi.Companion/Resources/Styles/`). You paste it in, wire the bindings to
match your ViewModel's actual property names, and build — you are not expected to
design layouts or pick colors. The agent still explains what the code does and why
it's structured that way (so the *concepts* — data templates, grid layouts,
`AppThemeBinding` — still transfer), but you don't have to produce the XAML
yourself. This is a deliberate exception to "you write it": UI layout is not what
this project is trying to teach; BLE, protocol parsing, state machines, and data
modelling are.

New vocabulary gets added to `docs/learning/02-Glossary.md` as it comes up, not
dumped all at once.

## Division of labour — not optional

| Actor | Can do | Cannot do |
|-------|--------|-----------|
| Agent | Explain concepts, design the shape of a logic task, **write UI/XAML code directly**, review code, write docs, run tests you ask it to run | Write your *logic* code for you (BLE, parsers, services, state machines), connect Bluetooth, observe the treadmill |
| Project owner | Write the logic code, wire up and adjust the agent's UI code, run the app on the phone, walk on the belt, capture logs, report observations | — |

---

## Phase order

| # | Phase | Hardware | Size | Gate it opens |
|---|-------|----------|------|---------------|
| [00](phase-00-probe-app/) | **Probe App** | **Yes** | **L** | Everything. Nothing real is known until this runs. |
| [01a](phase-01-protocol-decode/) | Protocol Decode & Fixtures | No | M | **Blocked** — needs Probe Part C (matched pairs) + C7 (counter semantics), not yet done |
| [01b](phase-01-protocol-decode/) | `ITreadmillService` skeleton + `FakeTreadmillService` | No | S | **Not blocked — start here.** Unblocks 03, 04, 06 |
| [02](phase-02-connection-hardening/) | Connection Hardening | **Yes** | M | Reliable link for every later phase |
| [03](phase-03-live-dashboard/) | Live Dashboard + contribution graph | No | M | First real feature |
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

No phase begins until the previous phase's acceptance criteria are met, with two
exceptions:

- **Phase 01b** (`ITreadmillService` skeleton + `FakeTreadmillService`) needs no
  probe data at all — the interface already exists at `../ITreadmillService.cs`. It
  is available to start immediately.
- **Phase 01a** (the actual parsers) is still blocked: Probe Part C (four-plus
  matched console-vs-hex pairs) and C7 (counter reset semantics) haven't been done
  yet. See `phase-00-probe-app/HUMAN-RUNBOOK.md`.

Phase 03 depends only on 01b, not 01a — it builds against `FakeTreadmillService`,
same as before.

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
| `../02-Technology-Stack.md` | Full decision record for every dependency — problem/why/alternatives/long-term. Flags `LiveCharts2`'s pre-1.0 status as an open risk before Phase 10 |
| `../05-FTMS-Protocol.md` | Byte-level spec. Source of truth for every parser |
| `../05a-FTMS-Probe-Procedure.md` | `[HUMAN]` procedure the Phase 00 app automates |
| `../14-Database.md` | Schema, PRAGMAs, write strategy, query patterns |
| `../ITreadmillService.cs` | The seam. Phases 03–11 build against this, not against BLE. Lives at the repo root until Phase 01b moves it into `src/MyHi.Companion.Core/Treadmill/` |
| `../DEVICE.md` | Measured facts only. Never write a guess here |
| `../ASSUMPTIONS.md` | Every guess, with the phase it blocks |
| `../captures/` | Raw capture files produced by the Phase 00 app |
| `../docs/learning/` | MAUI/.NET primer, emulator setup, glossary, doc links, monochrome theme guide — read these, not just this phase list |
| `../docs/learning/00a-CSharp-Essentials.md` | Start here if C# itself (not just MAUI) is new — records, nullable types, async/await, events, LINQ, explained against real code from this repo |
| `../docs/learning/03-Doc-Links.md` | Verified external documentation URLs, grouped by topic — every phase's "Reference docs" links come from here |
| `../docs/learning/04-Monochrome-Theme.md` | The shared theme every phase's UI code is built on — already implemented in `src/MyHi.Companion/Resources/Styles/` |

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
