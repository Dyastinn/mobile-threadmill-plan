# Phase 01 — Protocol Decode & Fixtures (01a) + `TreadmillService` Contract (01b)

> This phase is split into two independent tracks with different blocking status.
> **Track 01b needs nothing but Phase 00's scaffold and starts right away.** Track
> 01a needs real capture data that doesn't exist yet. See `../README.md` for the
> collaboration model: you write the code, the agent explains concepts up front and
> reviews after.

**Hardware:** none for either track.

---

## Track 01b — `TreadmillService` contract + `FakeTreadmillService` ← START HERE

**Blocked by:** Phase 00 task 0.1 (the project has to exist). `treadmillService.ts`
already exists in this folder.
**Unblocks:** Phase 03 (Live Dashboard), 04 (Workout Engine), 06 (Recording & Schema).
Anything that needs to display or react to treadmill data can now be built and tested
without the real hardware in the room.

### Understanding what you're building (read this before the tasks)

**The problem.** `FakeTreadmillService` is a stand-in for the real Bluetooth
connection, the way a hardware rig fakes an engine's signals so a car's dashboard
can be built and tested before the engine exists. Track 01a (the actual byte
parsers) is blocked on physical access to the hardware, a scarce, in-person
resource that can't be conjured by typing faster.

**Why not just wait for the real thing?** The simplest-sounding plan is: don't
build a fake, wait until 01a is unblocked and the real parsers exist, then build
one `BleTreadmillService` and be done. That's actually the worse plan, not the
simpler one. Phases 03, 04, 06, 09, 10, 11, and 12 all need treadmill data flowing
to be built and tested at all. If everything waits on one in-person capture
session, the rest of the project serializes behind a single bottleneck outside
your control. Building one small fake now isn't complexity for its own sake; it's
what lets six other phases proceed in parallel with your actual schedule instead
of the treadmill's availability. The concrete test for whether an abstraction
earns its keep is counting how many things it unblocks. Here, the answer is six
phases for about 150 lines of code. Clear win, not a judgement call.

**The pattern, named plainly.** This is **programming against an interface, not
an implementation**, one of the oldest ideas in software design, applied here as a
**seam**: a deliberately placed point where a real dependency (live Bluetooth) can
be swapped for a stand-in (the fake) without anything on the other side noticing
or changing. `TreadmillService` *is* the seam. The cost is real: one extra layer of
indirection. Any code holding a `TreadmillService` genuinely doesn't know whether
it's talking to hardware or a simulation, which is one more thing to hold in your
head. The payoff is specific to this project: BLE hardware is expensive to keep "in
the loop" for every phase's day-to-day development (you'd need the treadmill
physically present and powered on for every single test run). Where the real
dependency is cheap and instant to use directly, most plain data transformations
for instance, this same pattern would be needless ceremony. It earns its place here
because the real thing is hard to get to, not as a default habit for every
dependency in the app.

In TypeScript the seam costs even less than in most languages: interfaces are
**structural**. Any object with the right methods *is* a `TreadmillService`; there's
no `implements` keyword required (though writing `class FakeTreadmillService
implements TreadmillService` is still worth it, because it makes the compiler point
at the exact missing method instead of failing somewhere far away).

### Learning goals

- Implementing an interface against a contract someone else already designed
  (`treadmillService.ts`), reading intent from doc comments, not just signatures.
- **Subscribe-returns-unsubscribe** as an event pattern: why this project uses it
  instead of an `EventEmitter` class or RxJS (see the design notes at the top of
  `treadmillService.ts`), and why it fits React's `useEffect` cleanup exactly.
- The **seam / fake pattern**: build a believable stand-in for something you don't
  have yet (real BLE data), so everything above it can be built and tested now.
- **Where code belongs, and why**: `treadmillService.ts` imports nothing from React,
  React Native or Expo, which is exactly the test for "does this belong in
  `src/core/`" (see `docs/learning/00-Expo-And-React-Native.md`). So
  `FakeTreadmillService` lives in `core/` too and gets real Jest tests, the same way
  `ftmsCommands` and the capture formatter do.
- **Jest fake timers**: testing something that ticks once a second without the test
  taking seconds.
- The **composition root** (`src/features/services.tsx`): the *only* place that will
  need to change when a real implementation replaces the fake one later.

### Reference docs

- **`./treadmillService.ts`**: read the whole file before writing anything.
  Every type you need (`ConnectionState`, `TreadmillSample`, `MachineEvent`,
  `TreadmillCapabilities`, `SpeedRange`, `clampSpeed`, `ControlResult`,
  `FtmsResultCode`, `AppErrorCode`, `TreadmillSimulation`, `SimulationScenario`) is
  already defined there, with doc comments explaining constraints.
- **`../phase-00-probe-app/PHASE-00-FINDINGS.md`**, section V2. The control-point
  finding matters here: `Start` does not appear to preserve a pre-set target speed on
  the real device. `FakeTreadmillService` doesn't have to reproduce this quirk, but
  decide *consciously* whether your fake should (see task 1b.2).
- **`../phase-00-probe-app/PHASE-00-FINDINGS.md`**: the speed range (1.0–16.0 km/h,
  0.1 increment) is a measured fact, safe to reuse as the fake's advertised range.
- **`src/core/shared/emitter.ts`** (from Phase 00): `createEmitter<T>()` gives you
  `emit(value)` and `subscribe(listener) → unsubscribe` in a dozen lines. Use it for
  the three event streams rather than writing listener arrays by hand.
- **`setInterval` / `clearInterval`**: the mechanism task 1b.2 uses to raise a sample
  roughly once a second. Whatever starts an interval must be able to stop it; an
  interval nobody clears keeps firing (and keeps the fake "connected") forever.
- **`AbortSignal`**: [AbortController (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/AbortController).
  The standard JavaScript way to say "give up on this async operation", used by
  `connect(deviceId, signal)`. Read it before 1b.2: `signal.aborted` and the
  `abort` event are the two things you'll touch.
- **Jest fake timers**: [Timer Mocks](https://jestjs.io/docs/timer-mocks). Read
  before 1b.3; `jest.useFakeTimers()` plus `await jest.advanceTimersByTimeAsync(ms)`
  is the whole trick.

### Your tasks

**1b.1: Move the contract into `core/`**

Creates: `src/core/treadmill/treadmillService.ts` (new `treadmill/` folder).

Concrete steps:
1. Create the folder `src/core/treadmill/`.
2. Move `treadmillService.ts` from this phase folder into it with a real move
   (`git mv phases/phase-01-protocol-decode/treadmillService.ts src/core/treadmill/`),
   not a retype. The content doesn't need to change.
3. Run the two checks that matter for anything in `core/`:
   ```powershell
   cd src
   npx tsc --noEmit
   npx expo lint
   ```
   Zero errors, zero warnings is the bar, same as every other phase. The lint run
   is what proves nothing React-Native-flavoured leaked in.

**1b.2: `FakeTreadmillService`**

Creates: `src/core/treadmill/fakeTreadmillService.ts`.

Implement `TreadmillService` fully. Instead of talking to real Bluetooth, generate a
synthetic session on a timer:

- Emit a sample roughly once a second with a `TreadmillSample` that changes
  realistically over time (speed ramping up, distance accumulating, elapsed time
  counting up). This is exactly what Phase 03's dashboard will render, so it needs
  to actually look like a workout, not just static numbers.
- Support at least the `normalWalk` scenario end to end: warm-up → steady speed →
  cool-down.
- `connect()` transitions state through `connecting → discovering → ready` with a
  short delay at each step, mirroring the shape of the real connection (see
  `src/features/bluetooth/treadmillConnection.ts` from Phase 00 for what the real
  state machine looks like) so nothing built against this fake is surprised later.
  `disconnect()` transitions `ready → disconnected` and stops the sample interval.
- `requestControl()` / `setSpeed()` / `start()` / `pause()` / `stop()` resolve to a
  plausible `ControlResult` and actually affect the simulated sample stream (e.g.
  `setSpeed()` changes what speed subsequent samples report). One design decision
  that's yours to make, informed by the Phase 00 finding above: should this fake also
  model "Start doesn't preserve target speed"? Either answer is defensible. Just make
  it a deliberate choice, and write a one-line comment saying which you picked and
  why.
- Populate the capabilities and the speed range once "connected."

The shape of the class, not the implementation. Just the skeleton so you're not
staring at a blank file:

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
  private sampleTimer: ReturnType<typeof setInterval> | null = null;
  private currentSpeedKph = 0;
  private distanceMeters = 0;

  private readonly stateChanges = createEmitter<ConnectionStateChange>();
  private readonly samples = createEmitter<TreadmillSample>();
  private readonly machineEvents = createEmitter<MachineEvent>();

  getConnectionState(): ConnectionState { return this.state; }
  // ... getCapabilities, getSpeedRange, hasControl

  onSample(listener: (sample: TreadmillSample) => void): Unsubscribe {
    return this.samples.subscribe(listener);
  }
  // ... onConnectionStateChange, onMachineEvent: same one-liner

  async connect(deviceId: string, signal?: AbortSignal): Promise<void> {
    // transition disconnected -> connecting -> discovering -> ready, with a short
    // delay at each step, emitting a state change each time; bail out (back to
    // 'disconnected') if signal?.aborted becomes true along the way
  }

  private tick(): void {
    // called by setInterval ~once/sec: advance currentSpeedKph / distanceMeters
    // per the normalWalk shape (warm-up -> steady -> cool-down), emit a sample
  }

  // disconnect, requestControl, setSpeed, start, pause, stop: same pattern:
  // mutate state, emit the relevant event, resolve a ControlResult
}
```

Deliberately **not required yet**: the other `SimulationScenario` values
(`dropoutMidSession`, `sparseFields`, `controlRejected`, `intervalWalk`) and a
counter-reset scenario. Get `normalWalk` solid first; we'll extend this file when a
later phase actually needs a specific scenario for a specific test, rather than
building five scenarios speculatively now.

**1b.3: Real Jest tests**

Creates: `src/core/treadmill/fakeTreadmillService.test.ts` and
`src/core/treadmill/clampSpeed.test.ts`.

At minimum: connecting reaches `ready`, `setSpeed()` clamps to the speed range
(through `clampSpeed`, which already exists in `treadmillService.ts` and has its own
logic worth testing directly too), and a listener subscribed with `onSample` after
`connect()` + `start()` actually receives at least one sample.

Concrete steps:
1. `clampSpeed.test.ts`: a `test.each` table covering min, max, below min, above max,
   mid-increment (6.47 → 6.5), and the floating-point case the doc comment mentions
   (`1.0 + 3 * 0.1` → exactly `1.3`). This one doesn't need a fake at all; it's
   testing a pure function directly.
2. `fakeTreadmillService.test.ts`, with `jest.useFakeTimers()` in a `beforeEach`:
   - `test('connect reaches ready', ...)`: start `connect()`, advance timers past the
     fake's delays, `await` it, expect `getConnectionState()` to be `'ready'`.
   - A test that subscribes with `onSample`, connects, starts, advances timers by
     ~3 s, and expects the listener to have been called at least twice.
   - A test that calls the unsubscribe function and proves the listener stops being
     called. A subscription that can't be cancelled is a memory leak waiting to
     happen in Phase 03.
3. Run `npm test`: all green, including Phase 00's existing tests (regression).

**1b.4: Wire it into the composition root**

Touches: `src/features/services.tsx`.

Create one `FakeTreadmillService` at module scope and expose it as `treadmill` on
the services object, typed as `TreadmillService` (the interface, not the class).
That type annotation is the whole point: every consumer sees only the contract, so
swapping in the real implementation later can't break them.

Concrete steps:
1. Open `services.tsx` and find the existing Phase 00 entries (the BLE client, the
   capture recorder); that's the pattern to copy.
2. Add `treadmill: TreadmillService` to the `Services` interface and
   `treadmill: new FakeTreadmillService()` to `createServices()`.
3. `npx tsc --noEmit`. A missing field shows up here at compile time, not at
   runtime, which is one thing this hand-rolled composition root does better than a
   DI container.

### Review checkpoint

Before Phase 03 starts building UI on top of this: the agent reviews
`fakeTreadmillService.ts` against `treadmillService.ts`'s doc comments (which encode
real constraints, e.g. "control methods resolve to a result rather than rejecting,"
"listeners run synchronously in subscription order"). Then we wire up something
tiny together, even just a button that connects and a line of text showing the latest
sample's speed, to *see* it work before Phase 03 builds a real screen on the
assumption that it does.

---

## Track 01a — Parsers & fixtures (blocked)

**Blocked by:** Probe Part C (four-plus matched console-vs-hex pairs) and C7 (counter
reset semantics) from `../phase-00-probe-app/HUMAN-RUNBOOK.md`. Neither has been
captured yet; only the control-point finding (V2) has.
**Unblocks:** the *real* `BleTreadmillService` (as opposed to 01b's fake) becoming
trustworthy. Nothing else is newly blocked on this beyond what it always blocked.

> Pure desk work once unblocked. No hardware in the loop. The hardware speaks in
> Phase 00's capture files; this track turns that hex into parsers provably correct
> against it.

### Understanding what you're building (read this before the tasks)

**The problem.** The treadmill sends a stream of raw bytes over Bluetooth,
meaningless without a decoder. A parser's job is translation: turn `"02 4B 00 A3
01 ..."` into `speedKph = 6.5, distanceMeters = 412`. That translation step is
where almost every real bug in a BLE app lives. The "dictionary" you're
translating against (the FTMS spec) is a *general* spec written for any fitness
machine, and this specific treadmill (a FitShow module, not a native FTMS
implementation) is already proven to deviate from it (see the `0x2ACC` finding
below that the feature flags over-claim). The goal is what this one device
actually does, verified against real captured bytes, not just the general spec on
paper.

**Why the parser can't be "read the bytes in order."** The naive, simplest-sounding
approach (read byte 0 as speed, byte 1 as distance, and so on at fixed positions)
is exactly what FTMS's flag-driven format forbids, and this isn't an arbitrary
rule. Different treadmill sessions send *different sets* of fields depending on
what the machine detected (a field is absent, not zero, when it's not being
reported), so a fixed-offset reader silently reads the wrong field the moment the
flag pattern changes. The correct approach (walk a cursor through the buffer,
consulting the flags bitmask to know what's present at each step) is more code
than fixed-offset reading, but it isn't optional complexity. It's the minimum
correct approach for a format designed to be variable-length. Where a fixed
format genuinely has no variability (like `0x2AD4`'s three-`uint16` speed range,
task 1a.3), this project reads it as plain fixed offsets. No cursor needed there,
because the extra machinery would buy nothing. The rule isn't "always parse
defensively," it's "match the actual shape of the data you have."

**The pattern, named plainly.** Length-validate-then-reject-malformed-input is an
application of a much older idea: **fail loudly and immediately at the boundary,
not silently deep inside the program** (sometimes called "fail fast"). The
tradeoff is a few extra lines per parser (check the byte count before trusting
any field), for a real payoff: a rejected packet shows up as one clear log line
at the exact moment something's wrong, instead of a `TreadmillSample` with
plausible-looking-but-wrong numbers silently corrupting a chart three screens
later. Uncle Bob's framing applies directly here. This isn't a rule to follow
blindly on every function; it's specifically valuable at a **trust boundary**
(bytes arriving from outside your program's control), which is exactly what a BLE
packet is. `DataView` helps in a specific way: reading past its end *throws*
(`RangeError`) instead of quietly returning garbage. Validating the length first
means that throw never happens in practice; if it ever does, it's a bug in your
length calculation, and it's loud.

### Reference docs

- The FTMS protocol reference below: `0x2ACC`, `0x2AD4`, `0x2ACD` and its three
  traps, `0x2A37`, `0x2ADA`, `0x2AD3`. (Control point response format, `0x2AD9`,
  lives in `../phase-05-treadmill-control/README.md` since that's the phase that
  writes to it.)
- **`DataView`**: [DataView (MDN)](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/DataView).
  `getUint8`, `getUint16(offset, true)`, `getInt16(offset, true)`. The `true` is
  little-endian and it is never optional in this project.
- `../phase-00-probe-app/PHASE-00-FINDINGS.md`: the measured truth
- `../../captures/`: raw sessions

---

## FTMS protocol reference

> **Status: PARTIALLY VERIFIED.** Static reads are done (see
> `../phase-00-probe-app/PHASE-00-FINDINGS.md`); the data stream and control
> point are not yet fully captured. Source of the protocol facts below: Bluetooth
> SIG *Fitness Machine Service* specification v1.0 and the GATT Specification
> Supplement. Where this reference states a fact with a confidence qualifier,
> that qualifier is meaningful — treat anything below "high" as needing
> confirmation from `PHASE-00-FINDINGS.md`.

### What this device actually is

The device advertises as **`FS-9F4235`**. `FS-` is FitShow (Xiamen) Information
Technology, who manufacture transparent-UART BLE modules for treadmills, exercise
bikes and rowers. This is confirmed by the presence of `FFE0` and `FFF0`, both
common transparent-serial service UUIDs, alongside the FTMS service.

```
treadmill motor board ←UART→ FitShow BLE module ←BLE→ phone
                                   │
                                   ├── FFE0 / FFF0   transparent serial
                                   │                  (what the FitShow app uses)
                                   ├── 180D          standard Heart Rate Service
                                   └── 1826          FTMS shim
```

**FTMS here is a vendor shim, not a native implementation.** That has one practical
consequence running through this entire reference: **verify against hex, trust nothing
the device declares about itself.** The `0x2ACC` section below documents a concrete case
where the device's own feature declaration is provably false.

The FitShow UART protocol on `FFE0`/`FFF0` is undocumented and prior public attempts
to decode it have not succeeded. It is **not** a fallback plan. It is recorded here
for completeness only.

### Reading this reference

FTMS is a **GATT profile**, not an HTTP API. There are no endpoints, no JSON on the
wire, and no request bodies. Every interaction is one of:

| GATT operation | What it is |
|----------------|------------|
| Read | Client reads a characteristic's current value (raw bytes) |
| Write | Client writes raw bytes to a characteristic |
| Notify | Server pushes raw bytes to the client, unacknowledged |
| Indicate | Server pushes raw bytes to the client, acknowledged |

All multi-byte integers are **little-endian**.

### Service and characteristics: VERIFIED

| UUID | Name | Used by this app |
|------|------|------------------|
| `1800` | Generic Access | No |
| `180A` | Device Information | Optional — firmware version |
| `180D` | **Heart Rate** | **Yes — preferred HR source, see below** |
| `FFE0` | Vendor (FitShow transparent serial) | No — recorded only |
| `FFF0` | Vendor (FitShow transparent serial) | No — recorded only |
| `1826` | **Fitness Machine** | **Yes — primary** |

### FTMS characteristics: VERIFIED PRESENT

| UUID | Name | Properties | Required by this app |
|------|------|------------|----------------------|
| `0x2ACC` | Fitness Machine Feature | Read | Advisory only — see below |
| `0x2ACD` | Treadmill Data | Notify | Yes — the core data stream |
| `0x2AD4` | Supported Speed Range | Read | Yes — drives all UI limits |
| `0x2AD9` | Fitness Machine Control Point | Write + Indicate | Speed control — see `../phase-05-treadmill-control/README.md` |
| `0x2ADA` | Fitness Machine Status | Notify | Yes — machine-initiated events |
| `0x2AD3` | Training Status | Read + Notify | Optional — display only |

All six are present. That is better than expected for a shim implementation, but
**presence is not function.** `0x2AD9` exposing Write and Indicate says nothing about
whether it honours commands — see the Phase 00 control-point verdict.

The app must still degrade gracefully: if the control point handshake fails, hide all
speed controls. Never fall back to guessed limits if `0x2AD4` is unreadable.

### Fitness Machine Feature (`0x2ACC`): Read

**8 bytes: two little-endian uint32 bitfields.**

```
[0..3]  Fitness Machine Features   (uint32 LE)
[4..7]  Target Setting Features    (uint32 LE)
```

**Fitness Machine Features (bytes 0–3):**

| Bit | Meaning |
|-----|---------|
| 0 | Average Speed supported |
| 1 | Cadence supported |
| 2 | Total Distance supported |
| 3 | Inclination supported |
| 4 | Elevation Gain supported |
| 5 | Pace supported |
| 6 | Step Count supported |
| 7 | Resistance Level supported |
| 8 | Stride Count supported |
| 9 | Expended Energy supported |
| 10 | Heart Rate Measurement supported |
| 11 | Metabolic Equivalent supported |
| 12 | Elapsed Time supported |
| 13 | Remaining Time supported |
| 14 | Power Measurement supported |
| 15 | Force on Belt and Power Output supported |
| 16 | User Data Retention supported |
| 17–31 | Reserved |

**Target Setting Features (bytes 4–7):**

| Bit | Meaning |
|-----|---------|
| 0 | **Speed Target Setting supported** ← the one that decides Phase 05 |
| 1 | Inclination Target Setting supported |
| 2 | Resistance Target Setting supported |
| 3 | Power Target Setting supported |
| 4 | Heart Rate Target Setting supported |
| 5 | Targeted Expended Energy configuration supported |
| 6 | Targeted Step Number configuration supported |
| 7 | Targeted Stride Number configuration supported |
| 8 | Targeted Distance configuration supported |
| 9 | Targeted Training Time configuration supported |
| 10–31 | Remaining bits are further targeted-value configurations and reserved |

*Confidence: high on bits 0–13 of machine features and bits 0–4 of target settings;
moderate on the higher bits, which this device almost certainly does not set anyway.*

#### ⚠️ THIS DEVICE'S FEATURE FLAGS ARE UNRELIABLE. DO NOT GATE ON THEM

Decoded from the first capture:

**Machine features claimed:** Total Distance, Step Count, Resistance Level, Expended
Energy, Heart Rate Measurement, Elapsed Time, Power Measurement

**Target features claimed:** Speed Target Setting, Inclination Target Setting,
Resistance Target Setting, Power Target Setting

Three of these are impossible:

| Claim | Reality |
|-------|---------|
| Resistance Level supported | Treadmills have no resistance mechanism |
| Power Measurement supported | This treadmill has no power meter |
| **Inclination Target Setting supported** | **The machine has no incline at all** |

The last one is decisive: inclination is **absent** from the machine-features word but
**present** in the target-settings word. A device cannot support setting a target for a
capability it does not report having. This is internally contradictory, which means the
bitmask is a stock value baked into FitShow module firmware rather than a description
of this treadmill.

Note also that the four claimed target bits are 0, 1, 2, 3 = `0x0000000F`, a
suspiciously round "all of them" value.

**Required handling (implemented in this phase, Phase 03 and Phase 05):**

- **Log `0x2ACC`, never branch on it.**
- **Dashboard fields** derive from the union of `0x2ACD` flag bits observed over the
  first ~30 seconds of a connection. A field is real if it arrives in packets.
- **Speed control** derives from the live control point handshake
  (`../phase-05-treadmill-control/README.md`), not from the Speed Target Setting bit,
  which is set, but carries little weight given the above.

*Confidence: high that the bitmask over-claims. The incline contradiction is direct
evidence.* Raw hex for `0x2ACC`, once captured, lives in
`../phase-00-probe-app/PHASE-00-FINDINGS.md` — use it as a parser fixture and to
confirm the decode above independently.

**Note on heart rate:** the HR feature bit means the machine can *report* HR, not that
a sensor is producing usable data. This treadmill has handgrip sensors, which only work
while gripped and are noisy when they do. See `0x2A37` below for the preferred source
and the decision rule.

### Supported Speed Range (`0x2AD4`): Read

**6 bytes, three uint16 LE values, all in units of 0.01 km/h.**

```
[0..1]  Minimum Speed      (uint16 LE, 0.01 km/h)
[2..3]  Maximum Speed      (uint16 LE, 0.01 km/h)
[4..5]  Minimum Increment  (uint16 LE, 0.01 km/h)
```

Example: `64 00 58 02 0A 00` → min 1.00 km/h, max 6.00 km/h, increment 0.10 km/h.

**VERIFIED:**

```
Minimum:   1.0 km/h
Maximum:  16.0 km/h
Increment: 0.1 km/h
```

This is a full folding treadmill, not a walking pad.

Still read this characteristic at runtime rather than hardcoding the values. `0x2AD4`
is the only authoritative source, and it costs one read.

Preset buttons should be generated from this range. Suggested set for 1.0–16.0:
2, 4, 6, 8, 10, 12 km/h, generated rather than hardcoded so the code survives a
firmware change or a different treadmill.

### Treadmill Data (`0x2ACD`): Notify

The primary data stream. **This is the single most error-prone parse in the project.**

**Structure:**

```
[0..1]  Flags (uint16 LE)
[2..]   Fields, in the fixed order below, present only if their flag bit is set
```

**The three traps:**

**Trap 1: bit 0 is inverted.** The FTMS spec defines bit 0 as *More Data*.
Instantaneous Speed is present when bit 0 is **`0`**, absent when it is `1`. This is
the opposite of every other bit in the field. Decoding it as a normal presence bit
shifts every subsequent field and corrupts the entire packet. This trips up most
first-time FTMS implementations.

**Trap 2: Total Distance is uint24.** Three bytes, little-endian. `DataView` has
`getUint16`/`getUint32` but no 24-bit reader; assemble it manually:

```ts
const distance = view.getUint16(i, true) | (view.getUint8(i + 2) << 16);
i += 3;
```

(`true` means little-endian. `DataView`'s default is big-endian, which is the
single easiest way to get every multi-byte field in this file wrong.)

**Trap 3: Expended Energy is one flag bit but three fields**, 5 bytes total:
Total Energy (uint16, kcal), Energy Per Hour (uint16, kcal), Energy Per Minute
(uint8, kcal). Advancing 2 bytes instead of 5 misaligns everything after it.

**Field table.** Fields appear in this order. Skip any whose flag bit is clear.

| Flag bit | Field | Type | Bytes | Resolution | Unit |
|----------|-------|------|-------|------------|------|
| 0 == 0 | Instantaneous Speed | uint16 | 2 | 0.01 | km/h |
| 1 | Average Speed | uint16 | 2 | 0.01 | km/h |
| 2 | Total Distance | **uint24** | **3** | 1 | m |
| 3 | Inclination | sint16 | 2 | 0.1 | % |
| 3 | Ramp Angle Setting | sint16 | 2 | 0.1 | degree |
| 4 | Positive Elevation Gain | uint16 | 2 | 0.1 | m |
| 4 | Negative Elevation Gain | uint16 | 2 | 0.1 | m |
| 5 | Instantaneous Pace | uint8 | 1 | 0.1 | km/min |
| 6 | Average Pace | uint8 | 1 | 0.1 | km/min |
| 7 | Total Energy | uint16 | 2 | 1 | kcal |
| 7 | Energy Per Hour | uint16 | 2 | 1 | kcal |
| 7 | Energy Per Minute | uint8 | 1 | 1 | kcal |
| 8 | Heart Rate | uint8 | 1 | 1 | bpm |
| 9 | Metabolic Equivalent | uint8 | 1 | 0.1 | — |
| 10 | Elapsed Time | uint16 | 2 | 1 | s |
| 11 | Remaining Time | uint16 | 2 | 1 | s |
| 12–15 | Reserved | | | | |

Bits 3 and 4 each gate **two** fields (4 bytes each). Bit 7 gates three (5 bytes).

*Confidence: high on bits 0, 1, 2, 7, 8, 10, these are the ones this device will
actually use. Moderate on the pace resolution (bits 5–6) and metabolic equivalent;
verify from `PHASE-00-FINDINGS.md` once captured.*

**Notification rate.** The FTMS specification states the server should notify
**approximately once per second**, and the interval is not configurable by the
client. If the measured rate (see `PHASE-00-FINDINGS.md`) is above 1 Hz, throttle UI
binding updates to 4 Hz — Phase 03 already specifies this. The fixed-cadence sample
storage this phase's schema uses is unaffected either way.

**Range limits (no rollover risk in practice):**

| Field | Max value | Real-world limit |
|-------|-----------|------------------|
| Elapsed Time (uint16) | 65,535 s | 18.2 hours |
| Total Distance (uint24) | 16,777,215 m | 16,777 km |
| Total Energy (uint16) | 65,535 kcal | not reachable in a session |

None of these will roll over in a single workout. **They may roll over or reset
across sessions** — see the counter reset semantics finding in
`../phase-00-probe-app/PHASE-00-FINDINGS.md` V1, which Phase 06's recording depends
on entirely.

**Parser requirements:**

- **Cursor-based and flag-driven. Never fixed-offset.**
- **Validate length before parsing:** compute the expected byte count from the flags
  and reject the packet if it doesn't match. Log rejected packets with their hex. Do
  not read past the buffer.
- Return a result, not a thrown exception, for a malformed packet: a rejected
  packet at 1 Hz is an expected event, and `try`/`catch` around every notification
  would bury real errors. A discriminated union (`{ ok: true, sample }` /
  `{ ok: false, reason, hex }`) makes the caller handle both.
- Treat every field as nullable in the domain model (`number | null`), because
  presence is per-packet, not per-device.

### Heart Rate Service (`180D`): the preferred HR source

The device exposes a **standard Heart Rate Service** in addition to the FTMS heart rate
field. Two sources for the same value.

**Characteristic `0x2A37`: Heart Rate Measurement, Notify**

```
[0]     Flags (uint8)
          bit 0:    0 = HR value is uint8, 1 = HR value is uint16
          bits 1-2: sensor contact status
          bit 3:    Energy Expended present
          bit 4:    RR-Interval present
[1..]   Heart Rate Measurement (uint8 or uint16 per bit 0)
[..]    Energy Expended (uint16, kJ) if flagged
[..]    RR-Intervals (uint16 each, 1/1024 s) if flagged
```

For a treadmill handgrip sensor, expect flags `0x00` and a single uint8, the simplest
possible case.

**Sensor contact status (bits 1–2)** is directly useful here: it distinguishes "user is
not gripping" from "reading is zero", which is exactly the ambiguity handgrip sensors
create.

| Value | Meaning |
|-------|---------|
| `0b00` / `0b01` | Contact detection not supported |
| `0b10` | Contact detection supported, **contact not detected** |
| `0b11` | Contact detection supported, contact detected |

*Confidence: high. This is a long-stable, widely-implemented standard service.*

**Why prefer this over the FTMS field.** A dedicated single-purpose characteristic is
far less likely to be mangled by a vendor shim than a conditionally-present field
buried inside a flag-driven FTMS record. If the shim gets any part of the `0x2ACD`
layout wrong, HR is one of the later fields and therefore among the first to be
corrupted.

**Rule:** use `0x2A37` if it produces usable data. Fall back to the FTMS field only if
`180D` is dead.

**Decision rule for whether HR ships at all.** Handgrip sensors only read while
gripped, and are noisy when they do. **If the capture shows sparse, implausible, or
wildly unstable values, cut heart rate from the dashboard and charts entirely.** A
metric that is wrong half the time is worse than no metric: it will pollute the
average and maximum HR columns in every stored workout. See the V3 verdict in
`../phase-00-probe-app/PHASE-00-FINDINGS.md`. Recording the field in `WorkoutSample`
while hiding it from the UI is a reasonable middle position if V3 said "marginal."

### Fitness Machine Status (`0x2ADA`): Notify

**This is an event stream, not a state string.** It reports *what just happened*,
typically as a result of user action on the machine itself. The app maintains its own
state; this feeds transitions into it.

```
[0]     Op Code (uint8)
[1..]   Parameters (op-code dependent, often absent)
```

| Op | Meaning | Parameters |
|----|---------|------------|
| `0x01` | Reset | — |
| `0x02` | Stopped or Paused by the user | uint8: `0x01` stop, `0x02` pause |
| `0x03` | Stopped by safety key | — |
| `0x04` | Started or resumed by the user | — |
| `0x05` | Target speed changed | uint16, 0.01 km/h |
| `0x06` | Target incline changed | sint16, 0.1 % |
| `0x07` | Target resistance level changed | — |
| `0x08` | Target power changed | — |
| `0x09` | Target heart rate changed | — |
| `0xFF` | Control permission lost | — |

*Confidence: high on `0x01`–`0x05` and `0xFF`; moderate on `0x06`–`0x09`, which are
not relevant to a treadmill without incline.*

**`0xFF` (control permission lost) matters:** on receiving it, the app must disable
speed controls and re-issue `Request Control` before re-enabling them — see
`../phase-05-treadmill-control/README.md` task 5.2.

**`0x03` (safety key) matters:** treat as an immediate hard stop. Do not attempt to
restart the machine over BLE in response.

If the device emits nothing here, the app must infer machine state from `0x2ACD`
speed values instead — workable, but less precise.

### Training Status (`0x2AD3`): Read + Notify

Distinct from both Machine Status and the app's workout state. Display-only.

```
[0]     Flags (uint8)
          bit 0: Training Status String present
          bit 1: Extended String present
[1]     Training Status (uint8)
[2..]   Training Status String (UTF-8), if flagged
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
| `0x09` | Speed Outside of Control Region — Low |
| `0x0A` | Speed Outside of Control Region — High |
| `0x0B` | Cool Down |
| `0x0C` | Watt Control |
| `0x0D` | Manual Mode |
| `0x0E` | Pre-Workout |
| `0x0F` | Post-Workout |

**Observed on this device at rest: `Idle` (`0x01`).** That at least means the
characteristic is populated rather than stubbed. Whether it transitions meaningfully
during a session is unverified.

*Confidence: high on the enum; low on whether this device implements it usefully.
Many budget machines report a single value permanently.*

**Do not drive the app's workout state machine from this characteristic.**

### Do not start until

- [ ] Raw hex for `0x2ACC` and `0x2AD4` exists
- [ ] At least four matched console-vs-hex pairs exist
- [ ] V1 (counter semantics) has an answer

Without these you are writing a parser you cannot test, which is the same as not
having one. If you have treadmill access again before this is done, running
`HUMAN-RUNBOOK.md` Part C is the highest-value fifteen minutes available.


### Tasks (once unblocked)

**1a.1: Extract fixtures from captures**

Creates: `src/core/ftms/fixtures/*.json`. Pull every distinct packet out of
`../../captures/` into named fixtures, each carrying its provenance (source file,
line `id`, timestamp, and, for `0x2ACD`, the paired console values). Include the
ugly ones: short packets, unexpected lengths, anything the log flagged. Jest imports
JSON directly (`import fixtures from './fixtures/treadmillData.json'`), so the
fixtures stay plain data a human can read.

**1a.2: `0x2ACD` Treadmill Data parser ← the one that matters**

Creates: `src/core/ftms/treadmillData.ts`, next to Phase 00's `ftmsCommands.ts` and
`controlPointResponse.ts`, since this is pure byte-parsing with no React Native
dependency and needs the same kind of fixture-based tests they got. Cursor-based,
flag-driven, never fixed-offset. The three traps (bit 0 inverted, uint24 distance,
5-byte expended energy) are documented in the "Treadmill Data" section of the FTMS
protocol reference above. Read it before writing this parser; it covers the
mistakes that otherwise get made here. Length validation is mandatory: reject and
log anything that doesn't match its flags, never read past the buffer.

The shape:

```ts
export type ParseResult<T> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false; readonly reason: string; readonly hex: string };

/** Decodes one 0x2ACD notification. Never throws on malformed input. */
export function parseTreadmillData(
  bytes: Uint8Array,
  receivedAtMs: number,
): ParseResult<TreadmillSample> {
  // TODO:
  // 1. bytes.length < 2 -> { ok: false, ... }
  // 2. const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  //    const flags = view.getUint16(0, true);
  // 3. compute the expected length from the flags (one helper, table-driven from
  //    the field table above) and reject if bytes.length differs
  // 4. walk a cursor from offset 2, field by field in table order
  // 5. return { ok: true, value: { receivedAtMs, speedKph: ..., ... } } with null for
  //    every field whose flag said "absent"
}
```

Two details worth knowing before you start. `new DataView(bytes.buffer, ...)` needs
the `byteOffset`/`byteLength` arguments because a `Uint8Array` can be a *window* onto
a larger buffer. And `bytes.byteOffset` is almost always `0` in this app, which is
exactly why forgetting it produces a bug that only shows up later.

**1a.3: Remaining parsers**

Creates, in `src/core/ftms/`: one file each for `0x2ACC` (`fitnessMachineFeature.ts`:
decode and expose, never branch on it), `0x2AD4` (`supportedSpeedRange.ts`: 3×
uint16 LE), `0x2ADA` (`machineStatus.ts`: op code + parameters, returning the
`MachineEvent` union), `0x2AD3` (`trainingStatus.ts`: display only), `0x2A37`
(`heartRateMeasurement.ts`: bit 0 selects uint8/uint16, bits 1–2 are sensor
contact). The control point response decoder already exists from Phase 00
(`controlPointResponse.ts`); reuse it, don't duplicate it.

**1a.4: Capability derivation**

Creates: `src/core/ftms/capabilityTracker.ts`. `0x2ACC` is advisory: log it, never
gate on it (see the "Capabilities (`0x2ACC`)" finding in
`../phase-00-probe-app/PHASE-00-FINDINGS.md` for why). Instead, accumulate the
union of observed `0x2ACD` flag bits over the first ~30 s after connecting; a
field is real if it arrives in packets. A pure class: feed it `(flags, receivedAtMs)`,
ask it `isFieldObserved('distance')`.

**1a.5: `BleTreadmillService` (real implementation)**

Creates: `src/features/treadmill/bleTreadmillService.ts`. This one belongs in
`features/`, not `core/`, because it depends on ble-manager (through Phase 00's
`bleClient.ts`, `treadmillConnection.ts` and `controlPointClient.ts`), which only
exists on a real phone. Implements `TreadmillService` (from `core/`) using the parsers
above. This is where 01b's fake gets a real sibling: same interface, same slot in
`services.tsx`, swapped in once this is done and reviewed. The swap is one line.

**1a.6: Update the protocol reference**

Touches: the "FTMS protocol reference" section above, and
`../phase-00-probe-app/PHASE-00-FINDINGS.md`. Replace every remaining blank in
`PHASE-00-FINDINGS.md` with a measured value or an explicit "not supported by
this device," and correct any confidence qualifier in the reference above that
the real capture contradicts. Anything still genuinely unknown stays recorded in
`PHASE-00-FINDINGS.md`'s "Still unresolved after this run" table, tagged with the
phase it blocks.

### Tests: this is where the project's real automated test suite lives

Everything else in this app is I/O and UI. These are the meaningful automated tests
(all in `src/core/ftms/*.test.ts`, run with `npm test`):

- Every fixture decodes to its expected values (`test.each` over the fixture file).
- Console-matched fixtures decode to the console's numbers, within rounding
  (`toBeCloseTo`): the test that proves correctness, not just non-crashing.
- Malformed input (truncated packet, length/flags mismatch, empty buffer): returns
  `{ ok: false }` with the hex, never throws, never reads out of bounds.
- Bit 0 specifically: More-Data-set vs. not, asserting the speed field's presence
  flips the way the spec says.
- uint24 distance at boundary values (`0x00FFFF`, `0x010000`).
- Expended Energy advancing exactly 5 bytes: assert a following field still lands
  correctly.
- A `Uint8Array` that's a window onto a bigger buffer (`new Uint8Array(buf, 3, 10)`)
  decodes the same as a fresh copy: the `byteOffset` trap from task 1a.2.

### Acceptance

- [ ] Every blank in `../phase-00-probe-app/PHASE-00-FINDINGS.md` resolved or marked unsupported
- [ ] Console-matched fixtures decode to the console's values at all captured speeds
- [ ] Malformed-input tests pass: nothing throws, everything logs
- [ ] `FakeTreadmillService` (from 01b) still produces a realistic stream and the
      real `BleTreadmillService` can be swapped in without any consumer changing
- [ ] `tsc` and lint clean; all Phase 00 tests still pass
