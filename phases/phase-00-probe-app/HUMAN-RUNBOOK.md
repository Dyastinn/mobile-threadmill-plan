# Phase 00 — `[HUMAN]` runbook

> For the operator, with the app installed. **Budget 60–75 minutes for Parts A–G**,
> plus a second session of about 90 minutes for Part H (the screen-off test, which
> includes a one-hour walk). Do it properly once rather than three times badly.
>
> The implementing agent has no Bluetooth radio and no treadmill. Everything below is
> unknowable to it until you run this and paste the results back. With the phone on
> wireless adb it *can* watch `adb logcat` and pull capture files while you walk.

---

## Before you start

- [ ] Development build installed and launching (`npx expo run:android --device`),
      **or** the release APK, for Part H
- [ ] For Parts A–G with the dev build: `npx expo start` running on the PC, and the
      phone on the same Wi-Fi so the app can load its JavaScript. (The release APK
      carries its JavaScript inside and needs no PC.)
- [ ] Treadmill powered on, safety key clipped on
- [ ] **nRF Connect** installed (Nordic Semiconductor, free) as an independent
      cross-check. If the app disagrees with nRF Connect, **nRF Connect is right.**
- [ ] Somewhere to paste text

**Safety.** Parts D and E move the belt while you hold a phone. Stand on the side
rails, not the belt, when starting a remote-control test. If the belt does something
unexpected, **pull the safety key**. Do not try to fix it from the phone.

**Copy the capture after each part**, not at the very end. If the app crashes at Part
E you do not want to lose Part A.

---

## Ten-minute quick wins (do these first)

No walking, no belt, no risk. These five unblock the entire Phase 01 desk work, so if
the full session has to wait, do at least these.

| # | Step | Screen | Answers |
|---|------|--------|---------|
| 1 | Raw hex of `0x2ACC` | Read Dump | Parser fixture + verifies the feature decode |
| 2 | Raw hex of `0x2AD4` | Read Dump | Predicted `64 00 40 06 0A 00` — confirm or disprove |
| 3 | MAC address and address type | Scan (MAC) + **nRF Connect** (address type: the library doesn't expose it) | If **random**, the schema's unique index on MAC is wrong |
| 4 | Is `0x1826` in the advertisement? | Scan (adv. detail) | Decides the production scan filter |
| 5 | Negotiated MTU | GATT Tree | Whether records can arrive split |

On item 2: if the actual bytes differ from `64 00 40 06 0A 00`, the documented decode
is wrong and **everything downstream inherits the error**. This is a two-minute check
that protects the whole project.

---

## Part A: Static reads · 5 min · belt stopped

Connect, let discovery finish, **Dump All** on the Read Dump screen.

- [ ] `0x2ACC` hex recorded
- [ ] `0x2AD4` hex recorded
- [ ] `0x2AD3` hex recorded (expect Training Status `Idle` = `0x01`)
- [ ] `180A` strings recorded — firmware/model, for `PHASE-00-FINDINGS.md`
- [ ] Full service + characteristic list matches what is already in `PHASE-00-FINDINGS.md`

Tap **Confirm** on each row you have cross-checked against nRF Connect.

---

## Part B: Idle stream · 5 min · belt stopped

Notification Log screen. Subscribe to `0x2ACD` and `0x2ADA`. **Do not walk yet.**
Let it run two minutes.

- [ ] Does `0x2ACD` notify while stopped? yes / no
- [ ] Rate, from the on-screen Hz counter: ______ Hz
- [ ] Flags tracker: how many distinct values? ______ (if >1 while idle, note it —
      it means the layout varies and the parser must be strictly flag-driven)
- [ ] Packet length: ______ bytes
- [ ] Any `0x2ADA` traffic while idle? op codes: ______

If the rate is ~1 Hz the FTMS spec's recommendation holds. If it is genuinely higher,
say so. The dashboard will need UI throttling.

---

## Part C: Walking capture · 15 min · **the important one**

**Console controls only.** Do not touch the app's control console yet.

1. Start at the lowest speed. Walk 2 minutes.
2. Step the speed up one notch. Walk 1 minute. Repeat to a comfortable maximum, then
   step back down the same way.
3. **At each speed**, use the console-value quick entry to record what the treadmill's
   own display shows at that moment. The app pairs it with the current packet.

**Minimum four matched pairs at different speeds.** This table is the only thing that
can prove the parser is right; there is no substitute.

- [ ] Four or more console-vs-hex pairs captured
- [ ] Did the flags value change mid-session? no / yes → values: ______
- [ ] Heart rate: is a plausible value ever populated? value seen: ______ bpm
- [ ] Stop from the console → `0x2ADA` op code emitted: ______

### C7: the critical one · counter reset behaviour

After stopping, note distance / calories / elapsed time. Then start a **new** session
on the console and check the same fields immediately.

```
Distance   end of session 1: ______ m     start of session 2: ______ m
Elapsed    end of session 1: ______ s     start of session 2: ______ s
Calories   end of session 1: ______ kcal  start of session 2: ______ kcal

Then power-cycle the treadmill, reconnect:
Distance after power cycle: ______ m

Verdict:
[ ] Per-session — counters reset to 0
[ ] Cumulative — counters continue
[ ] Mixed — which fields do what: ______________________________
```

**This one answer determines the entire Phase 06 recording implementation.** Per-session
means values get recorded directly; cumulative means every workout value is a delta
against a start baseline and the engine has to detect mid-workout re-baselining.
Guessing wrong makes every stored workout wrong. Do not skip it and do not guess.

---

## Part D: Control point · 15 min · **decides whether Phase 05 exists**

Control Console screen. **Stand on the side rails.** The belt may start.

**D1.** Confirm indications enabled, then **Request Control** (`00`).

```
Indication received? yes / no      Raw response: ____________   Result: 0x____
```

If this fails, record the result code and **stop Part D**. Speed control is not
available and Phase 05 is void. That's a valid finding, not a failure. The app is
still worth shipping read-only.

**D2.** Set Target Speed while stopped, at your minimum. (2.0 km/h → `02 C8 00`.)

```
Sent: ____________  Response: ____________  Result: 0x____
Belt moved? yes/no      Console display changed? yes/no
```

**D3.** Start (`07`).  → `Response: ______  Belt started? yes/no`

**D4.** Set speed while running, to something higher.

```
Sent: ____________  Response: ____________  Result: 0x____
Belt speed changed? yes/no    Time from write to observed change: ______ s
0x2ADA event emitted? op code 0x____
```

**D5.** Out-of-range: send a speed above 16.0 km/h using the **free hex field**.

```
Sent: ____________  Response: ____________  Result: 0x____   (expect 0x03)
```

**D6.** Pause (`08 02`), then Stop (`08 01`).

```
Pause response: ______  Belt paused? yes/no
Stop  response: ______  Belt stopped? yes/no
```

**D7.** Leave the connection idle 5 minutes, then Set Target Speed again.

```
Result: 0x____  (0x05 = permission expired)   Expiry window: ______ min
```

**D8.** Disconnect, reconnect, immediately Set Target Speed **without** Request Control.

```
Result: 0x____  (expect 0x05 — confirms control must be re-requested)
```

**Tap Confirm with a note on every command that produced the intended physical
result.** That annotation is what turns a hex log into a specification.

---

## Part E: Resilience · 10 min

| | Test | Record |
|---|------|--------|
| E1 | Start a session, walk ~15 m away / into another room | Time to detect disconnect: ______ s · Does the belt keep running? |
| E2 | Walk back | Reconnect works? Time: ______ s · Counters continue or reset? |
| E3 | Toggle phone Bluetooth off and on | Recovers? Time: ______ s · GATT codes in log: ______ |
| E4 | Power the treadmill off and on | Reconnects? Counters reset or retained? |
| E5 | Screen off 5 min during a session, **keep-alive OFF** | Survives? Max notify gap: ______ s · Max heartbeat gap: ______ s |

Reconnect is manual in this phase, by design. Phase 02 automates it. And **if
E5 shows a large heartbeat gap, that is expected here.** It's the baseline Part H
compares against: proof the problem is real before testing the fix. Note it and
move on.

---

## Part F: Advertisement and bonding

- [ ] Did Android ever show a pairing prompt? (Expected: no)
- [ ] Is `0x1826` in the advertised service UUIDs? yes / no
- [ ] Advertised name: ______   MAC: ______   Address type (from nRF Connect's
      device details): public / random

If `0x1826` is not advertised, the production scan filter must fall back to the `FS-`
name prefix, **not** to unfiltered scanning, which is slower and drains more battery.
If the address is random resolvable, remembering the device by MAC will break and the
schema's `UX_Device_MacAddress` is wrong.

---

## Part G: Heart rate · 10 min

Subscribe to `0x2A37` in service `180D`.

**G1.** No hands on the grips. → `Notifies? yes/no · Flags: 0x____ · Value: ______ bpm`

**G2.** Grip both handles firmly, 60 s. Meanwhile count your own pulse at the wrist
for 15 s and multiply by 4.

```
Time from grip to first plausible reading: ______ s
0x2A37 after 60 s: ______ bpm    Manually counted: ______ bpm    Difference: ______
Stable, or jumps by ______ bpm?
```

**G3.** At the same moment, does the FTMS field in `0x2ACD` agree? yes / no / absent

**G4.** Release the grips. → drops to 0 / holds last value / stops notifying

**G5: the decision.** Be honest; this determines whether HR ships at all.

```
[ ] Usable    — within ~10 bpm, stable, contact status reliable
                → show in dashboard and charts
[ ] Marginal  — works but noisy or slow to settle
                → record to the database, hide from the UI, revisit later
[ ] Unusable  — implausible, wildly unstable, or never populates
                → cut heart rate from the app entirely
```

A metric that is wrong half the time is worse than no metric. It will permanently
pollute the average and maximum HR columns of every stored workout. **Cutting it is a
perfectly good outcome.**

---

## Part H: Screen off · ~90 min · **decides where BLE lives**

A separate session from A–G. This is the test of the one risk the Expo stack adds:
whether JavaScript keeps receiving BLE data *and* keeps running its timers with the
screen locked, on HyperOS, for a full workout. See task 0.12 for what the keep-alive
is and why it's needed.

**Setup, all of it before starting:**

- [ ] **Release APK** installed (`npx expo run:android --variant release --device`),
      not the dev build. The release build carries its own JavaScript, so nothing
      depends on the PC's bundler staying reachable.
- [ ] **Phone unplugged.** Android doesn't enter Doze while charging, so a test run on
      a cable can pass and then fail in a real workout. If you want the agent watching
      `adb logcat` live, pair wireless debugging first.
- [ ] All four HyperOS controls set (see `PHASE-00-FINDINGS.md`): Autostart on,
      Battery saver "No restrictions", locked in Recents, battery optimisation off.
      Without them a failure here says nothing about the code.
- [ ] Connected, `0x2ACD` subscribed on the Notification Log screen, gap meter reset.

**H1. Baseline, keep-alive OFF.** (Skip if E5 already recorded it.) Lock the screen
for 5 minutes, belt stopped is fine.

```
Max notify gap: ______ s     Max heartbeat gap: ______ s
```

Expect the heartbeat gap to be roughly the whole locked period. That's the problem
the keep-alive exists to fix.

**H2. Keep-alive ON, screen locked, 15 minutes walking.** Turn the Keep-alive toggle
on (a "Keep-alive spike" notification appears), start walking, lock the screen,
don't look at it for 15 minutes.

```
Notification visible the whole time? yes / no
Max notify gap: ______ s     Max heartbeat gap: ______ s     Disconnects: ______
```

**H3. Keep-alive ON, split screen with YouTube, 15 minutes.** Unlock, put the app in
split screen with YouTube playing a video in the other half, keep walking. YouTube's
memory use is the realistic pressure that makes HyperOS reclaim background apps.

```
Max notify gap: ______ s     Max heartbeat gap: ______ s     Disconnects: ______
App still alive afterwards (not restarted)? yes / no
```

**H4. Keep-alive ON, screen locked, 60 minutes walking.** Your normal session length.

```
Max notify gap: ______ s     Max heartbeat gap: ______ s     Disconnects: ______
Battery %, start: ______   end: ______
```

**H5: the decision.**

```
[ ] Holds        — every run: notify gap ≤ 5 s, heartbeat gap ≤ 10 s, no disconnect
                   → BLE and recording stay in TypeScript; Phase 07 finishes the spike
[ ] Partly holds — notifies keep arriving but heartbeats stall (or the reverse)
                   → write down exactly which; Phase 07 starts with the agent reviewing it
[ ] Fails        — gaps over a minute, disconnects, or the app gets killed
                   → BLE and recording move into the Kotlin module (Phase 07's fallback)
```

Copy the capture file off the phone right after H4, before anything else.

---

## After the session

1. Export the Probe Checklist → paste into `PHASE-00-FINDINGS.md`, **measured facts
   only.** Anything still uncertain stays blank there, tagged with the phase it
   blocks, rather than guessed.
2. Share the capture JSONL files → commit them under `../../captures/`.
3. Share the app log file.
4. Hand back to the agent with: *"Update `../phase-01-protocol-decode/README.md`'s
   FTMS protocol reference with these measured values, build parser fixtures from
   the captured hex, and list what is still unresolved in
   `PHASE-00-FINDINGS.md`."*

## Done when

- [ ] Every question below has an answer in `PHASE-00-FINDINGS.md`
- [ ] Raw hex captured for `0x2ACC` and `0x2AD4`
- [ ] Four or more matched console-vs-hex pairs at different speeds
- [ ] **C7 counter semantics answered unambiguously**
- [ ] **Part D control point verdict unambiguous** — Phase 05 either exists or does not
- [ ] **G5 heart rate decision made**, including "cut it" as a valid answer
- [ ] **H5 background decision made**, from runs done unplugged on the release APK
