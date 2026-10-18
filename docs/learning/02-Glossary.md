# Glossary

> Growing list. Add a term here the first time it comes up in a phase, so it's never
> just "explained once in chat and gone."

### Expo and React Native

- **Expo Go**: Expo's prebuilt app from the Play Store, containing a fixed set of
  native modules. Can't run this project, because `react-native-ble-manager` isn't one
  of them. See `00-Expo-And-React-Native.md`.
- **Development build**: your own version of the app, compiled with your native
  modules inside plus a developer menu; it loads JavaScript from Metro, so TypeScript
  edits still update instantly. Built with `npx expo run:android --device`.
- **Release build**: the optimised app with its JavaScript bundled inside; no Metro,
  no dev tools. The only build to measure performance or test screen-off behaviour on.
- **Metro**: the bundler that turns `src/**/*.ts(x)` into one JavaScript bundle and
  serves it to a development build.
- **Fast Refresh**: Metro pushing an edited file into the running app without a full
  reload, keeping component state where it can.
- **Continuous Native Generation (CNG) / prebuild**: generating the `android/` project
  from `app.json` and config plugins (`npx expo prebuild`) instead of editing it by
  hand. `src/android/` is never edited and never committed.
- **Config plugin**: a JavaScript function that modifies the generated native project
  during prebuild (e.g. Phase 11's `withResizeableActivity`). Libraries ship them too.
- **Local Expo module**: Kotlin (and TypeScript) source in `src/modules/<name>/`,
  exposed to JavaScript through the **Expo Modules API** (`Function(...)`,
  `Events(...)`). Phase 00/07's foreground service.
- **Native module**: Java/Kotlin code callable from JavaScript. ble-manager,
  expo-sqlite and expo-file-system are native modules.
- **JavaScript thread**: the single thread all of the app's TypeScript runs on.
  Nothing to marshal between, and nothing else runs while your code is busy.
- **Hermes**: the JavaScript engine React Native runs your code in.
- **New Architecture / JSI / TurboModule**: React Native's current internals. JSI lets
  JavaScript call native code directly; a TurboModule is a native module built on it.
  Always on from Expo SDK 55.
- **Headless JS task**: JavaScript React Native runs without a screen, started from
  native code (`HeadlessJsTaskService`). While one is running, JS timers keep firing in
  the background. This project's keep-alive is a headless task that deliberately never
  finishes until the workout ends.
- **Expo Router / route file**: file-based navigation. `src/app/settings.tsx` *is* the
  `/settings` screen; `src/app/history/[workoutId].tsx` is a dynamic route.
- **`app.json`**: the Expo project config: name, package, permissions, plugins, icons.

### React

- **Component**: a function that returns a description of UI. React calls it again on
  every render.
- **Props**: a component's inputs, passed by its parent.
- **State** (`useState`): a value whose change re-renders the component.
- **Effect** (`useEffect`): code that synchronises a component with something outside
  React (a subscription, a timer), run after rendering.
- **Cleanup**: the function an effect returns; undoes what the effect did (unsubscribe,
  clear a timer). Runs before the effect re-runs and on unmount.
- **Ref** (`useRef`): a `{ current }` box React keeps across renders without
  re-rendering when it changes.
- **Hook**: a function starting with `use` that calls other hooks. In this project, a
  screen's hook (`useDashboard`) plays the ViewModel role.
- **`useSyncExternalStore`**: React's hook for reading a value owned outside React and
  re-rendering when it changes. Needs a stable `subscribe` and a `getSnapshot` that
  returns the same object until something changes.
- **Context** (`createContext`, `useContext`): passing a value to every component below
  a provider without threading props. `useServices()` is built on it.
- **Strict Mode**: a development-only wrapper that mounts, unmounts and remounts
  components to expose missing effect cleanups.
- **`key`**: the identity React uses to match list items across renders.
- **Controlled input**: an input whose value comes from state and whose change handler
  writes state back (`<Switch value={...} onValueChange={...} />`).
- **`FlatList`**: a virtualised list that only renders the rows on screen. For long or
  growing lists; short fixed ones just use `.map()`.
- **Stale closure**: a callback still seeing values from the render it was created in.

### TypeScript

- **String-literal union**: `'idle' | 'active' | 'paused' | 'finished'`, this
  project's "enum."
- **Discriminated union**: a union of object types sharing a tag field (`kind`, `ok`);
  checking the tag narrows the type.
- **Narrowing**: TypeScript refining a variable's type after a check (`if`, `switch`,
  a type guard).
- **Type guard**: a function returning `value is T`, teaching the compiler what a
  check proved.
- **Exhaustiveness check**: `const unhandled: never = x` at the end of a `switch`;
  stops compiling if a union member isn't handled.
- **`as const`**: keep literal types (`'dark'`, `0x01`) instead of widening them;
  combined with `typeof`, derives a type from a value.
- **Structural typing**: TypeScript compares shapes, not names. Any object with the
  right members satisfies an interface.
- **`noUncheckedIndexedAccess`**: compiler option making `array[i]` possibly
  `undefined`. On in this project, for safe byte parsing.

### Android

- **Process**: the running app. Lives as long as Android lets it; the JavaScript
  runtime lives inside it.
- **Activity**: the screen container Android shows. Can be destroyed and recreated
  (rotation, resize) while the process lives on.
- **`configChanges`**: the configuration changes an activity handles itself instead of
  being recreated for. Expo's template already declares the split-screen ones.
- **`resizeableActivity`**: whether an activity may run in split screen.
- **Service / foreground service**: an Android component for work without a screen. A
  foreground service shows a notification and in return isn't killed for being in the
  background.
- **Foreground service type**: the declared kind of work (`connectedDevice` here),
  required since Android 14; it needs its own permission.
- **Wake lock (partial)**: keeps the CPU running with the screen off.
- **Doze**: Android's deep idle mode on a still, unplugged phone with the screen off;
  defers timers, network and jobs. Never happens while charging.
- **`PendingIntent`**: a token handing an action (e.g. a notification button) to the
  OS, to be delivered back to the app later.
- **Runtime permission**: one the user grants in a dialog at runtime
  (`BLUETOOTH_SCAN`, `BLUETOOTH_CONNECT`, `POST_NOTIFICATIONS`), requested with
  `PermissionsAndroid`.
- **`adb`**: the Android Debug Bridge: install, logs (`adb logcat`), shell, `dumpsys`.
- **`dumpsys`**: Android's system state dump (`meminfo`, `batterystats`, `activity
  services`).
- **HyperOS controls**: Xiaomi's Autostart, Battery saver, Recents-lock settings,
  separate from Android's battery optimisation. Effectively mandatory for this app.

### Bluetooth / ble-manager

- **GATT**: Generic Attribute Profile, the data model BLE devices expose: services,
  each containing characteristics, each optionally having descriptors.
- **Service / Characteristic / Descriptor**: see "Reading this reference" in
  `phases/phase-01-protocol-decode/README.md`'s FTMS protocol reference for the
  read/write/notify/indicate operations available on each.
- **Indication vs. Notification**: both are "the device pushes data without being
  asked"; an indication is acknowledged by the receiver, a notification isn't. The
  control point (`0x2AD9`) uses indications specifically so the app can be sure a
  command was received.
- **CCCD (`0x2902`)**: the descriptor a client writes to turn notifications (`0x0001`)
  or indications (`0x0002`) on. `BleManager.startNotification` writes it for you.
- **MTU**: the maximum packet size negotiated after connecting. Default 23 bytes (20 of
  payload); this app requests 517.
- **GATT 133**: Android's catch-all GATT error status. Generic, common, and treated as
  empirical in this project (Phase 02).
- **RSSI**: received signal strength in dBm. −49 dBm at the walking position is strong.
- **Peripheral id**: ble-manager's name for a device; on Android, its MAC address.
- **`retrieveServices`**: ble-manager's service discovery call.

### This project's architecture

- **`core/` vs. `features/`**: `src/core/` is plain TypeScript with no React, React
  Native or Expo imports (lint-enforced), tested in Node; `src/features/` is the React
  side and the adapters to native modules. See `00-Expo-And-React-Native.md`.
- **Composition root**: `src/features/services.tsx`, the one place long-lived objects
  are created, and the one place a fake is swapped for a real implementation.
- **Seam / Fake pattern**: an interface (e.g. `TreadmillService`) with a real
  implementation and a `Fake...` one that simulates realistic behaviour. Used for
  `TreadmillService` (Phase 01b), `WorkoutHistoryProvider` (03), `PreferencesStore`
  (08) and `StatisticsProvider` (10).
- **Subscribe-returns-unsubscribe**: this project's event pattern: `onSample(listener)`
  returns the function that removes the listener.
- **Inject what `core/` can't import**: passing a capability (UUID generator, clock,
  byte sink, haptic) into a `core/` class from `features/`.
- **Exponential backoff**: retrying with growing delays (1, 2, 4, 8, 16, then 30 s
  steady). Phase 02.
- **Throttle vs. debounce**: throttle fires at a bounded rate while a stream keeps
  going (dashboard updates, Phase 03); debounce waits for the stream to go quiet (speed
  taps, Phase 05).
- **`AbortController` / `AbortSignal`**: the standard JavaScript way to cancel async
  work. Phase 02's backoff loop.
- **Keep-alive**: the foreground service + never-finishing headless task that keeps the
  process and JS timers alive during a workout. Phase 00 spike, Phase 07 product.
