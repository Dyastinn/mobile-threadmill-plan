# Phase 14 — Endurance Testing (Flutter track)

> All `[HUMAN]`. This is the phase that decides whether the app is actually
> better than FitShow. Nothing here can be simulated.
>
> **Budget a full week**, not a sitting. This file is a lab procedure: exact
> taps, exact things to watch, exact pass/fail — same discipline as
> `../../../phases/phase-00-probe-app/HUMAN-RUNBOOK.md`.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 13

## The concept

Every one of the nine tests below is framework-independent — they exercise
real BLE radio behaviour, real Android OS lifecycle events, and real HyperOS
battery policy, none of which care whether the app above them is written in
Dart or C#. This file is nearly identical to the original track's for that
reason; the only differences are the specific tools used to observe memory
(Flutter DevTools instead of `dotnet-gcdump`) and where the capture files
land. Same discipline as the original: unit tests already proved the
parsers correct; this phase proves the *app* survives someone's actual
workout, which no unit test can construct.

## Before starting

Identical HyperOS checklist to the original track — these are phone-level
settings, unaffected by the app's framework:

- [ ] **Autostart** enabled for the app
- [ ] App's **Battery saver** set to **"No restrictions"**
- [ ] App **locked in Recents**
- [ ] Android **battery optimization** disabled for the app

Also: phone fully charged, treadmill powered on with safety key clipped on,
enough free storage for capture logs, and you know where the app's Capture
Recorder screen lives (Phase 00) and how to start/stop/export a session.

**Copy each test's capture log off the phone right after that test**, not at
the end of the week.

## The nine — quick reference

| # | Test | Pass condition |
|---|------|----------------|
| 1 | 60-minute walk, screen locked | No disconnect, no sample gap > 5s |
| 2 | 120-minute walk | Same, plus flat memory — also empirically confirms `connectedDevice` has no 6-hour timeout |
| 3 | Split screen, switching YouTube / Messenger / Chrome | No disconnect |
| 4 | Lock/unlock 20× during a workout | No disconnect, no duplicate samples |
| 5 | Disable Bluetooth mid-workout, re-enable | Grace-window resume works (Phase 04's 60s window) |
| 6 | Power treadmill off mid-workout, back on | Workout finishes cleanly or resumes per policy |
| 7 | Force-close app mid-workout, relaunch | Partial workout recovered, no data loss |
| 8 | Export → uninstall/reinstall → import | Full history restored, values match exactly |
| 9 | Seven consecutive daily workouts | History, stats, and PRs all correct |

## Runbook

The step-by-step procedure for each test is identical to the original
track's — see
[`../../../phases/phase-14-endurance/README.md`](../../../phases/phase-14-endurance/README.md)
for the full runbook (common setup, exact steps, exact checks, exact
pass/fail criteria for all nine tests). Follow it as written; nothing about
the procedure itself changes with the framework. Two tool-specific notes for
this track:

**Test 2 (120-minute walk, memory):** instead of `dotnet-gcdump`, use
Flutter DevTools' Memory view (`flutter run --profile`, same procedure as
Phase 12 step 3) for the managed-Dart-heap reading, and Android Studio's
Profiler for the native/JNI cross-check, same as Phase 12. Record the same
three-readings-over-two-hours pattern (baseline, 60 min, 120 min) and the
same "flat/sawtooth is a pass, steady climb is a leak" verdict.

**Test 8 (export/uninstall/reinstall/import):** the ZIP format is this
track's own (`samples.jsonl`, not `samples.json` — see
[`../phase-09-backup-minimal/README.md`](../phase-09-backup-minimal/README.md)),
but the procedure and pass condition are identical: write down workout
count, total distance, and earliest/latest dates before exporting; they
must match exactly after reinstall and import.

## Recording results

Use the Phase 00 capture recorder for tests 1–6, same JSONL format shared
with the original track. Copy each test's capture out to `../../../captures/`
right after that test finishes. For test 8, keep the exported ZIP itself
alongside your before/after numbers.

## Acceptance

- [ ] All nine pass. **Test 8 is the one that validates the entire backup phase.**
