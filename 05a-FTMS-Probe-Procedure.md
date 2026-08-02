# FTMS Probe Procedure (`[HUMAN]`)

> Run this on the real treadmill. Its output resolves every `TBD` in
> `05-FTMS-Protocol.md`.
> **Budget 45–60 minutes.** Do it properly once rather than three times badly.

---

## Progress (updated 2026-07-28)

| Part | Status |
|------|--------|
| A — Static reads | **Mostly done.** Service and characteristic inventory captured, feature flags decoded, speed range read. Raw hex still missing. |
| B — Idle stream | Not started |
| C — Walking capture | Not started ← **includes the highest-priority unknown** |
| D — Control point | Not started ← **decides whether Phase 6 exists** |
| E — Resilience | Not started |
| F — Bonding / advertisement | **Partly done.** Bonding: not required. Advertisement contents and MAC still missing. |
| G — Heart rate (new) | Not started |

---

## Ten-minute quick wins: do these first

Five items need no walking, no belt, and no app build. Just nRF Connect:

1. **Raw hex of `0x2ACC`.** Read the characteristic, copy the byte string. Needed as a
   parser test fixture and to independently verify the decoded feature list, which is
   known to over-claim (see protocol doc §2).
2. **Raw hex of `0x2AD4`.** Same. Predicted to be `64 00 40 06 0A 00`; confirm or
   disprove.
3. **MAC address and address type** (public vs. random resolvable), from the scanner
   view. If random, the database's unique index on MAC is wrong.
4. **Is `0x1826` in the advertisement?** Inspect the raw advertising packet. Decides
   the Phase 1 scan filter.
5. **Negotiated MTU.** nRF Connect shows this after connecting.

Capturing these unblocks parser work and the Phase 1 scan implementation without
waiting for a full session.

---

## Why this exists

The implementing agent cannot do any of this. It has no Bluetooth radio and no
treadmill. Every protocol assumption in this project is unverified until you run
these steps and paste the results back.

Until then, code written against those assumptions is a guess wearing a spec's
clothing.

---

## Before you start

**Required:**
- Phone with the app built through Phase 3 (diagnostic screen working)
- Treadmill powered on, safety key inserted, in a state where you can walk on it
- Somewhere to paste text (the app's "share log" button, into a note or email)

**Strongly recommended:** install **nRF Connect** (Nordic Semiconductor, free, Play
Store) as an independent cross-check. If your app's decoder disagrees with nRF
Connect, nRF Connect is right. It also lets you complete most of Part A even if the
app's diagnostic screen has bugs.

**Safety:** Parts D and E involve the belt moving while you interact with the phone.
Stand on the side rails, not the belt, when starting a remote-control test. Keep the
safety key clipped on. If the belt does something unexpected, pull the key. Do not
try to fix it from the phone.

---

## Recording your results

Fill in `docs/DEVICE.md` as you go. Copy the log after each part rather than at the
very end: if the app crashes you lose the capture.

For every hex value, record the **complete** byte string, including leading zeros.
`02 00` and `2 0` are not the same thing and the second is unusable.

---

## Part A: Static reads (5 min, belt stopped)

Connect and let service discovery complete.

**A1. Full characteristic inventory (✅ DONE 2026-07-28)**

All six FTMS characteristics present with expected properties. Services: `1800`,
`180A`, `180D`, `FFE0`, `FFF0`, `1826`.

`FFE0` and `FFF0` identified as FitShow transparent-serial services. Recorded for
completeness; **not a fallback plan.** The FitShow UART protocol is undocumented and
prior public decoding attempts have not succeeded.

`180D` (Heart Rate) is a useful find and adds **Part G** below.

Remaining from this step: nothing.

**A2. Read `0x2ACC` (Feature): ⚠️ DECODED, RAW HEX STILL NEEDED**

Decoded feature list captured. **It is provably wrong in at least one respect:** the
device claims Inclination Target Setting on a machine with no incline, while omitting
inclination from the machine-features word. See protocol doc §2 for the full analysis
and the design consequences.

```
Raw hex STILL NEEDED: ________________________________
```

**Capture the raw bytes.** Two reasons: they become a parser unit test fixture, and
they let the decode be independently verified rather than trusted.

**A3. Read `0x2AD4` (Supported Speed Range): ✅ DECODED, RAW HEX STILL NEEDED**

```
Min: 1.0 km/h   Max: 16.0 km/h   Increment: 0.1 km/h
Raw hex STILL NEEDED: ____________________
```

Predicted bytes: `64 00 40 06 0A 00`. Confirm or disprove. If the actual bytes differ,
the decode is wrong and everything downstream inherits the error.

Sanity check passed: 16 km/h matches a full folding treadmill. (An earlier revision of
these documents wrongly suspected this was a walking pad with a lower real ceiling.)

**A4. Read `0x2AD3` (Training Status)** if present.

```
Raw hex: ____________  Decoded status value: 0x____
```

---

## Part B: Idle notification stream (5 min, belt stopped)

Subscribe to `0x2ACD` and `0x2ADA`. **Do not walk yet.** Let it run for 2 minutes.

```
Does 0x2ACD notify while the belt is stopped?   [ ] yes  [ ] no
Notification rate (count over 60 s ÷ 60):        _____ Hz
Flags value (should be constant):                 0x________
Full packet, repeated 3 times:
  ______________________________________________
  ______________________________________________
  ______________________________________________
Packet length in bytes:                           _____

Any 0x2ADA notifications while idle?             [ ] yes  [ ] no
If yes, op codes:                                 ______________
```

**Rate matters.** If it's ~1 Hz, the FTMS spec's recommendation holds and the original
"5–10/sec" figure was wrong. If it's genuinely higher, note it. Phase 4 needs UI
throttling.

---

## Part C: Walking capture (15 min, the important one)

Walk on the treadmill using the **console controls only**. Do not touch the app's
speed controls yet.

**C1.** Start at the lowest speed. Walk 2 minutes.

**C2.** Increase speed one step. Walk 1 minute. Repeat until you reach a comfortable
maximum, then step back down the same way.

**C3.** At each speed, record one full packet **and** what the treadmill console
shows at the same moment:

```
Console speed  Console distance  Console time  Raw 0x2ACD hex
_____ km/h     _____ km/_____m   _____         _______________________________
_____ km/h     _____ km/_____m   _____         _______________________________
_____ km/h     _____ km/_____m   _____         _______________________________
_____ km/h     _____ km/_____m   _____         _______________________________
```

This table is what validates the parser. Without matched console-vs-hex pairs there is
no way to prove the decoder is right.

**C4.** Does the flags value ever change mid-session?

```
[ ] no, constant
[ ] yes, values seen: ____________________________
```

If it changes, the device is splitting records across notifications (More Data bit) or
conditionally including fields. Either way the parser must be flag-driven, which it
already must be.

**C5.** Heart rate: quick check here, full comparison in **Part G**.

```
Is bit 8 set in the 0x2ACD flags?         [ ] yes  [ ] no
Is a plausible HR value ever populated?   [ ] yes  [ ] no
Value seen: ______ bpm
```

**C6. Stop the belt from the console.**

```
0x2ADA op code emitted on stop:  0x____  parameters: ______
```

**C7. THE CRITICAL ONE. Counter reset behaviour.**

After stopping, note the reported distance / calories / elapsed time. Then start a
**new** session on the console and check the same fields immediately.

```
Distance at end of session 1:     ______ m
Distance at start of session 2:   ______ m
Elapsed time at end of session 1: ______ s
Elapsed time at start of session 2: ______ s
Calories at end of session 1:     ______ kcal
Calories at start of session 2:   ______ kcal

Verdict:
[ ] Per-session: counters reset to 0
[ ] Cumulative: counters continue from the previous value
[ ] Mixed: specify which fields do what: ________________________
```

Also test: **power-cycle the treadmill**, reconnect, and check whether counters reset.

```
After power cycle, distance reads: ______ m
```

This one answer determines the entire Phase 7 recording implementation. Do not skip it
and do not guess.

---

## Part D: Control point (15 min, belt stopped initially)

Only if `0x2AD9` was found in A1.

**Stand on the side rails.** The belt may start.

**D1. Enable indications** on `0x2AD9`, then write `00` (Request Control).

```
Indication received?  [ ] yes  [ ] no  (if no, wait 5 s then try once more)
Raw response hex:     ____________
Result code:          0x____   (0x01 = success)
```

If this fails, record the result code and stop Part D. Speed control is not available
and Phase 6 is void. That's a valid finding, not a failure.

**D2. Set Target Speed while stopped.** Write `02` followed by your minimum speed as
uint16 LE. For 2.0 km/h: `02 C8 00`.

```
Bytes written:      ____________
Response hex:       ____________  Result: 0x____
Did the belt move?  [ ] yes  [ ] no
Did the console display change?  [ ] yes  [ ] no
```

**D3. Start.** Write `07`.

```
Response hex: ____________  Result: 0x____
Belt started? [ ] yes  [ ] no
```

**D4. Set speed while running.** Step on the belt only if it's moving safely. Write
`02` with a higher speed.

```
Bytes written:  ____________
Response:       ____________  Result: 0x____
Belt speed changed?  [ ] yes  [ ] no
Time from write to observed speed change: ______ s
0x2ADA event emitted? op code 0x____
```

**D5. Out-of-range value.** Write a speed above the maximum from A3.

```
Bytes written: ____________
Response:      ____________  Result: 0x____  (expect 0x03 Invalid Parameter)
```

**D6. Pause then Stop.** Write `08 02` (pause), then `08 01` (stop).

```
Pause response: ____________  Belt paused? [ ] yes [ ] no
Stop response:  ____________  Belt stopped? [ ] yes [ ] no
```

**D7. Permission expiry.** Leave the connection idle for 5 minutes without sending
anything, then write a Set Target Speed.

```
Result code: 0x____   (0x05 = Control Not Permitted → permission expired)
Approximate expiry window if it did expire: ______ minutes
```

**D8. Permission after reconnect.** Disconnect, reconnect, and immediately write Set
Target Speed **without** Request Control.

```
Result code: 0x____  (expect 0x05, confirms control must be re-requested)
```

---

## Part E: Resilience (10 min)

**E1.** Start a workout, then walk out of range (~15 m or into another room).

```
Time until disconnect detected: ______ s
Does the treadmill keep running? [ ] yes  [ ] no
```

**E2.** Walk back into range.

```
Auto-reconnect succeeds?        [ ] yes  [ ] no
Time to reconnect:              ______ s
Do counters continue or reset?  [ ] continue  [ ] reset
```

**E3.** Toggle phone Bluetooth off and on mid-session.

```
Recovers? [ ] yes  [ ] no   Time: ______ s
GATT error codes seen in the log: ______________
```

**E4.** Power the treadmill off and on mid-session.

```
Reconnects when powered back on? [ ] yes  [ ] no
Counters after power cycle:      [ ] reset  [ ] retained
```

**E5.** Screen off for 5 minutes during a session.

```
Connection survives?     [ ] yes  [ ] no
Notification gaps seen:  ______ s maximum
```

If E5 fails, that is expected before Phase 8 and is the reason the foreground service
was moved earlier. Note it and move on.

---

## Part F: Bonding and advertisement

**F1.** Did Android ever show a pairing prompt?

```
[ ] yes, bonding required
[ ] no, bonding not required
```

**F2.** In nRF Connect's scanner, inspect the raw advertisement.

```
Is 0x1826 in the advertised service UUIDs?  [ ] yes  [ ] no
Advertised device name:                      ____________________
MAC address:                                 ____________________
Address type: [ ] public  [ ] random
```

If `0x1826` is not advertised, the service-UUID scan filter finds nothing and Phase 1
must scan unfiltered. If the address type is random/resolvable, remembering the device
by MAC will break and you'll need to match on name instead. Note it prominently.

---

## Part G (NEW): Heart rate, two sources (10 min)

The device exposes standard Heart Rate Service `180D` **and** an FTMS heart rate field.
This part decides which to use, and whether to use either.

**G1.** Subscribe to `0x2A37` (Heart Rate Measurement) in `180D`. Do not grip yet.

```
Does it notify with no hands on the grips?  [ ] yes  [ ] no
Flags byte:                                  0x____
Value reported:                              ______ bpm
```

**G2.** Grip both handles firmly. Hold for 60 seconds. Meanwhile, count your own pulse
at the wrist for 15 s and multiply by 4.

```
Time from grip to first plausible reading:  ______ s
0x2A37 value after 60 s:                     ______ bpm
Manually counted pulse:                      ______ bpm
Difference:                                  ______ bpm
Is the reading stable or does it jump?       [ ] stable  [ ] jumps by ______ bpm
```

**G3.** At the same moment, check the FTMS field in `0x2ACD`.

```
Does the FTMS HR field agree with 0x2A37?   [ ] yes  [ ] no  [ ] field absent
FTMS value: ______ bpm    0x2A37 value: ______ bpm
```

**G4.** Release the grips.

```
Behaviour on release:  [ ] drops to 0  [ ] holds last value  [ ] stops notifying
Sensor contact bits (flags bits 1-2) change?  [ ] yes  [ ] no
```

**G5. Decision.** Be honest here. This determines whether HR ships.

```
[ ] Usable: accurate within ~10 bpm, stable, contact status reliable
      → use 0x2A37, show in dashboard and charts
[ ] Marginal: works but noisy or slow to settle
      → record to WorkoutSample, hide from the UI, revisit later
[ ] Unusable: implausible, wildly unstable, or never populates
      → cut heart rate from the app entirely
```

A metric that is wrong half the time is worse than no metric. It will pollute the
average and maximum HR columns of every stored workout, permanently. Cutting it is a
perfectly good outcome.

---

## After the session

1. Fill in `docs/DEVICE.md` completely.
2. Copy the full diagnostic log out of the app.
3. Paste the log plus the completed answers back to the implementing agent, with the
   instruction: *"Update `05-FTMS-Protocol.md` with these measured values, add parser
   unit test fixtures from the captured hex, and list anything still unresolved."*
4. Anything still unknown goes in `docs/ASSUMPTIONS.md` with the phase it blocks.

## Definition of done

- Every outstanding question in `05-FTMS-Protocol.md` §10 has an answer
- Raw hex captured for `0x2ACC` and `0x2AD4`
- At least four matched console-vs-hex pairs captured at different speeds
- **The counter reset question (C7) is answered unambiguously.** This is the one that
  determines the entire Phase 7 implementation
- **The control point verdict (Part D) is unambiguous.** Phase 6 either exists or
  doesn't
- The heart rate decision (G5) is made, including "cut it" as a valid answer
- Parser unit tests exist using the captured hex as fixtures, and pass
