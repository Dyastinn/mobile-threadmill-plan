# Phase 01 — Decoding (01a) and the TreadmillService interface (01b)

> Two separate tracks. **01b needs nothing but Phase 00's project setup, so it can
> start right away.** 01a needs treadmill data that hasn't been captured yet. You
> write the code; the agent explains first and reviews after (see `../README.md`).

**Treadmill needed:** no, for either track.

---

## Track 01b — `TreadmillService` + a fake treadmill ← START HERE

**Waits for:** Phase 00 task 0.1 (the project must exist). `treadmillService.ts` is
already in this folder.
**Unlocks:** Phases 03, 04 and 06. Anything that shows or reacts to treadmill data can
now be built without the treadmill.

### The idea

`FakeTreadmillService` pretends to be the treadmill. It sends realistic speed and
distance numbers once a second, so you can build the dashboard, the workout logic and
the database without walking on a belt every time you test something.

**Why not just wait for the real decoder?** Because six phases need treadmill data to
be built at all, and the real decoder is stuck until the next treadmill session.
About 150 lines of fake unblocks all six.

**The name for this:** a *seam*. `TreadmillService` is an interface, and anything that
uses it can't tell the fake from the real one. The cost is one extra layer. It's worth
it here because the real thing is hard to get to. For something cheap and instant, it
wouldn't be.

TypeScript makes this cheap: any object with the right methods counts as a
`TreadmillService`. Still write `class FakeTreadmillService implements
TreadmillService`, so the compiler points at a missing method right away.

### What you'll learn

- Building something to match an interface someone else designed, including what the
  comments say.
- The event pattern used everywhere in this project: `onSample(listener)` returns a
  function that stops listening. It fits React's `useEffect` cleanup exactly.
- Fake timers in Jest: testing something that ticks once a second without waiting.
- Where code goes: `treadmillService.ts` imports nothing phone-specific, so it goes in
  `src/core/` and gets real tests.
- The services file: the one place to swap the fake for the real thing later.

### Read first

- **`./treadmillService.ts`**, all of it. Every type you need is there.
- **`PHASE-00-FINDINGS.md` V2**: on the real treadmill, `Start` seems to forget the
  target speed. Decide whether your fake copies that (task 1b.2).
- **`PHASE-00-FINDINGS.md`**: the speed range (1.0–16.0 km/h, steps of 0.1) is
  measured. Use it in the fake.
- **`src/core/shared/emitter.ts`** (from Phase 00): `createEmitter<T>()` gives you
  `emit(value)` and `subscribe(listener)`. Use it for the three events.
- **`setInterval` / `clearInterval`**: how the fake ticks once a second. Whatever starts
  an interval must stop it, or it runs forever.
- **`AbortSignal`**: [AbortController (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/AbortController).
  The standard way to cancel async work, used by `connect(deviceId, signal)`.
- **Jest fake timers**: [Timer Mocks](https://jestjs.io/docs/timer-mocks).
  `jest.useFakeTimers()` plus `await jest.advanceTimersByTimeAsync(ms)`.

### Tasks

**1b.1: Move the interface into `core/`**

1. Create `src/core/treadmill/`.
2. Move the file with git, don't retype it:
   `git mv phases/phase-01-protocol-decode/treadmillService.ts src/core/treadmill/`
3. From `src/`, run `npx tsc --noEmit` and `npx expo lint`. Both must be clean. Lint
   proves nothing phone-specific slipped in.

**1b.2: `FakeTreadmillService`**

Creates `src/core/treadmill/fakeTreadmillService.ts`. It implements every method of
`TreadmillService`, using a timer instead of Bluetooth:

- About once a second, send a sample that looks like a real walk: speed ramps up,
  distance grows, time counts up. The dashboard will show these.
- Support the `normalWalk` scenario: warm-up, steady, cool-down.
- `connect()` goes `connecting → discovering → ready`, with a short pause at each step,
  like the real connection. If the signal is aborted, go back to `disconnected`.
  `disconnect()` goes back to `disconnected` and stops the timer.
- The control methods return a believable `ControlResult` and change the samples (after
  `setSpeed(8)`, samples show 8 km/h).
- Fill in capabilities and speed range once connected.
- **Your call:** should the fake copy "Start forgets the target speed"? Either answer
  is fine. Pick one and say which in a one-line comment.

A starting outline:

```ts
import { createEmitter } from '../shared/emitter';
import {
  type ConnectionState, type ConnectionStateChange, type ControlResult,
  type MachineEvent, type SpeedRange, type TreadmillCapabilities,
  type TreadmillSample, type TreadmillService, type Unsubscribe,
} from './treadmillService';

export class FakeTreadmillService implements TreadmillService {
  private state: ConnectionState = 'disconnected';
  private capabilities: TreadmillCapabilities | null = null;
  private speedRange: SpeedRange | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;
  private speedKph = 0;
  private distanceMeters = 0;

  private readonly stateChanges = createEmitter<ConnectionStateChange>();
  private readonly samples = createEmitter<TreadmillSample>();
  private readonly machineEvents = createEmitter<MachineEvent>();

  getConnectionState(): ConnectionState { return this.state; }
  // TODO: getCapabilities, getSpeedRange, hasControl

  onSample(listener: (sample: TreadmillSample) => void): Unsubscribe {
    return this.samples.subscribe(listener);
  }
  // TODO: onConnectionStateChange, onMachineEvent (same one line)

  async connect(deviceId: string, signal?: AbortSignal): Promise<void> {
    // TODO: connecting -> discovering -> ready, short pause between each
  }

  private tick(): void {
    // TODO: move speed and distance along the walk, then send a sample
  }

  // TODO: disconnect, requestControl, setSpeed, start, pause, stop
}
```

**Not needed yet:** the other scenarios (`dropoutMidSession`, `sparseFields`,
`controlRejected`, `intervalWalk`) and counter resets. Add each one when a later test
needs it.

**1b.3: Tests**

Create `src/core/treadmill/clampSpeed.test.ts` and
`src/core/treadmill/fakeTreadmillService.test.ts`.

1. `clampSpeed`: a `test.each` table with min, max, below min, above max, a
   between-steps value (6.47 → 6.5), and `1.0 + 3 * 0.1` → exactly `1.3`.
2. The fake, with `jest.useFakeTimers()`:
   - `connect` reaches `ready`.
   - A listener added with `onSample` gets at least two samples in 3 fake seconds.
   - After calling the unsubscribe function, the listener gets nothing more. (A
     listener you can't remove is a memory leak waiting to happen.)
3. `npm test`: everything passes, including Phase 00's tests.

**1b.4: Add it to the services file**

In `src/features/services.tsx`, add `treadmill: TreadmillService` to the `Services`
type and `treadmill: new FakeTreadmillService()` where services are created. The type
is the interface, not the class, so nothing else can depend on fake-only details.
Run `npx tsc --noEmit`: a missing field is caught here.

### Review

The agent checks the fake against the interface's comments (for example: control
methods never throw). Then you wire up something tiny together, like a Connect button
and a line showing the latest speed, and watch it work before Phase 03 builds on it.

---

## Track 01a — Decoding the treadmill's data (blocked)

**Waits for:** runbook Part C (four console-vs-hex pairs) and C7 (counter resets).
Neither has been captured yet.
**Unlocks:** the *real* `BleTreadmillService`.

> Desk work once unblocked. The treadmill's bytes come from Phase 00's capture files.

### The idea

The treadmill sends bytes. A parser turns `02 4B 00 A3 01 ...` into
`speedKph = 6.5, distanceMeters = 412`. That's where most Bluetooth bugs live, and this
treadmill is known to bend the spec, so every parser is tested against real captured
bytes.

**Why you can't just read bytes at fixed positions.** Each packet starts with flags
that say which fields follow. A missing field isn't sent at all (not sent as zero), so
everything after it shifts. A fixed-position reader would silently read the wrong
field. Instead, walk through the bytes with a cursor, checking the flags at each step.
Where a format really is fixed (like `0x2AD4`, always three numbers), read fixed
positions: no need for more.

**Check the length first.** Before reading any field, work out from the flags how long
the packet should be, and reject it if it isn't. A bad packet then shows up as one
clear log line, instead of a wrong number on a chart later. This matters most at a
*trust boundary*: data from outside your control, like a Bluetooth packet. `DataView`
also throws if you read past the end, which is a second safety net.

### Read first

- The FTMS reference below.
- **`DataView`**: [DataView (MDN)](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/DataView).
  `getUint8`, `getUint16(offset, true)`, `getInt16(offset, true)`. The `true` means
  "little-endian" (low byte first). FTMS always needs it.
- `PHASE-00-FINDINGS.md` and `../../captures/`.

---

## FTMS protocol reference

> **Partly checked.** The reads are confirmed (see `PHASE-00-FINDINGS.md`); the data
> stream and control point aren't fully captured yet. Source: the Bluetooth SIG
> *Fitness Machine Service* spec v1.0. Where a fact has a confidence note, anything
> below "high" still needs checking against captured bytes.

### What this device is

It advertises as **`FS-9F4235`**. `FS-` means FitShow, who make Bluetooth chips for
treadmills and bikes. That's confirmed by the `FFE0`/`FFF0` services alongside FTMS.

```
treadmill motor board ←serial→ FitShow Bluetooth chip ←Bluetooth→ phone
                                   │
                                   ├── FFE0 / FFF0   FitShow's own protocol
                                   │                  (what the FitShow app uses)
                                   ├── 180D          standard heart rate
                                   └── 1826          FTMS (standard fitness machine)
```

**FTMS here is a translation layer, not the chip's native language.** So: check
everything against real bytes, and don't trust what the device says about itself.

FitShow's own protocol (`FFE0`/`FFF0`) is undocumented and nobody has decoded it
publicly. It's **not** a backup plan; it's only recorded.

### How Bluetooth data works here

FTMS isn't a web API. There's no JSON and no requests. Everything is one of:

| Operation | What it is |
|----------------|------------|
| Read | The phone reads a value (raw bytes) |
| Write | The phone writes raw bytes |
| Notify | The treadmill sends bytes, no acknowledgement |
| Indicate | The treadmill sends bytes, and the phone acknowledges |

Every number longer than one byte is **little-endian**: low byte first.

### Services (confirmed)

| UUID | Name | Used? |
|------|------|------------------|
| `1800` | Generic Access | No |
| `180A` | Device Information | Optional: firmware version |
| `180D` | **Heart Rate** | **Yes: preferred heart rate source** |
| `FFE0` | FitShow's own | No: recorded only |
| `FFF0` | FitShow's own | No: recorded only |
| `1826` | **Fitness Machine** | **Yes: the main one** |

### FTMS characteristics (all present)

| UUID | Name | Supports | Used for |
|------|------|------------|----------------------|
| `0x2ACC` | Fitness Machine Feature | Read | Logged only: see below |
| `0x2ACD` | Treadmill Data | Notify | The main data stream |
| `0x2AD4` | Supported Speed Range | Read | All speed limits in the UI |
| `0x2AD9` | Fitness Machine Control Point | Write + Indicate | Speed control: see Phase 05 |
| `0x2ADA` | Fitness Machine Status | Notify | Things the treadmill did |
| `0x2AD3` | Training Status | Read + Notify | Display only |

All six exist, but **existing isn't the same as working**. `0x2AD9` accepting writes
doesn't mean it obeys them; see Phase 00's control verdict.

If the control handshake fails, hide all speed controls. If `0x2AD4` can't be read,
never guess the speed limits.

### Fitness Machine Feature (`0x2ACC`): Read

**8 bytes: two 32-bit bit fields, little-endian.**

```
[0..3]  Machine features   (uint32 LE)
[4..7]  Target settings    (uint32 LE)
```

**Machine features (bytes 0–3):**

| Bit | Meaning |
|-----|---------|
| 0 | Average Speed |
| 1 | Cadence |
| 2 | Total Distance |
| 3 | Inclination |
| 4 | Elevation Gain |
| 5 | Pace |
| 6 | Step Count |
| 7 | Resistance Level |
| 8 | Stride Count |
| 9 | Expended Energy |
| 10 | Heart Rate Measurement |
| 11 | Metabolic Equivalent |
| 12 | Elapsed Time |
| 13 | Remaining Time |
| 14 | Power Measurement |
| 15 | Force on Belt and Power Output |
| 16 | User Data Retention |
| 17–31 | Reserved |

**Target settings (bytes 4–7):**

| Bit | Meaning |
|-----|---------|
| 0 | **Speed target** ← the one that matters for Phase 05 |
| 1 | Inclination target |
| 2 | Resistance target |
| 3 | Power target |
| 4 | Heart rate target |
| 5 | Targeted expended energy |
| 6 | Targeted step number |
| 7 | Targeted stride number |
| 8 | Targeted distance |
| 9 | Targeted training time |
| 10–31 | Further targets and reserved |

*Confidence: high on machine bits 0–13 and target bits 0–4; moderate on higher bits,
which this device almost certainly doesn't set.*

#### ⚠️ This device's feature list is wrong. Never decide anything from it

From the first capture, the device claims:

**Machine features:** Total Distance, Step Count, Resistance Level, Expended Energy,
Heart Rate, Elapsed Time, Power

**Targets:** Speed, Inclination, Resistance, Power

Three of these are impossible:

| Claim | Reality |
|-------|---------|
| Resistance Level | Treadmills have no resistance |
| Power Measurement | This treadmill has no power meter |
| **Inclination target** | **This machine has no incline at all** |

The last one proves it: incline is missing from the machine features but present in the
targets. You can't set a target for something you don't have. So the list is a default
baked into FitShow's chip, not a description of this treadmill. (The four target bits
are 0–3, i.e. `0x0000000F`: suspiciously "all of them".)

**What to do instead (Phases 01, 03 and 05):**

- **Log `0x2ACC`; never branch on it.**
- **Which fields to show** comes from which fields actually arrive in `0x2ACD` packets
  during the first ~30 seconds.
- **Whether speed control works** comes from actually trying it (Phase 05), not from
  bit 0.

*Confidence: high that the list over-claims.* The raw hex, once captured, goes in
`PHASE-00-FINDINGS.md` and becomes test data.

**About heart rate:** the heart rate bit only means the machine *can* report it. The
handgrip sensors only work while held, and are noisy. See `0x2A37` below.

### Supported Speed Range (`0x2AD4`): Read

**6 bytes: three 16-bit numbers, little-endian, in hundredths of km/h.**

```
[0..1]  Minimum speed
[2..3]  Maximum speed
[4..5]  Smallest step
```

Example: `64 00 58 02 0A 00` → min 1.00, max 6.00, step 0.10 km/h.

**Confirmed for this treadmill:** min 1.0, max 16.0, step 0.1 km/h. It's a full
treadmill, not a walking pad.

Still read it every time instead of hardcoding it: it's the only reliable source and
costs one read. Generate preset buttons from it (for 1.0–16.0: 2, 4, 6, 8, 10, 12
km/h), so the code still works after a firmware change.

### Treadmill Data (`0x2ACD`): Notify

The main data stream, and **the easiest thing in this project to decode wrong.**

```
[0..1]  Flags (uint16 LE)
[2..]   Fields, in the order below, only if their flag bit says so
```

**Three traps:**

**Trap 1: bit 0 is backwards.** Speed is present when bit 0 is **`0`**, and absent when
it's `1`. Every other bit works the normal way. Get this wrong and every field after it
is shifted. Most first-time FTMS code gets this wrong.

**Trap 2: distance is 3 bytes (24-bit), little-endian.** `DataView` has no 24-bit
reader, so build it from two reads:

```ts
const distance = view.getUint16(i, true) | (view.getUint8(i + 2) << 16);
i += 3;
```

(`true` = little-endian. `DataView` defaults to the opposite, which is the easiest way
to get every field in this file wrong.)

**Trap 3: energy is one flag bit but three fields**, 5 bytes: total (2 bytes, kcal), per
hour (2 bytes, kcal), per minute (1 byte, kcal). Skip 2 bytes instead of 5 and
everything after is shifted.

**Fields**, in this order. Skip any whose bit is off.

| Bit | Field | Type | Bytes | Unit |
|-----|-------|------|-------|------|
| 0 == 0 | Speed | uint16 | 2 | 0.01 km/h |
| 1 | Average Speed | uint16 | 2 | 0.01 km/h |
| 2 | Total Distance | **uint24** | **3** | 1 m |
| 3 | Inclination | sint16 | 2 | 0.1 % |
| 3 | Ramp Angle | sint16 | 2 | 0.1 degree |
| 4 | Positive Elevation Gain | uint16 | 2 | 0.1 m |
| 4 | Negative Elevation Gain | uint16 | 2 | 0.1 m |
| 5 | Pace | uint8 | 1 | 0.1 km/min |
| 6 | Average Pace | uint8 | 1 | 0.1 km/min |
| 7 | Total Energy | uint16 | 2 | 1 kcal |
| 7 | Energy Per Hour | uint16 | 2 | 1 kcal |
| 7 | Energy Per Minute | uint8 | 1 | 1 kcal |
| 8 | Heart Rate | uint8 | 1 | 1 bpm |
| 9 | Metabolic Equivalent | uint8 | 1 | 0.1 |
| 10 | Elapsed Time | uint16 | 2 | 1 s |
| 11 | Remaining Time | uint16 | 2 | 1 s |
| 12–15 | Reserved | | | |

Bits 3 and 4 each cover **two** fields (4 bytes). Bit 7 covers three (5 bytes).

*Confidence: high on bits 0, 1, 2, 7, 8, 10 (the ones this device will use); moderate
on pace and metabolic equivalent.*

**How often.** The spec says about once a second. If Phase 00 measures faster, the
dashboard limits itself to 4 updates a second (Phase 03). Saving is unaffected.

**Limits.** Time (uint16) goes up to 18 hours; distance (uint24) to 16,777 km. Neither
rolls over in a workout. **They may reset or keep counting between sessions**: that's
Finding V1, which Phase 06 depends on.

**Parser rules:**

- Walk a cursor, guided by the flags. Never fixed positions.
- **Check the length first:** work out the expected length from the flags, and reject
  the packet if it doesn't match. Log rejected packets with their hex.
- Return a result instead of throwing for a bad packet. A bad packet is normal, and
  `try`/`catch` around every packet would hide real errors. Use
  `{ ok: true, value }` / `{ ok: false, reason, hex }`.
- Every field can be `null`, because each packet can include a different set.

### Heart Rate (`180D`): the preferred source

The device has a standard Heart Rate service as well as the heart rate field in
`0x2ACD`. Two sources for one value.

**`0x2A37`: Heart Rate Measurement, Notify**

```
[0]     Flags (uint8)
          bit 0:    0 = value is 1 byte, 1 = value is 2 bytes
          bits 1-2: sensor contact
          bit 3:    energy field present
          bit 4:    RR-interval fields present
[1..]   Heart rate (1 or 2 bytes, per bit 0)
[..]    Energy (uint16, kJ), if flagged
[..]    RR intervals (uint16 each, 1/1024 s), if flagged
```

For handgrips, expect flags `0x00` and one byte: the simplest case.

**Bits 1–2 (contact)** tell "not holding the grips" apart from "reading is zero":

| Value | Meaning |
|-------|---------|
| `0b00` / `0b01` | Contact detection not supported |
| `0b10` | Supported, **no contact** |
| `0b11` | Supported, contact |

*Confidence: high. A long-standing, widely used standard.*

**Why prefer it.** A dedicated characteristic is less likely to be garbled than a field
deep inside `0x2ACD`. Heart rate is one of the last fields there, so it's the first to
break if the layout is off.

**Rule:** use `0x2A37` if it works. Use the `0x2ACD` field only if `180D` sends nothing.

**Whether heart rate ships at all:** handgrips only read while held, and they're noisy.
**If the capture shows sparse, wrong or jumpy values, remove heart rate from the
dashboard and charts.** A number that's wrong half the time ruins every saved average.
See Finding V3. "Marginal" means: save it, don't show it.

### Fitness Machine Status (`0x2ADA`): Notify

**These are events, not a status.** It reports what just happened, usually because
someone pressed a button on the treadmill. The app keeps track of its own state.

```
[0]     Op code (uint8)
[1..]   Extra values (depends on the code, often none)
```

| Code | Meaning | Extra |
|----|---------|------------|
| `0x01` | Reset | — |
| `0x02` | Stopped or paused by the user | `0x01` stop, `0x02` pause |
| `0x03` | Stopped by the safety key | — |
| `0x04` | Started or resumed by the user | — |
| `0x05` | Target speed changed | uint16, 0.01 km/h |
| `0x06` | Target incline changed | sint16, 0.1 % |
| `0x07` | Target resistance changed | — |
| `0x08` | Target power changed | — |
| `0x09` | Target heart rate changed | — |
| `0xFF` | Control permission lost | — |

*Confidence: high on `0x01`–`0x05` and `0xFF`; moderate on `0x06`–`0x09`, which don't
matter without an incline.*

**`0xFF` (control lost):** disable speed controls and send Request Control again before
enabling them (Phase 05, task 5.2).

**`0x03` (safety key):** treat as a hard stop. Never try to restart over Bluetooth.

If the device sends nothing here, work out the state from the speed in `0x2ACD`
instead. That works, just less precisely.

### Training Status (`0x2AD3`): Read + Notify

Separate from the machine status and the app's workout state. For display only.

```
[0]     Flags (uint8)
          bit 0: status text present
          bit 1: extended text present
[1]     Status (uint8)
[2..]   Status text (UTF-8), if flagged
```

| Value | Meaning |
|-------|---------|
| `0x00` | Other |
| `0x01` | Idle |
| `0x02` | Warming Up |
| `0x03` | Low Intensity Interval |
| `0x04` | High Intensity Interval |
| `0x05` | Recovery Interval |
| `0x06` | Isometric |
| `0x07` | Heart Rate Control |
| `0x08` | Fitness Test |
| `0x09` | Speed too low for control |
| `0x0A` | Speed too high for control |
| `0x0B` | Cool Down |
| `0x0C` | Watt Control |
| `0x0D` | Manual Mode |
| `0x0E` | Pre-Workout |
| `0x0F` | Post-Workout |

**This device at rest: `Idle` (`0x01`).** So it's filled in, at least. Whether it
changes during a workout is unknown.

*Confidence: high on the list; low on whether this device uses it properly. Many cheap
machines always report the same value.*

**Never drive the workout state from this.**

### Don't start until

- [ ] Raw hex for `0x2ACC` and `0x2AD4` exists
- [ ] At least four console-vs-hex pairs exist
- [ ] V1 (do counters reset?) has an answer

Without these you'd be writing a parser you can't test. If you get treadmill time
before then, runbook Part C is the most valuable 15 minutes.

### Tasks (once unblocked)

**1a.1: Test data from the captures**

Create `src/core/ftms/fixtures/*.json`. Copy every distinct packet out of
`../../captures/` into named test cases, each noting where it came from (file, line
`id`, time, and for `0x2ACD` the console values). Include the odd ones: short packets,
strange lengths. Jest can import JSON files directly.

**1a.2: The `0x2ACD` parser (the important one)**

Create `src/core/ftms/treadmillData.ts`, next to Phase 00's command builder. Use a
cursor and the flags, check the length first, and watch for the three traps above.

```ts
export type ParseResult<T> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false; readonly reason: string; readonly hex: string };

/** Decodes one 0x2ACD packet. Never throws. */
export function parseTreadmillData(bytes: Uint8Array, receivedAtMs: number): ParseResult<TreadmillSample> {
  // TODO: fewer than 2 bytes -> { ok: false }
  // TODO: read the flags with a DataView (see the note below)
  // TODO: work out the expected length from the flags; reject if different
  // TODO: walk a cursor from byte 2, field by field, in table order
  // TODO: return every field, with null for fields the flags say are missing
}
```

Create the `DataView` as `new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)`.
A `Uint8Array` can be a window into a bigger block of memory, and forgetting the offset
causes a bug that only shows up later.

**1a.3: The other parsers**

In `src/core/ftms/`, one file each:
`fitnessMachineFeature.ts` (`0x2ACC`: decode and expose, never decide from it),
`supportedSpeedRange.ts` (`0x2AD4`: three 16-bit numbers),
`machineStatus.ts` (`0x2ADA`: returns a `MachineEvent`),
`trainingStatus.ts` (`0x2AD3`: display only),
`heartRateMeasurement.ts` (`0x2A37`: bit 0 picks 1 or 2 bytes, bits 1–2 are contact).
The control-point reply decoder already exists from Phase 00; reuse it.

**1a.4: Which fields are real**

Create `src/core/ftms/capabilityTracker.ts`. Collect the flag bits seen in `0x2ACD`
packets during the first ~30 seconds. A field is real if it actually arrives. Feed it
`(flags, receivedAtMs)`; ask it `isFieldObserved('distance')`.

**1a.5: The real `BleTreadmillService`**

Create `src/features/treadmill/bleTreadmillService.ts`. It lives in `features/`
because it uses the Bluetooth code from Phase 00. It implements `TreadmillService`
using the parsers above. When it's done and reviewed, swap it in for the fake in the
services file: one line.

**1a.6: Update the reference**

Fill in every blank in `PHASE-00-FINDINGS.md` with a measured value or "not supported",
and fix any confidence note above that the capture proves wrong. Anything still unknown
goes in the findings' "Still unresolved" table, with the phase it blocks.

### Tests (this is where most of the project's real tests are)

All in `src/core/ftms/*.test.ts`:

- Every test case decodes to its expected values.
- Console-matched cases decode to the console's numbers (`toBeCloseTo`). This proves
  the decoder is right, not just that it doesn't crash.
- Bad input (too short, length doesn't match flags, empty) returns `{ ok: false }` with
  the hex. Never throws.
- Bit 0: speed is present when bit 0 is 0, absent when it's 1.
- Distance at the edges: `0x00FFFF` and `0x010000`.
- Energy skips exactly 5 bytes: the field after it still decodes correctly.
- A `Uint8Array` that's a window into a bigger buffer decodes the same as a fresh copy.

### Done when

- [ ] Every blank in `PHASE-00-FINDINGS.md` is filled or marked "not supported"
- [ ] Console-matched cases decode to the console's values at every captured speed
- [ ] Bad-input tests pass: nothing throws, everything is logged
- [ ] The fake still works, and the real service swaps in without changing anything else
- [ ] `tsc` and lint are clean; Phase 00's tests still pass
