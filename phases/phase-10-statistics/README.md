# Phase 10 — Statistics

**Hardware:** none · **Size:** M · **Blocked by:** Phase 09

---

## Goal

Aggregates and charts. LiveCharts2 (MIT).

## Features

- Daily / weekly / monthly / yearly aggregates
- Personal records: longest distance, longest duration, fastest average
- Cross-workout charts: distance, calories, duration, average speed over time
- **Per-workout charts** — speed and heart rate curves from `WorkoutSample`. This is
  the payoff for storing telemetry and the main thing FitShow does badly.

If V3 said heart rate is marginal or unusable, HR curves are hidden here too — but the
data keeps being recorded, so they can be switched on later without a gap in history.

---

## Implementation requirements

- **Statistics and PRs are always computed from `Workout` / `WorkoutSample`, never
  stored.** Not columns, not tables, not exported. If a query becomes slow, add a cache
  table that is *explicitly rebuildable* and rebuild it after any import — but do not
  start there.
- **Aggregate in SQL**, not in C# over a full table read. Query patterns and the
  local-day-via-stored-offset expression are in `../../14-Database.md`.
- Index `Workout(StartedAtUtc)` — every aggregate filters on it.
- **Downsample sample series for display.** A 2-hour workout is 1,440 points; a 400 px
  chart needs perhaps 200. Downsample in SQL (`WHERE ElapsedSec % $n = 0`), not by
  loading everything and thinning it in C#.
- Gap-marked samples render as breaks in the line, never as interpolated segments.

## Tests

- Hand-calculate weekly totals for a seeded dataset and compare
- Timezone: a workout at 23:30 local buckets into the correct **local** day
- DST boundary week aggregates correctly
- Empty dataset renders without crashing
- 5,000-workout dataset: aggregate queries under 200 ms

## Acceptance

- [ ] Manual calculations match for daily, weekly, monthly
- [ ] No stored derived values anywhere in the schema
