# Phase 14 — Endurance Testing

> All `[HUMAN]`. This is the phase that decides whether the app is actually better than
> FitShow. Nothing here can be simulated.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 13

---

## Before starting

- [ ] All four HyperOS boxes from Phase 07 verified. **If skipped, these tests fail for
      reasons unrelated to the code** and the debugging is wasted.

## The nine

| # | Test | Pass condition |
|---|------|----------------|
| 1 | 60-minute walk, screen locked | No disconnect, no sample gap > 5 s |
| 2 | 120-minute walk | Same, plus flat memory — also the empirical check that `connectedDevice` has no 6-hour timeout |
| 3 | Split screen, switching YouTube / Messenger / Chrome throughout | No disconnect |
| 4 | Lock/unlock 20× during a workout | No disconnect, no duplicate samples |
| 5 | Disable Bluetooth mid-workout, re-enable | Grace-window resume works |
| 6 | Power treadmill off mid-workout, back on | Workout finishes cleanly or resumes per policy |
| 7 | Force-close app mid-workout, relaunch | Partial workout recovered, no data loss |
| 8 | Export → uninstall/reinstall → import | Full history restored, **checksum match** |
| 9 | Seven consecutive daily workouts | History, stats, and PRs all correct |

## Recording results

Use the Phase 00 capture recorder for tests 1–6. A failure that is only described in
prose cannot be debugged; a failure with a timestamped byte log can.

## Acceptance

- [ ] All nine pass. **Test 8 is the one that validates the entire backup phase.**
