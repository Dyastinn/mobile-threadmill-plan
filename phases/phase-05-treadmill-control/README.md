# Phase 05 — Speed Control

> **This phase might not happen.** Finding V2 decides. If the treadmill ignores
> commands, skip to Phase 06. An app that only reads data is still worth having.

**Treadmill needed:** yes · **Size:** M · **Waits for:** Phase 04 · **Depends on:** V2

---

## Goal

Change the treadmill's speed from the app, reliably. Phase 00's console already proved
the commands by hand; this phase turns them into real buttons.

### The idea

**Only trust what actually works.** The treadmill's feature list (`0x2ACC`) says it
supports speed control, but the same list falsely claims an incline. So the speed
buttons are only enabled after a real test: `Request Control` succeeds, then a real
`Set Target Speed` succeeds. Otherwise you could end up with buttons that look fine and
do nothing, which is worse than no buttons.

That costs one real round trip before the buttons work, repeated after every reconnect
and every "control lost" event. Worth it here because this exact device is known to
lie.

**Stop asks "are you sure?"** A wrong tap on + is fixed with one tap on −. A wrong
Stop can't be undone mid-stride. So Stop, and only Stop, gets a confirmation. It's
never called "Emergency Stop": a Bluetooth command isn't one. The physical safety key
is.

**Where the logic lives.** All the decisions here (waiting for taps to settle,
clamping, presets, enabling, retrying) go in a plain class in `core/`,
`SpeedController`, with a tiny hook on top. That's the same shape as the treadmill
service and the workout engine. It means "tap + ten times, exactly one command sent"
can be tested on the PC without React.

## What you'll learn

- **Debounce**: wait until the taps stop, then send once. It's the same
  cancel-and-restart timer as Phase 04's grace timer, used for a different job.
- **Enabling buttons from a real test**, not a feature flag.
- **`.map()` for a short list, `FlatList` for a long one.** Six preset buttons don't
  need a `FlatList`.
- **Confirming before something you can't undo**, without a red color.
- **The "class + thin hook" shape**, for logic that needs real tests.

## Read first

- The control point reference below
- `PHASE-00-FINDINGS.md` V2: the exact bytes that worked, and what the belt did
- [`04-Monochrome-Theme.md`](../../docs/learning/04-Monochrome-Theme.md), especially
  "Conveying state without color": it's why Stop looks the way it does
- **`Alert`**: https://reactnative.dev/docs/alert (the Stop confirmation)
- **`useSyncExternalStore`**: https://react.dev/reference/react/useSyncExternalStore
- **Jest fake timers**: https://jestjs.io/docs/timer-mocks

---

## Control point reference: `0x2AD9`

The only FTMS characteristic you write to. It controls speed, start and stop.
*Confidence: high.*

### Required order (skipping a step usually means commands silently do nothing)

1. **Turn on replies** for `0x2AD9` (write `0x0002` to its `0x2902` descriptor).
2. **Send `Request Control` (`0x00`)** and wait for success.
3. Then send other commands.
4. **Send `Request Control` again after every reconnect**, and after a `0xFF` ("control
   lost") event from `0x2ADA`.

### How to write

- **Write With Response**, never without.
- **One command at a time.** Wait for the reply (up to 3 seconds) before the next.
- Debounce taps, so ten quick taps on + send one command with the final speed.

### Commands

| Command | Bytes | Notes |
|---------|-------|-------|
| Request Control | `00` | Must succeed first |
| Reset | `01` | |
| Set Target Speed | `02 LL HH` | Hundredths of km/h, low byte first |
| Set Target Inclination | `03 LL HH` | Not used: no incline |
| Start or Resume | `07` | |
| Stop or Pause | `08 01` (stop) / `08 02` (pause) | **The second byte is required** |

Example: 6.5 km/h → 650 → `0x028A` → `02 8A 02`.

Other commands exist (resistance, power, heart rate targets...). None apply here.

### The reply (an indication)

```
[0]     0x80  (always)
[1]     The command it's answering
[2]     Result code
[3..]   Extra values (rare)
```

| Result | Meaning | What the app does |
|--------|---------|---------------|
| `0x01` | Success | Carry on |
| `0x02` | Command not supported | Disable that button for good, log it |
| `0x03` | Invalid value | A clamping bug: log the value sent |
| `0x04` | Operation failed | Retry once, then tell the user |
| `0x05` | Control not allowed | Request control again, then retry once |

These are the treadmill's codes. They're separate from the app's own error codes
(Phase 02). Don't mix the two lists.

**Safety:** never send `Start` (`07`) without a deliberate tap. Not from a
notification, not when the app reopens, not after a reconnect.

**Command order on this treadmill (V2, early finding):** the belt seems to forget a
speed set before `Start`. Current theory: `Request Control → Start → Set Target Speed`.
Check the latest findings before relying on it.

---

## Features

- + and − buttons, one step each (0.1 km/h)
- Preset speed buttons **made from the speed range (`0x2AD4`)**, not hardcoded. For
  1.0–16.0: 2, 4, 6, 8, 10, 12 km/h
- Stop

## When the buttons are enabled

Only when all three are true:

1. `0x2AD9` exists
2. `Request Control` (`00`) returned `0x01`
3. The first `Set Target Speed` returned `0x01`

If any step fails: disable the speed buttons for this session, log the code, and show a
plain explanation.

---

## Task 5.1 — `SpeedController` and its helpers

Creates: `src/core/treadmill/speedController.ts`, `src/core/treadmill/controlHelpers.ts`.

1. `controlHelpers.ts`, two plain functions:
   ```ts
   /** `count` speeds spread across the range, each on a real step. 1–16 km/h → about 2, 4, ... 12. */
   export function generatePresets(range: SpeedRange, count = 6): number[] {
     // TODO: spread evenly, pass each through clampSpeed
   }

   /** A plain sentence for each result code. Filled in by task 5.5. */
   export function describeResult(code: FtmsResultCode): string {
     // TODO: a switch, with the `never` check from Phase 04 at the end
   }
   ```
2. `speedController.ts`: the class the screen uses. Keep the public parts as shown, so
   the hook and screen work unchanged.

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

  // Returns the same object until something changes (see the note below).
  getSnapshot = (): SpeedControlSnapshot => this.snapshot;
  subscribe = (listener: () => void): Unsubscribe => this.changes.subscribe(listener);

  increase(): void {
    // TODO: target + one step (clampSpeed), then debounceSend()
  }

  decrease(): void {
    // TODO: target − one step
  }

  setPreset(kph: number): void {
    // TODO: clampSpeed(range, kph). A preset outside the range is clamped, never sent as is
  }

  async stop(): Promise<void> {
    // TODO: treadmill.stop(); show a failure in statusMessage.
    // No confirmation here: the screen already asked.
  }

  dispose(): void {
    // TODO: clear the timer, call every unsubscriber
  }

  private update(changes: Partial<SpeedControlSnapshot>): void {
    // TODO: this.snapshot = { ...this.snapshot, ...changes }; then emit a change
  }

  private debounceSend(): void {
    // TODO: clear the old timer, start a new one. When it fires, send
    // this.snapshot.targetSpeedKph. Read the target when it fires, not when it's set.
  }

  private async evaluateControlAvailability(): Promise<void> {
    // TODO: task 5.2
  }

  private onConnectionStateChange(change: ConnectionStateChange): void {
    // TODO: on 'ready': fill presetSpeeds, then evaluateControlAvailability() (with .catch).
    // On 'disconnected': canControl = false.
  }

  private onMachineEvent(event: MachineEvent): void {
    // TODO: task 5.2. This is what Phase 04's "handled in Phase 05" comment points at.
  }

  private onSample(sample: TreadmillSample): void {
    // TODO: task 5.6
  }
}
```

**Why `getSnapshot` must return the same object until something changes.** React's
`useSyncExternalStore` compares snapshots. If you return a new object every time, React
thinks something changed on every redraw, and redraws forever. So only `update()`
creates a new snapshot object. This is the one rule of that hook worth memorising.

3. Add it to the services file as `speedControl`.

---

## Task 5.2 — Enabling the buttons, and the hook

1. On `ready`: `const result = await this.treadmill.requestControl()`.
2. `canControl = result.success`.
3. If it failed: `statusMessage = describeResult(result.code)`.
4. Run the same check again after a `controlPermissionLost` event (`0xFF`). Buttons stay
   disabled until control succeeds again.
5. The hook, `src/features/treadmill/useSpeedControl.ts`: get `speedControl` from
   `useServices()`, and return
   `useSyncExternalStore(speedControl.subscribe, speedControl.getSnapshot)` as
   `snapshot`, plus the controller itself as `controller`. Three lines.
   `subscribe` and `getSnapshot` are arrow functions on the class, so they never change;
   that's why it's this short.

---

## Task 5.3 — The controls (given)

Creates `src/features/treadmill/TreadmillControls.tsx`:

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
        <Button label={'−'} onPress={() => controller.decrease()} disabled={disabled} accessibilityLabel="Decrease speed" style={{ width: 72 }} />
        <Button label="+" onPress={() => controller.increase()} disabled={disabled} accessibilityLabel="Increase speed" style={{ width: 72 }} />
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

      <Button label="Stop" onPress={confirmStop} disabled={disabled} />
      <Text variant="caption" style={{ textAlign: 'center' }}>
        The physical safety key is the emergency stop. This button sends a Bluetooth command and may not respond instantly.
      </Text>

      {snapshot.statusMessage !== null && <Text variant="caption">{snapshot.statusMessage}</Text>}
    </View>
  );
}
```

Worth knowing:

- **`.map()`, not `FlatList`.** Six buttons don't need a list that only draws what's
  visible. `FlatList` is for long lists (like Phase 00's notification log).
- **`key={kph}`**: each speed is unique, so it's a good key.
- **`void controller.stop()`**: `onPress` doesn't wait for a promise. `void` says that's
  on purpose. `stop()` reports failures through `statusMessage`, so nothing is lost.

Add `<TreadmillControls />` to the dashboard, under the numbers.

---

## Task 5.4 — Why Stop is labelled and confirmed this way

The code above already does this; this section is just the reasoning.

- It's labelled **"Stop"**, never "Emergency Stop". A command over a radio link isn't an
  emergency stop. The safety key is.
- The confirmation says, every time, that the safety key is the emergency stop.
- The theme has no red, so the extra tap is what makes Stop feel different. Same reason
  `start()` is only ever called from a deliberate tap.

---

## Task 5.5 — Result messages

Fill in `describeResult`:

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

"Control not allowed" and "Invalid value" need different messages: the first means
wait, the second means your clamping sent a bad value (log the value).

On `ControlNotPermitted` (`0x05`): request control again and retry the command once
before showing the message.

---

## Task 5.6 — Check against the actual speed

Fill in `onSample`.

1. If the target changes on screen immediately when you tap (before the treadmill
   confirms), compare it with the speed the treadmill reports a few seconds later.
2. If the belt never gets near the target, put the target back to the last confirmed
   value and say so in `statusMessage`. Don't show a speed the belt never reached.
3. How long to wait and how close counts as "reached" is a judgement call. Decide it
   with the agent at the review.

---

## Rules

- **Debounce:** ten quick taps send **one** command. ~300 ms.
- Clamp to the `0x2AD4` range and round to the step (`clampSpeed`).
- **One command at a time** is the treadmill service's job (Phase 01a), not this class's.
  This class just calls `setSpeed`/`stop`.
- Show the real meaning of a result code (task 5.5).
- `0x05` → request control again, retry once.
- `0xFF` from `0x2ADA` → disable buttons, request control again.
- **Request control after every reconnect** before enabling buttons.
- Showing the new target straight away is fine, but **check it against the next samples**
  and undo it if the belt didn't follow (task 5.6).
- If V2 found control expires after a while idle, renew it before sending a command.

---

## Safety

"Emergency Stop" was in an earlier draft. **Never call it that.**

- Label it **"Stop"**.
- Say on screen that the safety key is the emergency stop.
- Never send `Start` (`07`) without a deliberate tap. **Not** from a notification,
  **not** when the app reopens, **not** after a reconnect.

---

## Tests

| Test | Expected | |
|------|----------|---|
| + | Belt speeds up | treadmill |
| − | Belt slows | treadmill |
| Tap + ten times fast | **One** command; final speed right | treadmill |
| Speed below minimum | Clamped, no error | |
| Speed above maximum | Clamped, no error | |
| Stop | Belt stops | treadmill |
| Stop, then Cancel | Nothing sent | treadmill |
| Command without Request Control | Fails cleanly, clear message | |
| Set speed after a reconnect | Works: control requested again | treadmill |
| Result 0x05 | Requests control, retries once | |
| No reply | Times out after 3 s, the queue keeps going | |

Automated tests: `src/core/treadmill/speedController.test.ts` and
`controlHelpers.test.ts`, with `jest.useFakeTimers()`:

1. With a connected fake: call `increase()` ten times, advance 300 ms, expect exactly one
   `setSpeed` call (`jest.spyOn(fake, 'setSpeed')`), with the start speed plus ten steps.
2. `setPreset(99)` with a 1–16 range sends `16`.
3. `generatePresets` with a few different ranges: every preset is in range and on a step.
4. `describeResult`: one check per code; none returns an empty string.
5. `getSnapshot()` returns the same object (`toBe`) when nothing changed, and a new one
   after `increase()`.
6. `npm test`.

## Done when

- [ ] 20 speed changes in a row work
- [ ] No command is ever sent outside the treadmill's range
- [ ] Fast tapping sends one command
- [ ] Nothing can start the belt without a deliberate tap
- [ ] Stop asks first and is never called "Emergency Stop"
