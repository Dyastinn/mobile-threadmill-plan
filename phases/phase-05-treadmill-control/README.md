# Phase 05 — Treadmill Control

> **This phase may not exist.** Phase 00 verdict V2 decides. If the control point does
> not honour commands, skip to Phase 06. A read-only app is still worth shipping, and
> "void" here is a finding, not a failure.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 04 · **Gated on:** V2

---

## Goal

Change treadmill speed from the app, reliably. Everything here was already proven by
hand in the Phase 00 control console; this phase turns that into product UI.

### Understanding what you're building (read this before the tasks)

**The everyday problem.** `0x2ACC`'s "Speed Target Setting" bit is the treadmill's
own self-description of what it supports, and this device's self-description is
already caught lying elsewhere (see `../phase-01-protocol-decode/README.md`'s
"Capabilities" finding: the same bitmask claims incline support on a machine with
no incline). So this phase doesn't enable the speed controls because the bit says
so. It enables them only after actually performing the handshake: `Request Control`
returning success, then a real `Set Target Speed` returning success. Trust the
behaviour, not the label.

The Stop button follows the same instinct for a different reason. A Bluetooth
command over a radio link is not the same thing as the physical safety key. Task
5.3/5.4's confirmation dialog exists because that command is the only thing
standing between "belt on" and "belt off" from the app's side, and a half-second
"wait, yes, do it" before it fires is cheap insurance against a mis-tap. The
actual breaker is the treadmill's physical safety key, and the app is careful
never to let its own UI imply otherwise (hence never labelling it "Emergency
Stop").

**Why not simpler.** The naive, less-code version of gating is right there in the
spec: read `0x2ACC` once after connecting, and if the Speed Target Setting bit is
set, enable the controls. Zero extra round trips, one field read. This project
doesn't do that out of general caution, but because Phase 00 already measured this
exact bit lying about incline on this exact device. Trusting it for speed control
means trusting a source with a demonstrated failure on the very same message. The
actual approach costs one real round trip (`Request Control`, then a live `Set
Target Speed`) before any button is enabled, plus re-running that same check after
every reconnect and after any `controlPermissionLost` event, noticeably more code
than "check a bit." What it buys: the alternative failure mode is a treadmill that
shows working +/- buttons that silently do nothing when tapped, which is worse
than not showing them at all.

For the Stop confirmation, the naive alternative is equally obvious: no dialog,
tap Stop, command goes out immediately, matching how the +/- buttons already work
with no confirmation. The reason Stop specifically gets the extra step and
increase/decrease don't: a wrong tap on "+0.1" is trivially correctable with
one tap on "−0.1," but a stop command has no correctable undo. Belt state, in
a fitness-safety context, is exactly where this project's safety principle (task
5.4, below) draws the line for a deliberate second step, and nowhere else in this
phase.

**The pattern, named plainly.** The gating decision is **testing behaviour
instead of trusting a declared capability**: not a formal GoF pattern, but the
same instinct behind "don't trust a client-declared capability, verify it,"
applied to a BLE feature bitmask instead of, say, an HTTP header. Cost: an extra
async round trip before any control appears usable, and it has to be re-run on
every reconnect and permission loss, which is more state to track than a one-time
flag read. Payoff, specific to this device: it directly prevents the failure Phase
00 already proved exists, controls that look live but aren't. It would **not**
be worth it against a device whose self-reported capabilities had never been
caught lying; re-verifying a capability bit you have no concrete reason to
distrust is exactly the kind of defensive complexity Metz would cut.

The Stop confirmation is a plain application of **friction as a deliberate safety
cost**, not a named software design pattern. It costs one extra tap and one dialog
every single time, on purpose, because the payoff, never silently letting a mis-tap
interrupt the belt, is worth that friction specifically for a command with no undo.
The same reasoning does not extend to the +/- buttons, which stay frictionless
because a wrong tap there costs nothing.

**Where the logic lives.** All of this phase's decisions (debounce, clamping,
presets, gating, retry-once) go into a plain class in `core/`, `SpeedController`,
with a tiny hook on top. That's a deliberate shape, and it's the same one the
treadmill service and the workout engine already have: a class with getters, a
subscribe function and methods, and a hook that only adapts it to React. The payoff
is the tests in this phase: "tap + ten times, exactly one write" is a fake-timer test
in plain Node, with no React renderer involved.

## Learning goals

- **Debounce/coalesce as a reusable pattern.** Phase 04's grace timer was the first
  place this project used cancel-and-restart on a timer handle; this phase's tap
  debounce (task 5.1) is the same shape applied to rapid button taps instead of a
  connection drop. The point is to recognize it as one pattern, not learn it twice.
- **Gating UI on a live handshake, not a static feature bit.** `0x2ACC`'s Speed
  Target Setting bit is set on this device and is still not trustworthy. See
  `../phase-01-protocol-decode/README.md`'s "Capabilities" finding. This phase
  gates every control on the actual `Request Control` → `Set Target Speed` round
  trip instead.
- **A plain `.map()` for a short list, not `FlatList`.** Six preset buttons don't need
  virtualization; `FlatList` is for long lists where only the visible rows should
  exist. Knowing when each one is the right tool is the point.
- **Confirmation-before-destructive-action UI**, and specifically why this project
  uses friction instead of a color for it. There is no red in the monochrome theme,
  and even if there were, this project's safety principle (task 5.4) treats "Stop"
  as needing a deliberate second step regardless of color.
- **The "class + thin hook" shape** for logic that needs real tests.

## Reference docs

- The control point protocol reference below: commands, response format, result codes
- `../phase-00-probe-app/PHASE-00-FINDINGS.md` V2: the exact byte sequences that
  worked, with the operator's notes on what physically happened
- `docs/learning/04-Monochrome-Theme.md`: every token and component this phase's UI
  uses; read "Conveying state without color" before task 5.4, it's the direct source
  of the Stop button's design
- **`Alert`**: https://reactnative.dev/docs/alert. The mechanism behind the Stop
  confirmation in task 5.3.
- **`useSyncExternalStore`**: https://react.dev/reference/react/useSyncExternalStore,
  same as Phase 02; task 5.2's hook is that shape again.
- **Jest fake timers**: https://jestjs.io/docs/timer-mocks, for the debounce tests.

---

## Control point protocol reference: `0x2AD9`

The only writable FTMS characteristic. Governs speed control, start, and stop.
*Confidence: high. Opcodes `0x00`, `0x01`, `0x02`, `0x07`, `0x08` and the `0x80`
response format are well-established across FTMS implementations.*

### Mandatory sequence: omitting any step is the usual cause of silent failure

1. **Enable indications** on `0x2AD9` by writing `0x0002` to its CCCD descriptor
   (`0x2902`). Some machines reject writes if indications aren't enabled.
2. **Write `Request Control` (`0x00`)** and wait for a successful indication.
3. Only then send any other command.
4. **Re-issue `Request Control` after every reconnect** and after any `0xFF`
   (control permission lost) status event from `0x2ADA`.

### Write requirements

- Use **Write With Response**, not write-without-response.
- **Serialise commands.** One outstanding command at a time. Wait for the indication
  (3-second timeout) before sending the next. Concurrent writes get dropped or error.
- Debounce user input upstream so rapid +/- taps produce one write of the final
  target, not one per tap.

### Commands

| Command | Bytes | Notes |
|---------|-------|-------|
| Request Control | `00` | Must succeed first |
| Reset | `01` | |
| Set Target Speed | `02 LL HH` | uint16 LE, 0.01 km/h |
| Set Target Inclination | `03 LL HH` | sint16, 0.1 %: not applicable, this machine has no incline |
| Start or Resume | `07` | |
| Stop or Pause | `08 01` (stop) / `08 02` (pause) | **The parameter byte is mandatory**: `08` alone is malformed |

Set Target Speed example: 6.5 km/h → 650 → `0x028A` → bytes `02 8A 02`.

Other opcodes exist (`0x04` resistance, `0x05` power, `0x06` target heart rate,
`0x09`–`0x0D` targeted energy/steps/strides/distance/time). None apply to this app.

### Response format (arrives as an indication)

```
[0]     0x80  (Response Code, always)
[1]     Request Op Code (the command being answered)
[2]     Result Code
[3..]   Response parameters (rare)
```

| Result | Meaning | App behaviour |
|--------|---------|---------------|
| `0x01` | Success | Proceed |
| `0x02` | Op Code not supported | Disable that control permanently, log it |
| `0x03` | Invalid Parameter | Bug in clamping: log with the sent value |
| `0x04` | Operation Failed | Retry once, then surface to user |
| `0x05` | Control Not Permitted | Re-issue Request Control, then retry once |

These result codes are **machine-level and unrelated to the app-level
`AppErrorCode` values** used elsewhere in this project (see
`../phase-02-connection-hardening/README.md`). Do not merge the two numbering
schemes.

**Safety:** if `Start` works remotely, the app must not start the belt without an
explicit, deliberate user action on screen. Never send `Start` (`07`) from a
notification action, a restored state, or an auto-reconnect.

**Command order on this device (V2, preliminary):** Phase 00 found the belt doesn't
carry a pre-`Start` target speed into the running state. The working theory is
`Request Control → Start → Set Target Speed`, i.e. re-send the target speed *after*
Start. Confirm against the latest `PHASE-00-FINDINGS.md` before relying on it.

---

## Features

- Increase / decrease by the device's minimum increment (0.1 km/h)
- Preset speed buttons **generated from the range read from `0x2AD4`**, not hardcoded.
  For 1.0–16.0: 2, 4, 6, 8, 10, 12 km/h. Generated, so the code survives a firmware
  change or a different treadmill
- Stop

## Availability is decided by behaviour, not by a feature bit

The `Speed Target Setting` bit is set on this device, and the same bitmask claims
incline support on a machine with no incline, so it carries little weight. Gate on the
live handshake:

1. `0x2AD9` was discovered
2. `Request Control` (`00`) returned `0x01`
3. The first `Set Target Speed` returned `0x01`

Any step fails → disable speed controls **for the session**, log the result code, show
the user a plain explanation.

---

## Task 5.1 — `SpeedController` and its pure helpers

Creates: `src/core/treadmill/speedController.ts`,
`src/core/treadmill/controlHelpers.ts`.

This is logic. The agent describes the shape below, you write the real method
bodies, per the usual "you write it, the agent teaches" track from `../README.md`.

Concrete steps:

1. `controlHelpers.ts` gets the two pure functions everything else needs:
   ```ts
   /**
    * `count` evenly spaced speeds between range.minKph and range.maxKph, each passed
    * through clampSpeed so it lands on a real device increment. For 1.0-16.0 km/h
    * and count 6 this should land close to 2, 4, 6, 8, 10, 12.
    */
   export function generatePresets(range: SpeedRange, count = 6): number[] {
     // TODO
   }

   /** A plain-language sentence for every result code. Task 5.5 fills it in. */
   export function describeResult(code: FtmsResultCode): string {
     // TODO: a switch over code, with the exhaustive `never` default from Phase 04
   }
   ```
2. `speedController.ts` is the class the UI talks to. Keep the public shape as it is
   so the hook and the UI in tasks 5.2/5.3 work without edits; fill in every `TODO`.

```ts
export interface SpeedControlSnapshot {
  readonly targetSpeedKph: number;
  readonly canControl: boolean;
  readonly presetSpeeds: readonly number[];
  readonly statusMessage: string | null;
}

export class SpeedController {
  private snapshot: SpeedControlSnapshot = {
    targetSpeedKph: 0,
    canControl: false,
    presetSpeeds: [],
    statusMessage: null,
  };
  private debounceTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly changes = createEmitter<void>();
  private readonly unsubscribers: Unsubscribe[];

  constructor(private readonly treadmill: TreadmillService, private readonly debounceMs = 300) {
    this.unsubscribers = [
      treadmill.onConnectionStateChange((c) => this.onConnectionStateChange(c)),
      treadmill.onMachineEvent((e) => this.onMachineEvent(e)),
      treadmill.onSample((s) => this.onSample(s)),
    ];
  }

  /** Same object until something changes: useSyncExternalStore relies on that. */
  getSnapshot = (): SpeedControlSnapshot => this.snapshot;
  subscribe = (listener: () => void): Unsubscribe => this.changes.subscribe(listener);

  increase(): void {
    // TODO: const range = this.treadmill.getSpeedRange(); if null, return.
    // update targetSpeedKph to clampSpeed(range, target + range.incrementKph)
    // (via a small private `update(partial)` that replaces this.snapshot with a new
    // object and emits a change), then this.debounceSend()
  }

  decrease(): void {
    // TODO: mirror of increase, subtracting the increment instead
  }

  setPreset(kph: number): void {
    // TODO: same, with clampSpeed(range, kph): a preset outside the range must be
    // clamped, never sent as-is
  }

  async stop(): Promise<void> {
    // TODO: await this.treadmill.stop(), surface a failure via statusMessage.
    // No confirmation here: the confirmation already happened in the UI before
    // this method was called. See task 5.4.
  }

  dispose(): void {
    // TODO: clear the debounce timer, call every unsubscriber
  }

  private debounceSend(): void {
    // TODO: clear this.debounceTimer, then start a new setTimeout(debounceMs) that
    // calls this.treadmill.setSpeed(this.snapshot.targetSpeedKph). Same
    // cancel-and-restart shape as Phase 04's grace timer: reread that if this feels
    // unfamiliar. Read the target when the timer FIRES, not when it's scheduled,
    // or the "final value wins" guarantee breaks.
  }

  private async evaluateControlAvailability(): Promise<void> {
    // TODO: task 5.2: requestControl(), then canControl = result.success
  }

  private onConnectionStateChange(change: ConnectionStateChange): void {
    // TODO: on 'ready', set presetSpeeds from generatePresets(range) and call
    // evaluateControlAvailability() (with a .catch that logs). On 'disconnected',
    // canControl = false.
  }

  private onMachineEvent(event: MachineEvent): void {
    // TODO: task 5.2: on 'controlPermissionLost', canControl = false, then re-run
    // evaluateControlAvailability(). This is the spot Phase 04's WorkoutEngine left a
    // "// handled in Phase 05" comment pointing at.
  }

  private onSample(sample: TreadmillSample): void {
    // TODO: task 5.6: reconcile targetSpeedKph against sample.speedKph if you use
    // optimistic UI; revert the displayed value if the machine didn't comply.
  }
}
```

Why `getSnapshot` returns the *same object* until something changes:
`useSyncExternalStore` compares snapshots with `Object.is`. Return a fresh object on
every call and React thinks the store changed on every render, which is an infinite
render loop. Replacing `this.snapshot` with a new object *only* inside `update()` is
what makes it correct. It's the one rule of that hook worth memorising.

3. Create it in `services.tsx` as `speedControl`, next to `workout`.

---

## Task 5.2 — Control availability gating, and the hook

Fill in `evaluateControlAvailability` and the `onMachineEvent` stub.

Concrete steps:

1. On `'ready'`: `const result = await this.treadmill.requestControl()`.
2. `canControl = result.success` (equivalently,
   `result.code === FtmsResultCode.Success`).
3. If it failed, set `statusMessage` to `describeResult(result.code)`. Task 5.5 fills
   in the actual messages; wire the call in now.
4. Re-run this same evaluation after a `controlPermissionLost` `MachineEvent`. That's
   what `0x2ADA`'s `0xFF` op code means (see the "Fitness Machine Status" section of
   `../phase-01-protocol-decode/README.md`'s FTMS protocol reference), and controls
   must stay disabled until `Request Control` succeeds again.
5. The hook, `src/features/treadmill/useSpeedControl.ts`, is now three lines:
   read `speedControl` from `useServices()`, return
   `useSyncExternalStore(speedControl.subscribe, speedControl.getSnapshot)` together
   with the controller itself (so the UI can call `increase()` etc.).
   `subscribe`/`getSnapshot` are arrow-function properties on the class precisely so
   they're stable and already bound; that's why this is three lines and not ten.

---

## Task 5.3 — Speed control UI (written in full)

Creates: `src/features/treadmill/TreadmillControls.tsx`. This is UI: the full, working
code, ready to paste in. Adjust names only if your hook's return value ended up
different.

```tsx
import { Alert, View } from 'react-native';

import { useSpeedControl } from './useSpeedControl';
import { formatOneDecimal } from '@/features/shared/format';
import { Button } from '@/features/shared/theme/Button';
import { MetricTile } from '@/features/shared/theme/MetricTile';
import { SecondaryButton } from '@/features/shared/theme/SecondaryButton';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';
import { useTheme } from '@/features/shared/theme/useTheme';

export function TreadmillControls() {
  const { snapshot, controller } = useSpeedControl();
  const { colors } = useTheme();
  const disabled = !snapshot.canControl;

  function confirmStop() {
    Alert.alert(
      'Stop the belt?',
      'This sends a Bluetooth stop command. It is not the emergency stop: pull the physical safety key if you need the belt to stop immediately.',
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Stop', onPress: () => void controller.stop() },
      ],
    );
  }

  return (
    <View style={{ gap: spacing.lg, opacity: disabled ? 0.5 : 1 }}>
      <MetricTile value={formatOneDecimal(snapshot.targetSpeedKph)} unit="km/h target" />

      <View style={{ flexDirection: 'row', justifyContent: 'center', gap: spacing.md }}>
        <Button
          label={'−'}
          onPress={() => controller.decrease()}
          disabled={disabled}
          accessibilityLabel="Decrease speed"
          style={{ width: 72 }}
        />
        <Button
          label="+"
          onPress={() => controller.increase()}
          disabled={disabled}
          accessibilityLabel="Increase speed"
          style={{ width: 72 }}
        />
      </View>

      <Text variant="caption">Presets</Text>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm }}>
        {snapshot.presetSpeeds.map((kph) => (
          <SecondaryButton
            key={kph}
            label={kph.toFixed(0)}
            onPress={() => controller.setPreset(kph)}
            disabled={disabled}
            accessibilityLabel={`Set speed to ${kph} kilometres per hour`}
          />
        ))}
      </View>

      <View style={{ height: 1, backgroundColor: colors.border, marginVertical: spacing.xs }} />

      {/* Stop: never call this an emergency stop. See task 5.4. */}
      <Button label="Stop" onPress={confirmStop} disabled={disabled} />
      <Text variant="caption" style={{ textAlign: 'center' }}>
        The physical safety key is the emergency stop. This button sends a Bluetooth command and may not respond instantly.
      </Text>

      {snapshot.statusMessage !== null && <Text variant="caption">{snapshot.statusMessage}</Text>}
    </View>
  );
}
```

Two things worth understanding, not just pasting:

- **`snapshot.presetSpeeds.map(...)`, not a `FlatList`.** Six buttons don't need
  virtualization. `FlatList` earns its complexity when a list is long enough that
  rendering every row up front would be slow (Phase 00's notification log is one);
  here it would only add props to get wrong.
- **`key={kph}`.** React uses keys to match list items between renders. The speed
  itself is unique within the list, so it's the right key; an index would also work
  here only because the list never reorders.
- **`void controller.stop()`.** `onPress` doesn't want a promise back. `void` says "I'm
  deliberately not awaiting this," and `stop()` already reports failure through
  `statusMessage`, so nothing is lost.

Embed it in Phase 03's dashboard: add `<TreadmillControls />` below the metric tiles
in `DashboardScreen.tsx`.

---

## Task 5.4 — Why the Stop confirmation, specifically

This isn't decoration. This is this project's standing safety principle, also
called out in `docs/learning/04-Monochrome-Theme.md`'s "Conveying state without
color" table: **"Emergency Stop" was in an earlier draft and was rejected.** A
Bluetooth command over an unreliable radio link is not an emergency stop; the physical
safety key on the treadmill is. Two consequences, both already in the code above:

- The button is labelled **"Stop"**, never "Emergency Stop" or anything implying it's
  the safety mechanism.
- The confirmation dialog's message states the safety key exists and what it's for,
  every time, not just once in a settings screen the user will forget.

The confirmation step itself is the answer to "how do you make Stop feel different
from every other button without a red color to lean on." This theme has no hue to
spend on danger, so the friction of a deliberate second tap carries that weight
instead. That's the same reasoning that keeps `start()` behind "a deliberate on-screen
user action" in `TreadmillService`'s doc comments, never a notification action or
restored state.

Nothing further to implement here: task 5.3's `confirmStop` already is this
requirement. This task exists so the reasoning isn't just implicit in code you pasted.

---

## Task 5.5 — Result code messages

Fill in `describeResult` from task 5.1.

Concrete steps:

1. One sentence per code:
   ```ts
   switch (code) {
     case FtmsResultCode.Success: return 'OK';
     case FtmsResultCode.OpCodeNotSupported: return "This treadmill doesn't support that command.";
     case FtmsResultCode.InvalidParameter: return "That speed isn't valid for this treadmill.";
     case FtmsResultCode.OperationFailed: return 'The treadmill rejected that. Try again.';
     case FtmsResultCode.ControlNotPermitted: return 'Control was lost. Reconnecting control...';
     case FtmsResultCode.Timeout: return "The treadmill didn't respond in time.";
     case FtmsResultCode.NotConnected: return 'Not connected to the treadmill.';
     case FtmsResultCode.NotSupported: return "Speed control isn't available on this device.";
     default: { const unhandled: never = code; return unhandled; }
   }
   ```
   `Control Not Permitted` and `Invalid Parameter` genuinely need different messages:
   one tells the user to wait, the other means a bug in your clamping logic sent a
   value outside the device's range (log the actual value sent, per the "Response
   format" table in the control point reference above).
2. On `ControlNotPermitted` specifically (result code `0x05`): re-issue
   `requestControl()` and retry the original command once before giving up and
   showing the message. This is the "re-issue Request Control, then retry once"
   behaviour from the Implementation requirements below.

---

## Task 5.6 — Reconcile against `0x2ACD`

Fill in `onSample`.

Concrete steps:

1. If you implemented optimistic UI (updating `targetSpeedKph` immediately on tap,
   before the control point confirms), compare each incoming `sample.speedKph`
   against what you expect a few seconds after a command was sent.
2. If the machine's reported speed never approaches the target you set, revert
   `targetSpeedKph` to the last confirmed value and set `statusMessage` accordingly.
   Don't leave the UI claiming a speed the belt never reached.
3. This is a judgement call on exact timing/tolerance. Discuss the approach at the
   review checkpoint rather than guessing a magic number alone.

---

## Implementation requirements

- **Debounce and coalesce.** Rapid +/- taps produce **one** write of the final target,
  not one per tap. ~300 ms debounce, task 5.1's `debounceSend`.
- Clamp to the `0x2AD4` range; round to the device increment. `clampSpeed` already
  exists in `treadmillService.ts` and has its own unit tests from Phase 01b.
- **Serialise writes.** One outstanding command, wait for the indication (3 s timeout)
  before the next. This is the `TreadmillService` implementation's job (Phase 01a's
  `BleTreadmillService`, through Phase 00's `controlPointClient.ts`), not this
  controller's. The controller just calls `setSpeed`/`stop` and trusts the service to
  serialise underneath.
- Surface failures with the **actual result code meaning**, task 5.5.
- `0x05 Control Not Permitted` → re-issue `Request Control`, then retry once, task 5.5.
- `0xFF` on `0x2ADA` → disable controls, re-request control before re-enabling,
  task 5.2.
- **Re-issue `Request Control` after every reconnect** before enabling controls,
  task 5.2's `onConnectionStateChange`.
- Optimistic UI is fine, but **reconcile against the next `0x2ACD` notification** and
  revert if the machine did not comply, task 5.6.
- If V2 found control permission expires after idle, refresh it before a command
  rather than letting the first tap after a pause fail.

---

## Safety — not pedantry

"Emergency Stop" was in an earlier draft. **Do not name it that.** A Bluetooth stop
command over an unreliable link is not an emergency stop; the physical safety key is.

- Label it **"Stop"**.
- State in the UI that the safety key is the emergency stop.
- Never send `Start` (`07`) without a deliberate on-screen user action. **Not** from a
  notification action, **not** from restored state, **not** from auto-reconnect.

---

## Tests

| Test | Expected | |
|------|----------|---|
| Increase speed | Belt actually speeds up | `[HUMAN]` |
| Decrease speed | Belt slows | `[HUMAN]` |
| Rapid 10× tap on + | **One** write; final speed correct | `[HUMAN]` |
| Set speed below minimum | Clamped, no error | |
| Set speed above maximum | Clamped, no error | |
| Stop | Belt stops | `[HUMAN]` |
| Stop tapped, dialog cancelled | No command sent | `[HUMAN]` |
| Control without Request Control | Fails gracefully, clear message | |
| Set speed after reconnect | Works: control re-requested | `[HUMAN]` |
| Result code 0x05 handling | Re-requests control, retries once | |
| Indication timeout | Surfaces after 3 s, does not hang the queue | |

Automated tests, creates `src/core/treadmill/speedController.test.ts` and
`controlHelpers.test.ts`, with `jest.useFakeTimers()`:

1. Using `FakeTreadmillService` (connected): call `increase()` ten times in a row,
   advance timers by 300 ms, expect exactly one `setSpeed` call to have reached the
   fake (`jest.spyOn(fake, 'setSpeed')`) and the final `targetSpeedKph` to be the
   starting value plus ten increments.
2. `setPreset(99)` with the fake's 1–16 range: expect `16`, not `99`, to be sent.
3. `generatePresets` as a `test.each` against a couple of different ranges (not just
   1.0–16.0): every preset inside the range and on a valid increment.
4. `describeResult`: one assertion per `FtmsResultCode` value. (The exhaustive `never`
   already guarantees a new code can't be silently missed at compile time; the test
   guarantees none returns an empty string.)
5. `getSnapshot()` returns the identical object (`toBe`) when nothing changed, and a
   new one after `increase()`. That's the `useSyncExternalStore` rule, tested.
6. `npm test`.

## Acceptance

- [ ] 20 consecutive speed changes succeed
- [ ] No command ever sent outside the device's range
- [ ] Rapid tapping produces one write
- [ ] Nothing in the app can start the belt without a deliberate on-screen tap
- [ ] Stop requires confirmation and is never labelled "Emergency Stop"
