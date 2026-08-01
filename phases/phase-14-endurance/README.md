# Phase 14 — Endurance Testing

> All `[HUMAN]`. This is the phase that decides whether the app is actually better than
> FitShow. Nothing here can be simulated.
>
> **Budget a full week**, not a sitting — tests 2, 8, and 9 span hours or days by
> design. This file is a lab procedure: follow it like `phase-00-probe-app/HUMAN-RUNBOOK.md`
> — exact taps, exact things to watch, exact pass/fail. Don't improvise the steps; if a
> step seems pointless, do it anyway and note why it felt that way.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 13

---

## Before starting

**All four HyperOS boxes below, checked and recorded in `../../DEVICE.md`.** These are
Xiaomi-specific controls beyond stock Android battery optimization — skipping any one
of them means tests 1, 2, and 4–7 can fail for reasons that have nothing to do with
your code, and the resulting debugging session is wasted time chasing a phantom bug.

- [ ] **Autostart** enabled for the app — Settings → Apps → Permissions → Autostart (a
      HyperOS-only menu, not part of stock Android)
- [ ] App's **Battery saver** set to **"No restrictions"** — Settings → Apps →
      MyHi Companion → Battery saver (HyperOS's own per-app setting, distinct from the
      Android battery-optimization toggle below)
- [ ] App **locked in Recents** — open Recents, long-press the app card, tap the
      padlock icon. This resists HyperOS's memory reclaim under pressure (e.g. after
      switching to YouTube in test 3)
- [ ] Android **battery optimization** disabled for the app — Settings → Apps →
      MyHi Companion → Battery usage → Unrestricted

Also before the first test:

- [ ] Phone fully charged — a mid-test low-battery power-save mode is a confound you
      don't want to have to untangle from a real bug
- [ ] Treadmill powered on, safety key clipped on
- [ ] Enough free storage for capture logs — tests 1–6 each produce one, test 2's is
      the largest (120 minutes)
- [ ] You know where the app's Capture Recorder screen lives (same screen used in
      Phase 00) and how to start/stop/export a session from it

**Copy each test's capture log off the phone right after that test**, not at the end
of the week. If the app crashes during test 6, you don't want to have lost test 1–5's
logs along with it.

---

## The nine — quick reference

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

The rest of this file turns each row into an actual procedure. The pass conditions
above are the only ones that count — nothing below invents a new number.

---

## Runbook

### Common setup (tests 1–6)

1. Open the app, go to the Capture Recorder screen, tap **Start Capture**.
2. Connect to the treadmill and let the connection reach `Ready`.
3. Start the workout.
4. Run the test-specific steps below.
5. At the end of the test, stop the capture and export/copy the log file off the
   phone before starting the next test.

### Test 1 — 60-minute walk, screen locked

1. After Common Setup, start walking at any comfortable, steady pace.
2. Lock the screen (power button) within the first minute and leave it locked for the
   remaining ~59 minutes. Don't peek — checking the screen mid-test can itself wake
   the radio and mask a bug that would show up if you'd left it alone.
3. Keep walking for the full 60 minutes. Set a separate timer (a second device, or a
   kitchen timer) so you aren't unlocking the phone to check elapsed time.
4. At 60 minutes, unlock the phone and check the app's connection indicator
   immediately — before touching anything else.
5. Stop the workout and the capture per Common Setup step 5.

**Check, from the exported capture log:**
- Scan every consecutive pair of `0x2ACD` sample timestamps. The largest gap between
  any two must be **≤ 5 seconds**.
- No disconnect/reconnect event appears anywhere in the log.

**Pass:** both of the above hold for the full 60 minutes. **Fail:** any gap > 5 s, or
any disconnect event, anywhere in the log — note the elapsed time it happened at, that
timestamp is the first thing worth looking at when debugging.

### Test 2 — 120-minute walk, flat memory

Same procedure as Test 1, extended to 120 minutes, with a memory measurement added.
This test is also the practical proof that `connectedDevice` foreground services don't
hit a 6-hour cutoff on this device — 2 hours is comfortably inside that, so a clean
run here is reassurance, not a new claim.

1. Before starting the capture, connect the phone to a computer via USB with ADB
   available, and confirm `adb devices` shows the phone.
2. Start Common Setup, then immediately record a baseline:
   ```
   adb shell dumpsys meminfo <your.package.name>
   ```
   Note the `TOTAL PSS` value.
3. Walk for 120 minutes, same locked-screen discipline as Test 1.
4. At the 60-minute mark, run the same `dumpsys meminfo` command again and note the
   value. At 120 minutes, run it a third time.
5. Stop the workout and capture per Common Setup step 5.

**Check:**
- Same gap/disconnect check as Test 1, over the full 120 minutes.
- Compare the three `TOTAL PSS` readings. Expect normal fluctuation between readings
  (garbage collection, background system noise) but **no sustained upward trend** —
  the 120-minute reading should not simply be the largest of the three by a wide,
  consistent margin. A flat or sawtooth pattern is a pass; a steady climb across all
  three readings is a leak.

**Pass:** no gap > 5 s, no disconnect, memory pattern flat/sawtooth, not climbing.
**Fail:** either the connectivity criteria from Test 1 fail, or memory climbs steadily
across all three readings.

### Test 3 — Split screen, switching apps

1. Common Setup, then start walking.
2. Open Recents, drag the app's card to the top of the screen to enter split-screen
   (or long-press the card and choose the split-screen option — exact gesture varies
   by HyperOS version, use whichever the phone offers).
3. In the other pane, open YouTube. Watch something for a few minutes.
4. Switch the other pane to Messenger. Send/receive a message or scroll for a few
   minutes.
5. Switch the other pane to Chrome. Browse for a few minutes.
6. Repeat steps 3–5 at least twice more, for a total split-screen duration comparable
   to a normal workout (roughly the 20–30 minute range you'd walk anyway).
7. Exit split screen, stop the workout and capture.

**Check:** capture log shows no disconnect/reconnect event at any app-switch boundary
— cross-reference the timestamps of your switches (note them as you go, even roughly)
against the log.

**Pass:** zero disconnects across all switches. **Fail:** any disconnect — note which
switch (which app, which direction) preceded it.

### Test 4 — Lock/unlock 20×

1. Common Setup, then start walking.
2. Lock the screen (power button). Wait roughly 10 seconds.
3. Unlock the screen (power button + swipe/PIN as configured). Wait roughly 10
   seconds.
4. Repeat steps 2–3 until you've completed **20 full lock/unlock cycles**. Tally them
   as you go — a paper tally or a notes app on a second device — 20 is easy to lose
   count of by cycle 14.
5. Stop the workout and capture.

**Check:**
- Capture log: no disconnect/reconnect event across any of the 20 cycles.
- Capture log: no two samples share the same elapsed-time/sequence value (a duplicate
  sample is the specific failure mode this test is designed to catch — the app
  re-delivering the same reading after a lock/unlock).

**Pass:** zero disconnects, zero duplicate samples, across all 20 cycles. **Fail:**
either condition breaks — note which cycle number.

### Test 5 — Disable Bluetooth mid-workout, re-enable

The reconnect grace window is **60 seconds** (Phase 04's connection-loss policy: a
loss during an active workout starts a 60-second grace timer — restored inside it,
the workout resumes; not restored, it finishes and saves what it has). This test
exercises the "restored inside the window" path.

1. Common Setup, then start walking for a couple of minutes to get a few normal
   samples recorded.
2. Open Quick Settings and turn phone **Bluetooth off**. Note the time.
3. Wait roughly 15–20 seconds — comfortably inside the 60-second window — then turn
   Bluetooth back **on**.
4. Watch the app: it should reconnect and the workout should resume (not restart) —
   confirm the elapsed time / distance continues from where it was, not from zero.
5. Keep walking a couple more minutes, then stop the workout and capture.

**Check:** the capture log (or the app's own connection-state history if it logs
transitions) shows a disconnect event followed by a reconnect within 60 seconds, and
the workout's distance/elapsed-time fields are continuous across the gap — no reset to
zero, no gap in the underlying record beyond the actual outage.

**Pass:** resumes within the window, values continuous. **Fail:** the workout ends
instead of resuming, or resumes but with reset/corrupted values.

### Test 6 — Power treadmill off mid-workout, back on

1. Common Setup, then start walking for a couple of minutes.
2. Power the treadmill off at its own switch (not the safety key — this is testing
   the BLE link dropping because the peripheral disappeared, not an emergency stop).
   Note the time.
3. Wait roughly 15–20 seconds, then power the treadmill back on.
4. Watch the app: same as Test 5, either the workout resumes cleanly (if within the
   60-second grace window and the app treats this the same as a Bluetooth toggle) or
   it finishes cleanly with the data collected so far saved — either outcome is a
   pass, per the stated policy. What is **not** acceptable is a stuck/ambiguous state
   (spinner forever, no clear connected/disconnected indication) or lost data from
   before the power-cycle.
5. Stop the workout and capture (if it hasn't already auto-finished).

**Check:** capture log for the disconnect/reconnect (or disconnect/finish) event, and
that every sample recorded before the power-cycle is still present in the saved
workout afterward.

**Pass:** clean finish or clean resume, no lost pre-outage data, no stuck state.
**Fail:** stuck state, or any sample from before the outage missing afterward.

### Test 7 — Force-close app mid-workout, relaunch

1. Common Setup, then start walking for a few minutes — long enough to have a
   handful of samples recorded (say, 3–5 minutes).
2. Note the exact wall-clock time, then **force-close** the app: open Recents and
   swipe the app's card away (a real force-close, not just pressing Home — pressing
   Home only backgrounds it and isn't what this test is checking).
3. Wait a few seconds, then relaunch the app from the home screen/app drawer (not by
   tapping a notification, unless that's the normal relaunch path — use whichever a
   real user would use).
4. Watch what the app does on launch: it should detect the in-progress workout and
   offer to recover it (per the schema's `Status = 0` in-progress row, written at
   workout start specifically so a crash leaves something recoverable).
5. Confirm the recovered workout's data matches what you walked before the
   force-close — compare against your own sense of elapsed time, or against the
   Capture Recorder's log if it was still writing up to the force-close.

**Check:** every sample from before the force-close is present in the recovered
workout; nothing after the force-close is fabricated or duplicated.

**Pass:** workout recovered with exactly the pre-close data intact. **Fail:** the
workout is gone, or it's recovered but missing/duplicating samples.

### Test 8 — Export → uninstall/reinstall → import, checksum match

This is the test that validates the entire backup phase (09 and, if you've done it,
15). Do it only after you have a reasonable amount of real history on the phone —
Test 9's seven days of data is a good moment to run this, so consider sequencing Test
8 right after Test 9 rather than on a near-empty database.

1. Before touching anything: from the app's History or Statistics screen, write down
   a few numbers you can check later without needing a byte-for-byte tool — total
   workout count, total distance, and (if shown) date of the earliest and latest
   workout. This is your human-checkable proxy for "nothing was lost."
2. Export a full backup ZIP via the app's export flow (share sheet). Save the file
   somewhere that survives an uninstall — Drive, email to yourself, or copy to a
   computer over USB. **Do not leave it only in the app's own storage** — that gets
   wiped with the app.
3. Uninstall the app.
4. Reinstall the app (same build, or the current build off the branch you're testing
   — not an old version).
5. On first launch, use the import flow to import the ZIP you saved in step 2.
6. Compare the History/Statistics numbers you wrote down in step 1 against what's
   shown now. They must match exactly — workout count, total distance, earliest/latest
   dates.
7. If the app exposes any explicit integrity check (a manifest count, a computed hash
   shown in a diagnostics screen), record and compare that too — this is the closer
   proxy for the row-count-and-SHA-256 check that Phase 09's automated test performs
   against a temp database; you're doing the human-observable version of the same
   claim.

**Check:** every number from step 1 matches exactly after import. No workout is
missing, duplicated, or has changed values.

**Pass:** exact match on every recorded number. **Fail:** any mismatch — note which
number and by how much, that tells you where to look (a lost workout will short the
count; a corrupted sample set may match the count but not the distance/duration
totals).

### Test 9 — Seven consecutive daily workouts

1. On each of seven **different calendar days**, do a normal walk with the app as you
   otherwise would — no special procedure, this test is about ordinary daily use
   holding up over a week, not a single long session.
2. After each day's workout, before moving on: open the History screen and confirm
   that day's workout appears, with sane values (not zero, not implausibly large).
3. If the app has a Statistics or personal-records screen, check it after each
   workout too — a new PR (fastest pace, longest distance, whatever the app tracks)
   should show up if that day's walk actually earned one, and should **not** show up
   if it didn't.
4. After the seventh day, review the full week in the History list: exactly seven
   entries for the seven days you actually walked, no extra or missing entries, no
   duplicates, and the Statistics screen's aggregate numbers (total distance for the
   week, etc.) match what you'd get by adding up the seven individual workouts by
   hand.

**Check:** seven History entries, correct per-day values, correct PR behavior each
day, correct aggregate totals at the end.

**Pass:** all of the above hold for all seven days. **Fail:** any day's entry is
wrong, missing, or duplicated, or any PR fires incorrectly (or fails to fire when it
should have) — note which day and what specifically was wrong.

---

### Understanding what you're building (read this before the tasks)

**The everyday problem.** Every parser this project has is already proven
correct — `TreadmillDataParser` and its siblings from Phase 01a pass against
real captured bytes, matching the console's own numbers within rounding. But
"the parser reads bytes correctly" and "the app survives someone's actual
workout" are different claims, and only one of them can be checked by running
`dotnet test`. A flight simulator can prove a pilot knows exactly which switch
does what and can fly a textbook approach — but no airline puts a pilot in a
real cockpit with real passengers on the strength of simulator hours alone.
There's a category of failure a simulator structurally cannot produce: real
turbulence, a real instrument glitch, three sustained hours instead of twenty
minutes of scripted scenarios. This phase is the real flight hours. It exists
because the actual risks to this app aren't in the math — they're in things no
unit test can construct: a phone with its screen off for two hours while
HyperOS's Autostart, Battery saver, Recents-lock, and Android battery
optimization settings (see "Before starting" above) all quietly decide whether
this app's Bluetooth connection is worth keeping alive, real BLE radio
interference, and real OS lifecycle events (a force-close, a Bluetooth toggle,
a treadmill power-cycle) landing at an arbitrary moment mid-workout.

**Why this can't be shortened to a five-minute smoke test.** The naive,
cheaper alternative is: run the app for a few minutes, watch it connect and
receive a couple of samples, call it verified — after all, the parsers are
already proven correct, so what's left to prove? The answer is specific to
each of the nine tests, and none of it is padding. Test 2's 120 minutes exists
because a memory leak of a few kilobytes per sample is invisible at five
minutes and only shows up as a trend across three `dumpsys meminfo` readings
taken over two hours — there is no way to observe "steady climb across three
readings" in a five-minute run. Test 4's 20 lock/unlock cycles exists because
the specific failure it's hunting — a duplicate sample re-delivered after a
lock/unlock — is a timing race that may not reproduce on cycle 1 or 2 but
reliably surfaces somewhere across twenty. Tests 5 and 6 exercise the 60-second
connection-loss grace window (Phase 04's policy) under real BLE reconnection
timing that `FakeTreadmillService` was never built to reproduce — it was
explicitly built in Phase 01b to unblock other phases quickly, not to
simulate radio dropout. Test 8's export/uninstall/reinstall/import check is
the one case where "the automated test already covers this" is almost true —
Phase 09 has a real xUnit test against a temp database — except that test
can't observe what happens to a file that has to survive an actual uninstall,
a share-sheet, and cloud storage in between, which is exactly the gap only a
human running the real flow can close. Each of the nine tests maps to one
specific, already-identified risk documented elsewhere in this project (the
connection-loss policy, HyperOS's own battery controls, the `Status = 0`
in-progress-workout recovery row, Phase 09's backup guarantees) — this isn't a
speculative "test everything" pass, it's closing nine named, concrete gaps
that automated tests structurally cannot reach.

No "pattern, named plainly" section here — this phase is a manual test
procedure, not a piece of code, so there's no abstraction or design pattern to
name a tradeoff for.

---

## Recording results

Use the Phase 00 capture recorder for tests 1–6. A failure that is only described in
prose cannot be debugged; a failure with a timestamped byte log can. Copy each test's
capture out to `../../captures/` (or wherever you're archiving them) right after that
test finishes, per "Before starting" above.

For test 8, keep the exported ZIP itself alongside your before/after numbers — if the
checksum-equivalent check fails, the ZIP is the only artifact that lets anyone
reconstruct what actually happened.

## Acceptance

- [ ] All nine pass. **Test 8 is the one that validates the entire backup phase.**
