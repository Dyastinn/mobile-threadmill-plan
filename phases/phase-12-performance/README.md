# Phase 12 — Performance

> Measure, then fix what is actually slow. Not the other way round.

**Hardware:** required · **Size:** S · **Blocked by:** Phase 11

---

## Measure

| Metric | Target |
|--------|--------|
| Memory over a 1-hour workout | under 150 MB, and **flat** |
| CPU during active recording | note it |
| Battery drain per hour of workout | note it |
| BLE notification → UI latency | note it |
| Sample flush duration (60 rows) | < 50 ms |
| Full workout sample insert (720 rows) | < 1 s |
| Workout list page (50 rows) at 5,000 workouts | < 100 ms |
| Weekly aggregate at 5,000 workouts | < 200 ms |
| Single workout chart load (2-hour workout) | < 200 ms |

**The memory slope matters more than the absolute number.** A rising graph means the
sample buffer or an event subscription is leaking, and 140 MB rising is worse than
160 MB flat.

## Tests

- Seed a synthetic 5,000-workout database and measure. Do not assume.
- `[HUMAN]` One-hour workout with the profiler attached
- Every screen opens in under 500 ms on the seeded database

## Acceptance

- [ ] Flat memory over one hour
- [ ] Statistics queries under 200 ms at 5,000 workouts
