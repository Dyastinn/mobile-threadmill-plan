# React and TypeScript, explained properly

> You've used React and TypeScript in a working app. This covers what usually gets
> skipped when an app is built quickly: *why* the patterns work, and where they bite.
> Each section points to the phase that first uses it.
>
> How Expo works underneath is in [`00-Expo-And-React-Native.md`](00-Expo-And-React-Native.md).

## A component runs again on every redraw

```tsx
function SpeedLabel({ kph }: { kph: number | null }) {
  return <Text>{kph === null ? '' : kph.toFixed(1)}</Text>;
}
```

React calls this function whenever it needs to know what to show: when it first
appears, when its props or state change, or when its parent redraws. It returns a
*description* of the screen; React compares it with the last one and only changes what's
different.

Two things follow:

- **Anything you can work out from props or state, work out in the function.** For
  example, `showsSpeed = sample?.speedKph != null` belongs right there, not in a separate
  state kept in sync by an effect (Phase 03). A separate copy can drift out of sync.
- **Every variable and arrow function inside a component is new on every redraw.**
  Usually fine. Occasionally the cause of a bug (see "Stale values" and
  `useSyncExternalStore` below).

## `useState`: a value that redraws when it changes

```ts
const [sample, setSample] = useState<TreadmillSample | null>(null);
```

- `setSample(next)` doesn't change `sample` right away. It asks for a redraw, and in that
  redraw `sample` has the new value.
- If the new value depends on the old one, pass a function: `setCount((c) => c + 1)`.
- Setting it to the same value skips the redraw. A new object with the same contents
  doesn't: React compares *which object*, not what's inside.

## `useEffect`: connecting to things outside React

An effect connects a component to something that isn't React: a listener, a timer, a
native library. It runs *after* the screen has been updated.

```ts
useEffect(() => {
  const unsubscribe = treadmill.onSample((s) => setSample(s));
  return unsubscribe; // the cleanup
}, [treadmill]);
```

- **The cleanup** (the function you return) runs before the effect runs again, and when
  the component goes away. Forget it, and the listener keeps calling `setSample` on a
  screen that no longer exists: a leak, or worse. That's why every listener in this
  project returns its own "stop listening" function.
- **The dependency list** says when to run again: `[]` means once; `[treadmill]` means
  when `treadmill` changes (it never does, but listing it is honest, and lint expects
  it); no list means after every redraw, which you almost never want.
- **Strict Mode** (on in this project's development builds) starts every component,
  removes it, and starts it again. With a correct cleanup, you won't notice. Without
  one, you'll see everything happen twice in the logs. That's the point: you find the
  bug now, not in Phase 12's memory graph.
- **Async work in an effect:** the effect itself can't be `async` (it must return a
  cleanup, not a promise). Start the async work inside it, and don't update state if the
  component has gone in the meantime:

  ```ts
  useEffect(() => {
    let cancelled = false;
    history.getDailyCounts(from, to).then((days) => {
      if (!cancelled) setDays(days);
    });
    return () => {
      cancelled = true;
    };
  }, [history]);
  ```

  (Phase 03's calendar.)
- **You might not need an effect.** If you can work something out from props and state,
  do it in the component. Effects are for the outside world. React's guide "You Might
  Not Need an Effect" is worth reading once (link in [`03-Doc-Links.md`](03-Doc-Links.md)).

## `useRef`: a value that's kept but doesn't redraw

```ts
const lastUpdateMs = useRef(0);
// later, in a listener:
if (Date.now() - lastUpdateMs.current < 250) return;
lastUpdateMs.current = Date.now();
```

A ref is a box (`{ current: ... }`) React keeps between redraws. Changing it does *not*
redraw. Use it for things your code needs to remember but the screen doesn't show: the
throttle's last-update time (Phase 03), a timer, a previous value. If the screen shows
it, it's state. If only your code needs it, it's a ref.

## Stale values

```ts
useEffect(() => {
  const id = setInterval(() => console.log(count), 1000); // always logs the first count
  return () => clearInterval(id);
}, []);
```

The interval's function remembers `count` from the redraw where the effect ran, and with
`[]` that never updates. A classic bug. It's one reason this project keeps long-running
logic (timers, debouncing, retries) in plain classes in `core/` instead of hooks: a
class reads `this.snapshot.targetSpeedKph` at the moment the timer fires, so there's no
old copy (Phase 05's `SpeedController`).

## `useSyncExternalStore`: showing a value that lives outside React

React has a hook made for "a value a service owns, redraw when it changes":

```ts
const state = useSyncExternalStore(reconnection.subscribe, reconnection.getState);
```

It takes two functions:

- `subscribe(onChange)`: start listening, call `onChange` when the value might have
  changed, and return a function that stops listening.
- `getSnapshot()`: return the current value.

**The one rule:** `getSnapshot` must return *the same object* until something actually
changes. If it returns a new object every time (`return { ...this.state }`), React
thinks it changed on every redraw and redraws forever. That's why this project's classes
keep a `snapshot` field, replace it only when something changes, and return it (Phases
05 and 08). Also pass the *same* `subscribe` function every time, not a new arrow each
redraw. Writing `subscribe` and `getSnapshot` as arrow functions on the class handles
both.

## Context: getting the services to every screen

```ts
const { treadmill, workout, settings } = useServices();
```

`ServicesProvider` (Phase 00) puts the services at the top of the app, and
`useServices()` reads them from anywhere below. You could import the services file
directly and it would work. Context allows one useful thing: a test can give a screen
fake services instead.

## Lists and keys

```tsx
{presets.map((kph) => <SecondaryButton key={kph} label={kph.toFixed(0)} ... />)}
```

`key` is how React tells list items apart between redraws ("this one moved" vs. "this one
is new"). Use something unique and stable: a speed, a date, a `workoutId`. The position
in the list is only safe if the list never changes order. Long or growing lists use
`FlatList`, which only draws what's on screen (Phase 10's history). Short, fixed ones use
`.map()` (Phase 05's six presets).

## Promises in button presses

```tsx
<Button label="Stop" onPress={() => void controller.stop()} />
```

- `onPress` doesn't wait for a promise. `void` says that's on purpose.
- **A failed promise nobody handles** shows an "unhandled promise rejection" warning, and
  the error is lost. Either the async function handles its own errors (and shows them
  through state, like `statusMessage`), or the caller adds `.catch(...)`. Phase 02's retry
  loop is started without `await`, so it needs one.
- An `async` function always returns a promise, even with no `await` inside.

## TypeScript patterns used everywhere

### Lists of strings instead of enums

```ts
type ConnectionState = 'disconnected' | 'connecting' | 'discovering' | 'ready';
```

At runtime they're just strings (easy to read in logs, JSON and the database), and the
compiler rejects typos.

### Unions with a `kind` field, and complete `switch`es

```ts
type MachineEvent =
  | { kind: 'stoppedBySafetyKey' }
  | { kind: 'targetSpeedChanged'; kph: number }
  | { kind: 'unknown'; opCode: number };

switch (event.kind) {
  case 'targetSpeedChanged':
    console.log(event.kph); // TypeScript knows kph exists here
    break;
  case 'stoppedBySafetyKey':
    engine.tryStop();
    break;
  case 'unknown':
    break;
  default: {
    const unhandled: never = event; // stops compiling if a kind is missed
    return unhandled;
  }
}
```

Checking `kind` tells TypeScript which kind it is, so `event.kph` is only allowed where
it exists. The `never` line at the end is a safety net: if someone adds a new kind and
forgets this `switch`, the code stops compiling (Phase 04). Results work the same way:
`{ ok: true; value } | { ok: false; reason }` forces the caller to check (Phase 01a).

### `as const`: one line gives a value and a type

```ts
export const THEME_PREFERENCES = ['system', 'light', 'dark'] as const;
export type ThemePreference = (typeof THEME_PREFERENCES)[number]; // 'system' | 'light' | 'dark'

export const FtmsResultCode = { Success: 0x01, Timeout: 0xf0 } as const;
export type FtmsResultCode = (typeof FtmsResultCode)[keyof typeof FtmsResultCode]; // 0x01 | 0xf0
```

`as const` keeps the exact values instead of turning them into `string[]` or `number`.
The type then comes *from* the value, so the options on screen and the type a setter
accepts can never get out of sync (Phase 08). Using the same name for the value and the
type is allowed and normal.

### Type guards

```ts
function isThemePreference(value: string | null): value is ThemePreference {
  return value !== null && (THEME_PREFERENCES as readonly string[]).includes(value);
}
```

A function returning `value is T` tells the compiler what a check proved. After
`if (isThemePreference(saved))`, `saved` has the type `ThemePreference`. That's how
"read an untrusted string, fall back if it's junk" stays type-safe.

### `null` for "missing" in data

Every optional field in data (samples, database rows, backups) is `number | null`.
`null` matches SQLite's `NULL`, and survives `JSON.stringify` (which silently *drops*
`undefined` fields). `undefined` is fine for optional function arguments and props.

### `noUncheckedIndexedAccess`

Turned on in Phase 00. With it, `bytes[3]` has the type `number | undefined`, because the
index might be past the end. Slightly annoying in normal code, and exactly right for a
project that reads byte arrays all day: you can't forget the length check.

### Shapes, not names

TypeScript checks shapes: any object with the right methods *is* a `TreadmillService`.
Writing `class FakeTreadmillService implements TreadmillService` isn't required, but it
makes the compiler point at a missing method right at the class. `ReturnType<typeof
setTimeout>` is the type for a timer handle (a number on the phone, an object in Node).

## Numbers and bytes

- **Every JavaScript number is a decimal (floating point) number.** Whole numbers are
  exact up to very large values, so bytes and counters are fine. Decimals aren't:
  `0.1 + 0.2` is `0.30000000000000004`. That's why `clampSpeed` rounds before encoding
  (Phase 01b).
- **Bit operators work on 32-bit whole numbers.** `flags & 0x01`, `byte << 8` and `a | b`
  are what the decoders use. Every FTMS field fits.
- **`Uint8Array`** is a fixed-size array of bytes; Bluetooth values become one when they
  arrive. **`DataView`** reads bigger numbers out of it: `view.getUint16(offset, true)`.
  The `true` means little-endian (low byte first), which FTMS always is and `DataView`
  doesn't assume. Forgetting it is the easiest way to get every field wrong (Phase 01a).
- A `Uint8Array` can be a *window* into a bigger block of memory. Create the `DataView`
  with `(bytes.buffer, bytes.byteOffset, bytes.byteLength)`, never just `bytes.buffer`.

## Next

- [`00-Expo-And-React-Native.md`](00-Expo-And-React-Native.md), if you haven't read it.
- [`03-Doc-Links.md`](03-Doc-Links.md): React's pages on effects, `useSyncExternalStore`
  and "You Might Not Need an Effect" are the most useful three for this project.
- `phases/phase-01-protocol-decode/treadmillService.ts`: the interface most phases build
  on, and a short example of every TypeScript pattern above.
