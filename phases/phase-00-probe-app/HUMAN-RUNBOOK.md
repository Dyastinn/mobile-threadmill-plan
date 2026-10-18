# Phase 00 — Treadmill runbook

> For you, with the app installed. **Parts A–G take 60–75 minutes.** Part H is a
> second session of about 90 minutes (it includes a one-hour walk). Do it properly
> once rather than three times in a rush.
>
> The agent has no Bluetooth and no treadmill, so it can't know any of this until you
> run it and paste the results back. With the phone on wireless `adb`, it *can* watch
> the logs and pull capture files while you walk.

---

## Before you start

- [ ] The app is installed and opens (`npx expo run:android --device`). For Part H,
      the release build instead.
- [ ] For Parts A–G with the development build: `npx expo start` running on the PC,
      and the phone on the same Wi-Fi. (The release build doesn't need the PC.)
- [ ] Treadmill on, safety key clipped on.
- [ ] **nRF Connect** installed (free, by Nordic) to double-check readings. If our app
      and nRF Connect disagree, **nRF Connect is right.**
- [ ] Somewhere to paste text.

**Safety.** Parts D and E move the belt while you're holding a phone. Stand on the side
rails, not the belt, when testing remote commands. If the belt does anything
unexpected, **pull the safety key**. Don't try to fix it from the phone.

**Share the capture file after each part**, not just at the end. If the app crashes in
Part E, you don't want to lose Part A.

---

## Quick wins (do these first, ~10 minutes)

No walking, no belt, no risk. These five unblock all of Phase 01's desk work. If the
full session has to wait, do at least these.

| # | Step | Screen | Why |
|---|------|--------|-----|
| 1 | Raw hex of `0x2ACC` | Read dump | Test data for the feature decoder |
| 2 | Raw hex of `0x2AD4` | Read dump | Expected `64 00 40 06 0A 00`. Confirm or disprove |
| 3 | MAC address and address type | Scan (MAC), **nRF Connect** (address type; our app can't see it) | If the type is **random**, remembering the treadmill by MAC won't work |
| 4 | Is `0x1826` in the advertisement? | Scan (advert bytes) | Decides how the real app finds the treadmill |
| 5 | Packet size (MTU) | Service list | Whether packets can arrive split in two |

About item 2: if the bytes aren't `64 00 40 06 0A 00`, the documented decoding is
wrong, and **everything built on it would be wrong too**. Two minutes that protect the
whole project.

---

## Part A: Read everything · 5 min · belt stopped

Connect, wait for the service list, then tap **Dump all** on the Read dump screen.

- [ ] `0x2ACC` hex recorded
- [ ] `0x2AD4` hex recorded
- [ ] `0x2AD3` hex recorded (expect `Idle`, which is `0x01`)
- [ ] `180A` text recorded (firmware and model)
- [ ] The service and characteristic list matches `PHASE-00-FINDINGS.md`

Tap **Confirm** on each row you've checked against nRF Connect.

---

## Part B: Idle data · 5 min · belt stopped

Notification log screen. Turn on `0x2ACD` and `0x2ADA`. **Don't walk yet.** Let it run
two minutes.

- [ ] Does `0x2ACD` send data while stopped? yes / no
- [ ] Rate (from the on-screen counter): ______ per second
- [ ] Flags tracker: how many different values? ______ (more than 1 while idle means
      the packet layout changes; note it)
- [ ] Packet length: ______ bytes
- [ ] Anything on `0x2ADA` while idle? codes: ______

About 1 per second is what the spec suggests. If it's clearly faster, say so: the
dashboard will need to slow its updates.

---

## Part C: Walking · 15 min · **the most important part**

**Use the treadmill's own buttons only.** Don't touch the app's console yet.

1. Start at the lowest speed. Walk 2 minutes.
2. Go up one step. Walk 1 minute. Repeat up to a comfortable top speed, then come back
   down the same way.
3. **At each speed**, use the console-value form to enter what the treadmill's own
   display shows right then. The app pairs it with the current packet.

**At least four of these pairs, at different speeds.** They're the only way to prove
the decoder is right.

- [ ] Four or more console-vs-hex pairs captured
- [ ] Did the flags value change while walking? no / yes → values: ______
- [ ] Heart rate: does a believable value ever appear? value: ______ bpm
- [ ] Stop with the treadmill's button → `0x2ADA` code sent: ______

### C7: do the counters reset? (critical)

After stopping, note distance, calories and time. Then start a **new** session with the
treadmill's buttons and check the same values straight away.

```
Distance   end of session 1: ______ m     start of session 2: ______ m
Time       end of session 1: ______ s     start of session 2: ______ s
Calories   end of session 1: ______ kcal  start of session 2: ______ kcal

Then turn the treadmill off and on, and reconnect:
Distance after power cycle: ______ m

Verdict:
[ ] Per session — counters go back to 0
[ ] Cumulative  — counters keep counting
[ ] Mixed       — which does what: ______________________________
```

**This one answer decides how Phase 06 saves workouts.** Per session: save the values
as they are. Cumulative: subtract the value from the start of the workout, and handle
the counter jumping back mid-workout. Getting it wrong makes every saved workout wrong.
Don't skip it, and don't guess.

---

## Part D: Speed control · 15 min · **decides whether Phase 05 happens**

Control console screen. **Stand on the side rails.** The belt may start.

**D1.** Check replies are on, then tap **Request Control** (`00`).

```
Reply received? yes / no      Raw reply: ____________   Result: 0x____
```

If this fails, write down the result and **stop Part D**. Remote speed control isn't
available and Phase 05 is skipped. That's a real finding, not a failure; the app is
still worth having without it.

**D2.** Set Target Speed while stopped, at your minimum (2.0 km/h → `02 C8 00`).

```
Sent: ____________  Reply: ____________  Result: 0x____
Belt moved? yes/no      Treadmill display changed? yes/no
```

**D3.** Start (`07`).  → `Reply: ______  Belt started? yes/no`

**D4.** Set a higher speed while running.

```
Sent: ____________  Reply: ____________  Result: 0x____
Belt speed changed? yes/no    Time from send to change: ______ s
0x2ADA event? code 0x____
```

**D5.** Too fast: send a speed above 16.0 km/h with the **free hex box**.

```
Sent: ____________  Reply: ____________  Result: 0x____   (expect 0x03)
```

**D6.** Pause (`08 02`), then Stop (`08 01`).

```
Pause reply: ______  Belt paused? yes/no
Stop reply:  ______  Belt stopped? yes/no
```

**D7.** Leave it connected and idle for 5 minutes, then Set Target Speed again.

```
Result: 0x____  (0x05 = permission expired)   Expired after: ______ min
```

**D8.** Disconnect, reconnect, and Set Target Speed straight away **without** Request
Control.

```
Result: 0x____  (expect 0x05 — control has to be requested again)
```

**Tap Confirm with a note on every command that did the right physical thing.** Those
notes turn a log into a spec.

---

## Part E: Connection problems · 10 min

| | Test | Record |
|---|---|---|
| E1 | Start a session, walk ~15 m away or into another room | Time until the app notices: ______ s · Belt keeps running? |
| E2 | Walk back | Reconnect works? Time: ______ s · Counters continue or reset? |
| E3 | Turn phone Bluetooth off and on | Recovers? Time: ______ s · Error codes in the log: ______ |
| E4 | Turn the treadmill off and on | Reconnects? Counters reset or kept? |
| E5 | Screen off for 5 min during a session, **keep-alive OFF** | Still alive? Longest packet gap: ______ s · Longest heartbeat gap: ______ s |

Reconnecting is manual in this phase; Phase 02 makes it automatic. **A big heartbeat
gap in E5 is expected.** It's the "before" picture that Part H compares against.

---

## Part F: Advertisement and pairing

- [ ] Did Android ever ask to pair? (Expected: no)
- [ ] Is `0x1826` in the advertised services? yes / no
- [ ] Advertised name: ______   MAC: ______   Address type (from nRF Connect's device
      details): public / random

If `0x1826` isn't advertised, the real app finds the treadmill by its `FS-` name
instead, **not** by scanning everything (slower, and drains the battery). If the
address is random, remembering the treadmill by MAC won't work, and the database's
unique MAC index (Phase 06) needs changing.

---

## Part G: Heart rate · 10 min

Turn on `0x2A37` (service `180D`).

**G1.** Hands off the grips. → `Sends data? yes/no · Flags: 0x____ · Value: ______ bpm`

**G2.** Hold both grips firmly for 60 seconds. Meanwhile, count your own pulse at the
wrist for 15 seconds and multiply by 4.

```
Time from gripping to a believable reading: ______ s
0x2A37 after 60 s: ______ bpm    Counted: ______ bpm    Difference: ______
Steady, or jumps by ______ bpm?
```

**G3.** At the same moment, does the heart rate inside `0x2ACD` agree? yes / no / not there

**G4.** Let go. → drops to 0 / keeps the last value / stops sending

**G5: the decision.** Be honest: this decides whether heart rate is in the app at all.

```
[ ] Usable    — within ~10 bpm, steady, knows when you let go
                → show it on the dashboard and charts
[ ] Marginal  — works, but noisy or slow
                → save it, don't show it, look again later
[ ] Unusable  — wrong, all over the place, or never appears
                → remove heart rate from the app
```

A number that's wrong half the time is worse than no number: it ruins the average and
maximum of every saved workout. **Cutting it is a perfectly good outcome.**

---

## Part H: Screen off · ~90 min · **decides where Bluetooth code lives**

A separate session. This tests the one big risk of using Expo: does the app keep
getting Bluetooth data *and* keep running its timers with the screen locked, on
HyperOS, for a full workout? Task 0.12 explains the keep-alive.

**Set up all of this first:**

- [ ] **Release build** installed (`npx expo run:android --variant release --device`),
      not the development build. It doesn't depend on the PC.
- [ ] **Phone unplugged.** Android doesn't go into its deep sleep mode while charging,
      so a test on a cable can pass and then fail for real. Pair wireless debugging
      first if you want the agent watching the logs.
- [ ] All four HyperOS settings done (see `PHASE-00-FINDINGS.md`): Autostart on,
      Battery saver "No restrictions", locked in Recents, battery optimisation off.
      Without them, a failure tells us nothing about the code.
- [ ] Connected, `0x2ACD` on in the Notification log, gap meter reset.

**H1. Keep-alive OFF, 5 min.** (Skip if E5 already did this.) Lock the screen for 5
minutes. Belt stopped is fine.

```
Longest packet gap: ______ s     Longest heartbeat gap: ______ s
```

The heartbeat gap should be about the whole 5 minutes. That's the problem the
keep-alive fixes.

**H2. Keep-alive ON, screen locked, 15 min walking.** Turn on the Keep-alive switch (a
"Keep-alive test" notification appears), start walking, lock the screen, and don't
look at it for 15 minutes.

```
Notification there the whole time? yes / no
Longest packet gap: ______ s     Longest heartbeat gap: ______ s     Disconnects: ______
```

**H3. Keep-alive ON, split screen with YouTube, 15 min.** Unlock, put the app in split
screen with a YouTube video playing in the other half, keep walking. YouTube uses a
lot of memory, which is what makes HyperOS close background apps.

```
Longest packet gap: ______ s     Longest heartbeat gap: ______ s     Disconnects: ______
App still running afterwards (not restarted)? yes / no
```

**H4. Keep-alive ON, screen locked, 60 min walking.** Your normal session.

```
Longest packet gap: ______ s     Longest heartbeat gap: ______ s     Disconnects: ______
Battery %, start: ______   end: ______
```

**H5: the decision.**

```
[ ] Holds        — every run: packet gap ≤ 5 s, heartbeat gap ≤ 10 s, no disconnects
                   → Bluetooth stays in TypeScript; Phase 07 finishes the keep-alive
[ ] Partly holds — packets keep coming but heartbeats stall (or the other way round)
                   → write down which; Phase 07 starts by looking at it with the agent
[ ] Fails        — gaps over a minute, disconnects, or the app gets closed
                   → Bluetooth and recording move into Kotlin (Phase 07's fallback)
```

Share the capture file straight after H4, before anything else.

---

## After the session

1. Export the checklist and paste it into `PHASE-00-FINDINGS.md`. **Measured values
   only.** Anything still unsure stays blank, marked with the phase it blocks.
2. Share the capture files and commit them to `../../captures/`.
3. Share the app's log file.
4. Hand back to the agent with: *"Update Phase 01's FTMS reference with these
   measurements, build test data from the captured hex, and list what's still unknown
   in `PHASE-00-FINDINGS.md`."*

## Done when

- [ ] Every question above has an answer in `PHASE-00-FINDINGS.md`
- [ ] Raw hex for `0x2ACC` and `0x2AD4`
- [ ] Four or more console-vs-hex pairs at different speeds
- [ ] **C7 counters answered clearly**
- [ ] **Part D control verdict clear**: Phase 05 happens or it doesn't
- [ ] **G5 heart rate decided**, and "cut it" counts as an answer
- [ ] **H5 screen-off decided**, from runs done unplugged on the release build
