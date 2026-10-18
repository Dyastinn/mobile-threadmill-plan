# Phase 14 — Endurance Tests

> All done by you, on the treadmill. This is the phase that decides whether the app is
> actually better than FitShow. None of it can be faked.
>
> **Allow a full week.** Tests 8 and 9 take days. Follow the steps exactly, like the
> Phase 00 runbook. If a step seems pointless, do it anyway and note why it felt that way.

**Treadmill needed:** yes · **Size:** M · **Waits for:** Phase 13

---

## Why these tests matter

The parsers already pass against real captured bytes. But "the parser reads bytes
correctly" and "the app survives an actual workout" are different claims, and only the
first can be checked with `npm test`. The real risks are things no test can create: the
screen off for an hour while HyperOS decides whether to keep the app running (and
whether React Native keeps your code running), real Bluetooth interference, and things
like a force-close or a Bluetooth toggle happening at a random moment.

Each test targets one known risk:

- **Test 2** (an hour, with memory readings): a small leak is invisible after five
  minutes and only shows as a trend over an hour.
- **Test 4** (20 lock/unlocks): a duplicate sample after unlocking is a timing problem
  that might not show on the first try but will within twenty.
- **Tests 5 and 6**: the 60-second grace period (Phase 04) with real reconnect timing,
  which the fake treadmill can't reproduce.
- **Test 8**: Phase 09's automated test can't check a file surviving an uninstall, the
  share sheet and cloud storage. Only you can.

---

## Before starting

**All four HyperOS settings done and recorded in `PHASE-00-FINDINGS.md`.** Without them,
tests 1, 2 and 4–7 can fail for reasons that have nothing to do with the code.

- [ ] **Autostart** on: Settings → Apps → Permissions → Autostart (a HyperOS menu)
- [ ] **Battery saver** set to **"No restrictions"**: Settings → Apps → MyHi Companion →
      Battery saver (HyperOS's own setting)
- [ ] App **locked in Recents**: open Recents, long-press the app, tap the padlock
- [ ] Android **battery optimisation** off: Settings → Apps → MyHi Companion → Battery
      usage → Unrestricted

Also, before the first test:

- [ ] Phone fully charged (a low-battery mode in the middle of a test would confuse the
      results)
- [ ] Treadmill on, safety key clipped on
- [ ] Enough free space for capture logs (tests 1–6 each make one)
- [ ] The **release build** installed (`npx expo run:android --variant release`), not the
      development build: it runs at real speed and is what you'll use day to day
- [ ] Phone **unplugged** for every test. The phone doesn't go into deep sleep while
      charging, so a test on a cable can pass and then fail for real. Pair wireless
      debugging first if you want `adb` (tests 2 and 7) or the agent watching the logs
- [ ] You know where the Capture screen is (from Phase 00) and how to start, stop and
      share a session

**Share each test's capture log straight after that test**, not at the end of the week.
If the app crashes during test 6, you don't want to lose tests 1–5 too.

---

## The nine tests

| # | Test | Pass if |
|---|------|---------|
| 1 | 60-minute walk, screen locked | No disconnect, no sample gap over 5 s |
| 2 | 60-minute walk with memory readings | Same, plus memory stays flat |
| 3 | Split screen, switching between YouTube, Messenger and Chrome | No disconnect |
| 4 | Lock and unlock 20 times during a workout | No disconnect, no duplicate samples |
| 5 | Bluetooth off mid-workout, then on | The workout carries on |
| 6 | Treadmill off mid-workout, then on | The workout finishes cleanly or carries on |
| 7 | Force-close the app mid-workout, reopen | The workout is recovered, nothing lost |
| 8 | Export → uninstall → reinstall → import | All history back, **numbers match exactly** |
| 9 | Seven days of workouts in a row | History, statistics and records all correct |

The steps below turn each row into a procedure. Only the pass conditions above count.

---

## Steps

### Setup for tests 1–6

1. Open the app, go to the Capture screen, tap **Start Capture**.
2. Connect to the treadmill and wait for `ready`.
3. Start the workout.
4. Do the test's own steps below.
5. At the end, stop the capture and share the log file before the next test.

### Test 1 — 60 minutes, screen locked

1. After setup, walk at a steady, comfortable pace.
2. Lock the screen within the first minute and leave it locked. Don't peek: unlocking
   can wake things up and hide a problem.
3. Walk the full 60 minutes. Use a separate timer (a kitchen timer or another device) so
   you don't unlock the phone.
4. At 60 minutes, unlock and look at the connection dot straight away, before touching
   anything.
5. Stop the workout and the capture.

**Check, in the capture log:**
- The biggest gap between two `0x2ACD` packets is **5 seconds or less**.
- No disconnect or reconnect anywhere.

**Pass:** both true for the whole 60 minutes. **Fail:** any gap over 5 s, or any
disconnect. Note the time it happened; that's where to start looking.

### Test 2 — 60 minutes, memory stays flat

Test 1 again, with memory readings. Sixty minutes matches your real sessions. If your
sessions get longer, make this test as long as the longest one. (`connectedDevice`
services don't have Android's 6-hour limit, so length alone isn't the risk; memory is.)

1. Before starting, check `adb devices` shows the phone over wireless debugging (still
   unplugged).
2. After setup, take a first reading:
   ```
   adb shell dumpsys meminfo com.myhi.companion
   ```
   Note the `TOTAL PSS` number.
3. Walk 60 minutes, screen locked, like test 1.
4. Take the same reading at 20, 40 and 60 minutes. It doesn't turn the screen on.
5. Stop the workout and the capture.

**Check:**
- The same gap and disconnect check as test 1.
- The four memory readings. Some up and down is normal. A **steady climb** across all
  four is a leak (Phase 12's snapshot comparison finds it).

**Pass:** no gap over 5 s, no disconnect, memory flat or up-and-down. **Fail:** test 1's
checks fail, or memory climbs across all four readings.

### Test 3 — Split screen, switching apps

1. After setup, start walking.
2. Open Recents and put the app in split screen (the gesture depends on the HyperOS
   version; use whichever your phone offers).
3. Open YouTube in the other half and watch for a few minutes.
4. Switch the other half to Messenger. Use it for a few minutes.
5. Switch it to Chrome. Browse for a few minutes.
6. Repeat steps 3–5 at least twice more, about 20–30 minutes in total.
7. Leave split screen, stop the workout and the capture.

**Check:** no disconnect at any switch. Note roughly when you switched, and compare with
the log.

**Pass:** no disconnects. **Fail:** any disconnect. Note which switch came just before
it.

### Test 4 — Lock and unlock 20 times

1. After setup, start walking.
2. Lock the screen. Wait about 10 seconds.
3. Unlock it. Wait about 10 seconds.
4. Repeat until you've done **20 full lock/unlocks**. Keep count on paper; it's easy to
   lose track around 14.
5. Stop the workout and the capture.

**Check, in the log:**
- No disconnect in any of the 20.
- No two samples with the same elapsed time (a repeated sample after unlocking is
  exactly the problem this test looks for).

**Pass:** no disconnects, no duplicates. **Fail:** either. Note which round.

### Test 5 — Bluetooth off, then on

The grace period is **60 seconds** (Phase 04): if the connection comes back within it,
the workout carries on; if not, it finishes and saves. This tests the "comes back in
time" case.

1. After setup, walk a couple of minutes.
2. Turn the phone's **Bluetooth off** from Quick Settings. Note the time.
3. Wait 15–20 seconds, then turn it back **on**.
4. The app should reconnect and the workout should carry on, not start over: time and
   distance continue from where they were.
5. Walk a couple more minutes, then stop the workout and the capture.

**Check:** the log shows a disconnect and then a reconnect within 60 seconds, and time
and distance continue without resetting.

**Pass:** carries on, numbers continue. **Fail:** the workout ends, or carries on with
reset or wrong numbers.

### Test 6 — Treadmill off, then on

1. After setup, walk a couple of minutes.
2. Switch the treadmill off at its power switch (not the safety key: this tests
   Bluetooth disappearing, not an emergency stop). Note the time.
3. Wait 15–20 seconds, then switch it back on.
4. Either result is fine: the workout carries on, or it finishes cleanly with the data
   so far saved. **Not** fine: stuck (spinning forever, unclear whether connected), or
   data from before the switch-off missing.
5. Stop the workout and the capture (if it didn't finish by itself).

**Check:** the log shows the disconnect and the reconnect (or the finish), and every
sample from before the switch-off is in the saved workout.

**Pass:** clean finish or clean carry-on, nothing lost, never stuck. **Fail:** stuck, or
samples from before the switch-off missing.

### Test 7 — Force-close, then reopen

1. After setup, walk 3–5 minutes so there are some samples.
2. Note the time, then **force-close** the app: open Recents and swipe it away (pressing
   Home only hides it, which isn't this test).
3. Wait a few seconds, then open the app again from the home screen.
4. It should notice the unfinished workout and recover it (that's the in-progress row
   from Phase 06).
5. Check the recovered workout matches what you walked before the force-close.

**Check:** every sample from before the force-close is there; nothing after it is made up
or doubled.

**Pass:** recovered with exactly the right data. **Fail:** gone, or missing or doubled
samples.

### Test 8 — Export, uninstall, reinstall, import

This test checks the whole backup feature. Do it when you have real history on the phone:
straight after test 9 is a good time.

1. First, write down numbers you can check later: total workouts, total distance, and the
   dates of the first and last workout (from History or Statistics).
2. Export a backup (share sheet). Save it somewhere that survives an uninstall: Drive,
   email to yourself, or copy it to the PC. **Not just inside the app**, which gets
   deleted.
3. Uninstall the app.
4. Install it again (the same build, or the current one; not an older one).
5. On first launch, import the backup.
6. Compare with step 1. They must match exactly.
7. If the app shows any other check numbers (counts in a diagnostics screen), compare
   those too.

**Check:** every number matches exactly. No workout missing, doubled, or changed.

**Pass:** everything matches. **Fail:** anything differs. Note which number and by how
much: a missing workout lowers the count; damaged samples might keep the count but change
distance or time.

### Test 9 — Seven days in a row

1. On seven **different days**, walk as you normally would. Nothing special; this is
   about normal daily use holding up.
2. After each walk, open History and check today's workout is there, with sensible
   numbers.
3. Check Statistics too. A new record should appear only if that walk actually beat one.
4. After day seven: exactly seven entries for the seven days, none missing or doubled,
   and the week's totals match adding up the seven by hand.

**Pass:** all true for all seven days. **Fail:** any day wrong, missing or doubled, or a
record that appears (or doesn't) when it shouldn't. Note which day and what was wrong.

---

## Recording results

Use the capture screen for tests 1–6. A problem described only in words can't be
debugged; one with a timestamped log can. Copy each test's log to `../../captures/` right
after the test.

For test 8, keep the backup file with your before/after numbers. If they don't match, the
file is the only way to find out what happened.

## Done when

- [ ] All nine pass. **Test 8 is the one that proves backups work.**
