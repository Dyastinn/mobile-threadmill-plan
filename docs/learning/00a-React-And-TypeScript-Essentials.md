# React and TypeScript essentials, for someone who's used them by feel

> You've written React and TypeScript before, in an app that works. This doc is the
> part that usually gets skipped when an app is built quickly: *why* the patterns
> work, and the specific places they bite. Every section is grounded in code this
> project actually writes, with the phase that first uses it.
>
> How the platform underneath works (Expo, native modules, background) is in
> [`00-Expo-And-React-Native.md`](00-Expo-And-React-Native.md).

## A component is a function that runs again on every render

```tsx
function SpeedLabel({ kph }: { kph: number | null }) {
  return <Text>{kph === null ? '' : kph.toFixed(1)}</Text>;
}
```

React calls this function every time it needs to know what the screen should look
like: when the component first appears, when its props change, when its state
changes, or when its parent re-renders. It returns a *description* of the UI; React
compares it with the last description and changes only what differs on the real
screen.

Two consequences this project leans on:

- **Anything computed from props or state can just be computed in the function.**
  `showsSpeed = sample?.speedKph != null` belongs in the render, not in a separate
  piece of state kept in sync by an effect (Phase 03). Derived state that's stored
  separately is a second source of truth, and it drifts.
- **Every `let` and every arrow function inside a component is new on every render.**
  That's usually fine, and occasionally the cause of a bug (see "Stale closures" and
  `useSyncExternalStore` below).

## `useState`: values that cause a re-render when they change

```ts
const [sample, setSample] = useState<TreadmillSample | null>(null);
```

- `setSample(next)` doesn't change `sample` immediately; it schedules a re-render, and
  in *that* render `sample` has the new value.
- If the new value depends on the old one, pass a function:
  `setCount((c) => c + 1)`. The function receives the latest value even if several
  updates are queued.
- Setting state to the same value (by `Object.is`) skips the re-render. Setting it to a
  new object with the same contents does not: React compares references, not contents.

## `useEffect`: synchronising with something outside React

An effect is for connecting a component to something that isn't React: a
subscription, a timer, a native module. It runs *after* the render has been committed
to the screen.

```ts
useEffect(() => {
  const unsubscribe = treadmill.onSample((s) => setSample(s));
  return unsubscribe; // the cleanup
}, [treadmill]);
```

- **The cleanup function** runs before the effect runs again and when the component
  unmounts. Forget it, and a listener keeps calling `setSample` on a screen that no
  longer exists: a memory leak at best, a crash at worst. Every subscribe in this
  project returns its unsubscribe function precisely so the effect can return it
  (Phase 01b's design note).
- **The dependency array** says when to re-run: `[]` once on mount; `[treadmill]` when
  `treadmill` changes (never, in practice, but listing it is honest and the lint rule
  insists); no array at all means after every render, which is almost never what you
  want.
- **Strict Mode** (on in this project's development builds, Phase 00) mounts every
  component, unmounts it, and mounts it again. An effect with a correct cleanup
  survives this invisibly. An effect without one subscribes twice, and you see doubled
  log lines. That's the point: it surfaces the bug in development instead of in Phase
  12's memory graph.
- **Async work in an effect**: the effect function itself can't be `async` (it must
  return a cleanup, not a promise). Start the async work inside it, and guard against
  the component disappearing before it finishes:

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

  (Phase 03's contribution graph.)
- **You might not need an effect.** If something can be calculated from props and
  state during render, calculate it. Effects are for the outside world. React's own
  guide on this is in [`03-Doc-Links.md`](03-Doc-Links.md); it's worth reading once.

## `useRef`: a box that survives renders without causing them

```ts
const lastUpdateMs = useRef(0);
// later, in a listener:
if (Date.now() - lastUpdateMs.current < 250) return;
lastUpdateMs.current = Date.now();
```

A ref is an object `{ current: ... }` that React keeps for you across renders and
ignores for rendering. Changing `.current` does *not* re-render. That makes it the
right home for things the screen doesn't display but the logic needs to remember:
the throttle's timestamp (Phase 03), a timer handle, the previous value of something.
If the screen needs to show it, it's state; if only your code needs it, it's a ref.

## Stale closures

```ts
useEffect(() => {
  const id = setInterval(() => console.log(count), 1000); // always logs the first count
  return () => clearInterval(id);
}, []);
```

The interval's arrow function "closes over" `count` from the render the effect ran
in, and with `[]` that render never changes. It's a classic bug, and it's one reason
this project keeps long-lived logic (timers, debouncing, backoff) in plain classes in
`core/` rather than in hooks: a class reads `this.snapshot.targetSpeedKph` when the
timer fires, so there's no stale copy to worry about (Phase 05's `SpeedController`).

## `useSyncExternalStore`: reading a value that lives outside React

For "a value owned by a service, and re-render when it changes," React has a
purpose-built hook:

```ts
const state = useSyncExternalStore(reconnection.subscribe, reconnection.getState);
```

It takes two functions:

- `subscribe(onChange)`: start listening, call `onChange` whenever the value might
  have changed, return an unsubscribe function.
- `getSnapshot()`: return the current value.

**The one rule worth memorising:** `getSnapshot` must return *the same value* (by
`Object.is`) until something actually changed. Returning a fresh object on every call
(`return { ...this.state }`) makes React think the store changed on every render, which
is an infinite re-render loop. That's why the classes in this project keep a `snapshot`
field, replace it with a new object only when something changes, and return that field
(Phases 05, 08). A second, gentler rule: pass a *stable* `subscribe` function, not a new
arrow on every render, or React unsubscribes and resubscribes on every render. Defining
`subscribe` and `getSnapshot` as arrow-function properties on the class solves both.

## Context: getting the services into every screen

```ts
const { treadmill, workout, settings } = useServices();
```

`ServicesProvider` (Phase 00) puts the services object into React context at the root,
and `useServices()` reads it anywhere below. You could import the services module
directly instead, and it would work. Context buys one thing that matters here: a test
or a preview can wrap a screen in a provider with fakes, without touching the real
module.

## Lists and keys

```tsx
{presets.map((kph) => <SecondaryButton key={kph} label={kph.toFixed(0)} ... />)}
```

`key` is how React matches items between renders, so it can tell "this item moved"
from "this item is new." Use something unique and stable: a speed value, a day
string, a `workoutId`. An array index is only safe if the list never reorders, inserts
or deletes in the middle. Long or growing lists use `FlatList` (it only renders what's
on screen: Phase 10's history); short fixed ones use `.map()` (Phase 05's six presets).

## Promises in event handlers

```tsx
<Button label="Stop" onPress={() => void controller.stop()} />
```

- `onPress` doesn't wait for or care about a returned promise. `void` says "I'm
  deliberately not awaiting this."
- **A rejected promise nobody handles** becomes an "unhandled promise rejection"
  warning and the error is otherwise lost. Either the async function handles its own
  errors (and reports them through state, like `statusMessage`), or the caller adds
  `.catch(...)`. Phase 02's backoff loop is started without `await` from an event
  handler, so it needs one.
- `async` functions always return a promise, even if nothing inside awaits.

## TypeScript patterns this project uses everywhere

### String-literal unions instead of enums

```ts
type ConnectionState = 'disconnected' | 'connecting' | 'discovering' | 'ready';
```

A union of strings is the idiomatic TypeScript "enum": it's just the strings at
runtime (readable in logs, in JSON, in the database), and the compiler rejects a typo.

### Discriminated unions and exhaustive `switch`

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
    const unhandled: never = event; // compile error if a kind was missed
    return unhandled;
  }
}
```

The shared `kind` field "discriminates" the union: checking it narrows the type, so
fields that only exist on one variant are only accessible where they exist. The
`never` line at the end is the safety net: if someone adds a new kind and forgets this
switch, the code stops compiling (Phase 04). Result types use the same idea:
`{ ok: true; value } | { ok: false; reason }` forces the caller to check (Phase 01a).

### `as const`: one declaration, both a runtime value and a type

```ts
export const THEME_PREFERENCES = ['system', 'light', 'dark'] as const;
export type ThemePreference = (typeof THEME_PREFERENCES)[number]; // 'system' | 'light' | 'dark'

export const FtmsResultCode = { Success: 0x01, Timeout: 0xf0 } as const;
export type FtmsResultCode = (typeof FtmsResultCode)[keyof typeof FtmsResultCode]; // 0x01 | 0xf0
```

`as const` makes TypeScript keep the exact literal values instead of widening them to
`string[]` or `number`. The type is then *derived* from the value, so the list of
options shown on screen and the type the setter accepts can never drift apart (Phase
08). Giving the value and the type the same name (`FtmsResultCode`) is legal and
conventional: TypeScript keeps values and types in separate namespaces.

### Type guards

```ts
function isThemePreference(value: string | null): value is ThemePreference {
  return value !== null && (THEME_PREFERENCES as readonly string[]).includes(value);
}
```

A function returning `value is T` teaches the compiler something: after
`if (isThemePreference(stored))`, `stored` is a `ThemePreference`. This is how
"parse an untrusted string, fall back if it's garbage" stays type-safe without a cast.

### `null` for "absent", never `undefined`, in data

This project uses `number | null` for every optional field in data (samples, rows,
exports): `null` maps directly onto SQLite `NULL` and survives `JSON.stringify`
(which silently *drops* `undefined` fields). `undefined` is fine for optional function
parameters and props.

### `noUncheckedIndexedAccess`

Turned on in Phase 00. With it, `bytes[3]` has type `number | undefined`, because the
index might be past the end. It's mildly annoying in ordinary code and exactly right for
a project whose core job is parsing byte buffers: you can't forget the length check.

### Structural typing and `implements`

TypeScript checks shapes, not names: any object with the right methods *is* a
`TreadmillService`. `class FakeTreadmillService implements TreadmillService` isn't
required, but it makes the compiler report a missing method at the class, not at some
distant call site. `ReturnType<typeof setTimeout>` is the portable type for a timer
handle (it's a number on the phone and an object in Node).

## Numbers and bytes

- **Every JavaScript `number` is a 64-bit float.** Integers are exact up to 2^53, so
  byte values and counters are fine, but decimals aren't: `0.1 + 0.2` is
  `0.30000000000000004`. That's why `clampSpeed` rounds to two decimals before encoding
  (Phase 01b).
- **Bitwise operators work on 32-bit integers.** `flags & 0x01`, `byte << 8` and
  `a | b` are what the FTMS parsers use; they're exact for anything that fits in 32
  bits, which every FTMS field does.
- **`Uint8Array`** is a fixed-size array of bytes, and what BLE values are turned into
  at the boundary. **`DataView`** reads multi-byte numbers out of one:
  `view.getUint16(offset, true)`. The `true` means little-endian, which FTMS always is
  and `DataView`'s default is not. Forgetting it is the easiest way to get every field
  wrong (Phase 01a).
- A `Uint8Array` can be a *window* onto a larger buffer (`byteOffset` > 0). Construct a
  `DataView` with `(bytes.buffer, bytes.byteOffset, bytes.byteLength)`, never just
  `bytes.buffer`.

## Where to go next

- [`00-Expo-And-React-Native.md`](00-Expo-And-React-Native.md) if you haven't read it.
- [`03-Doc-Links.md`](03-Doc-Links.md): the official React docs on effects,
  `useSyncExternalStore` and "You Might Not Need an Effect" are the three most useful
  pages for this project.
- `phases/phase-01-protocol-decode/treadmillService.ts`: the contract almost every
  phase builds against, and a compact example of everything in the TypeScript section
  above.
