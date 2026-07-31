# Phase 03 — Live Dashboard + Contribution Graph

> See `../README.md` for the collaboration model — you write the code, the agent
> explains concepts up front and reviews after.

**Hardware:** none for development (`FakeTreadmillService`), yes to verify at the end
**Size:** M · **Blocked by:** Phase 01b only (not 01a)

---

## Goal

Build the app's real home/dashboard screen: live treadmill metrics (built entirely
against `ITreadmillService`, developed and tested with `FakeTreadmillService`, real
hardware only to verify at the end) **and** a GitHub-style workout "contribution
graph" at the top of the screen, using a fake data source until Phase 06 provides
real workout history.

This screen replaces Phase 00's `HomePage` as the app's actual front door. Phase 00's
six diagnostic screens don't disappear — they're still useful for debugging — but
they stop being the first thing the app shows. (Exactly how they're relocated, e.g.
into a hidden diagnostics menu, is a small decision to make together when we get
there — not blocking for this phase.)

## Learning goals

- Building the **same seam/fake pattern from Phase 01b**, applied to a second,
  unrelated problem (workout history instead of BLE) — the point is to notice it's a
  repeatable technique, not a one-off trick
- A `CollectionView` with a **grid layout** (`GridItemsLayout`), for laying out many
  small uniform items — different from the single-column lists you've seen so far
  (Scan screen, Notification Log)
- Throttling a high-frequency event stream for UI binding, so updates don't outpace
  what a screen can usefully render
- Deciding what belongs in `Core` vs. the app project — you'll make this call
  yourself this time, using the same test the agent applied in Phase 01b: does this
  code reference MAUI/Android at all?

## Reference docs

- `src/MyHi.Companion.Core/Treadmill/ITreadmillService.cs` (moved here in Phase 01b)
- `../../05-FTMS-Protocol.md` §4 (fields), §4a (heart rate)
- `../phase-00-probe-app/PHASE-00-FINDINGS.md` — notification rate, flags observed,
  V3 heart rate verdict
- `docs/learning/02-Glossary.md` — add `GridItemsLayout` and anything else new here
  as you go

---

## Part 1 — Live metrics

### Your tasks

- Speed, distance, calories, elapsed time, machine status, on a new dashboard page
- Heart rate **only if V3 (Phase 00 findings) said usable**
- Connection indicator
- **Fields the device does not actually send are hidden, not shown as `--`.** A row
  of dashes is a promise the app can't keep.

**Field visibility** comes from the union of observed `0x2ACD` flag bits (Phase 01a's
capability tracker, once it exists), **not** from `0x2ACC` — that bitmask on this
device over-claims (it advertises incline target setting on a machine with no
incline). Log it; never branch on it. Until Phase 01a lands, it's fine to show
whatever fields `FakeTreadmillService` populates.

**Heart rate source**: `0x2A37` from `180D` if usable; the FTMS field only if `180D`
is dead; **removed from the UI entirely if V3 said unusable.** If V3 said marginal,
keep recording it (Phase 06) and hide it on this screen.

### Implementation requirements

- Notification callbacks from `ITreadmillService` are documented as already
  marshalled to the UI thread at the service boundary (see the interface's doc
  comments) — don't marshal again, and don't assume they aren't if you changed that
  in Phase 01b.
- **Throttle UI updates to at most 4 Hz** even if notifications arrive faster. Above
  that, MAUI janks in split screen for no visible benefit. (How you throttle —
  a timer sampling the latest value, a debounce, something else — is your design
  decision; think about what "throttle" actually needs to guarantee before picking
  one.)
- Format at display time only. **Never convert units before storage** — metric
  always, everywhere below the ViewModel.

### Tests

- Fake service: 10-minute stream renders continuously, no frozen UI
- Sparse-field scenario: absent fields are hidden, present ones render
- `[HUMAN]` Walk 5 minutes; every field updates and matches the treadmill console
- `[HUMAN]` Rotate the device mid-workout; no crash, no reset

---

## Part 2 — Contribution graph

### The feature

A GitHub-style grid showing which days had a workout, at the top of the dashboard,
above the connection status. **Simplified from GitHub's original**: no gradient by
count — each day is either lit (≥1 workout) or unlit (none). If you want the
gradient-by-count version later, that's a good follow-up once the simple version
works and is reviewed, not a requirement now.

### The seam (same pattern as `ITreadmillService`/`FakeTreadmillService`)

Real workout history doesn't exist until Phase 06 (Recording & Schema). Rather than
wait, define the shape you need and fake it:

- **Interface**, in `Core` (it's plain data, no MAUI dependency):
  a method that returns daily workout counts over a date range. Design the exact
  signature yourself — think about what Phase 06's real SQLite-backed implementation
  will need to accept and return, and what's easiest for a UI to bind against. (Hint:
  look at how `ITreadmillService` shapes its return types — `TreadmillSample` is a
  `readonly record struct`; is a `record` right here too?)
- **Fake implementation**, also in `Core`: generates a plausible several-months of
  synthetic daily counts (some days with 0, some with 1+) so the widget has something
  real to render and look right immediately.
- **Real implementation** arrives in Phase 06, reading `Workout.StartedAtUtc` grouped
  by local day (there's a query pattern for exactly this in `14-Database.md`) — the
  dashboard's UI code does not change when this swap happens, only a `MauiProgram.cs`
  registration does.

### The widget

- A reusable view bound to a collection of "day + lit/unlit" data.
- Look into `CollectionView`'s `GridItemsLayout` (`Span="7"` for a GitHub-style
  7-rows-per-week layout, or however you decide to orient it) — this is a different
  `ItemsLayout` than the single-column lists you've built so far. Read MAUI's docs on
  `GridItemsLayout` before starting; it's a good excuse to learn a control you
  haven't used yet, rather than something to guess at.
- Roughly the last 3–6 months is a reasonable range to start with — full year of
  GitHub-style history can come later once you're happy with the layout at a smaller
  size.
- Placed at the very top of the new dashboard page, above the connection indicator
  and live metrics from Part 1.

### Review checkpoint

Before wiring Phase 06's real data source in later: agent reviews the
`IWorkoutHistoryProvider` shape (is it something Phase 06 can actually implement
without redesigning it?), the fake's realism, and the widget's binding approach.

---

## Acceptance

- [ ] Smooth updates for 10 minutes with no UI stall (Part 1)
- [ ] `[HUMAN]` Displayed values match the console at three different speeds
- [ ] No field shows a placeholder for data the device never sends
- [ ] Contribution graph renders against `FakeWorkoutHistoryProvider`, lit days
      visibly distinct from unlit ones
- [ ] `IWorkoutHistoryProvider` lives in `Core`, has no MAUI dependency, and is
      registered in `MauiProgram.cs` the same way `ITreadmillService` is
