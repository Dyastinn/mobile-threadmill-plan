# Glossary

> Add a word here the first time it comes up in a phase, so it isn't explained once in
> chat and then lost.

### Expo and React Native

- **Expo Go**: Expo's ready-made app from the Play Store. It only includes certain
  libraries, and the Bluetooth library isn't one, so it can't run this app.
- **Development build**: your own version of Expo Go, with your libraries inside. Your
  code still comes from the PC and updates when you save. Built with
  `npx expo run:android --device`.
- **Release build**: the finished app, with your code inside. No PC needed. Use it for
  speed, battery and screen-off tests.
- **Metro**: the tool that bundles your code and sends it to the development build.
- **Fast Refresh**: the app updating on the phone when you save a file.
- **Prebuild / generated `android/` folder**: Expo creates the Android project from
  `app.json` on every build. You never edit `src/android/` and never commit it.
- **Config plugin**: a small JavaScript file that changes a setting in the generated
  Android files (Phase 11 has one). Libraries can include their own.
- **Local Expo module**: Kotlin code in `src/modules/<name>/` that TypeScript can call.
  Phase 00/07's background service.
- **Native module**: Java or Kotlin code your TypeScript can call, like the Bluetooth
  library or `expo-sqlite`.
- **JavaScript thread**: the one thread all your code runs on. While your code is busy,
  nothing else in your code runs.
- **Hermes**: the JavaScript engine on the phone.
- **New Architecture**: how current React Native connects JavaScript to native code.
  Always on since Expo 55; nothing to set up.
- **Headless JS task**: JavaScript that React Native runs with no screen showing. While
  one is running, timers keep working in the background. This app's keep-alive is a task
  that deliberately never ends until the workout does.
- **Expo Router / route file**: screens as files. `src/app/settings.tsx` is the
  `/settings` screen. `src/app/history/[workoutId].tsx` is a screen with a parameter.
- **`app.json`**: the Expo settings file: name, package, permissions, plugins, icons.

### React

- **Component**: a function that returns what the screen should show. React calls it
  again on every redraw.
- **Props**: a component's inputs, given by its parent.
- **State** (`useState`): a value that redraws the component when it changes.
- **Effect** (`useEffect`): code that connects a component to something outside React
  (a listener, a timer). Runs after the screen updates.
- **Cleanup**: the function an effect returns. It undoes the effect (stop listening,
  clear a timer). Runs before the effect runs again, and when the component goes away.
- **Ref** (`useRef`): a box that keeps a value between redraws without causing one.
- **Hook**: a function whose name starts with `use`. Here, each screen has one
  (`useDashboard`) that holds its state and logic.
- **`useSyncExternalStore`**: React's hook for showing a value that lives outside React.
  Its `getSnapshot` must return the same object until something changes.
- **Context**: a way to give a value to every component below a provider.
  `useServices()` uses it.
- **Strict Mode**: a development-only setting that starts, removes and restarts each
  component, to show up effects that forget their cleanup.
- **`key`**: how React tells list items apart between redraws.
- **Controlled input**: an input whose value comes from state, and whose change handler
  updates that state (`<Switch value={...} onValueChange={...} />`).
- **`FlatList`**: a list that only draws the rows on screen. For long or growing lists.
- **Stale value**: a function still seeing a value from an older redraw.

### TypeScript

- **String union**: `'idle' | 'active' | 'paused' | 'finished'`. This project's version
  of an enum.
- **Union with a `kind` field** (discriminated union): several object types that share a
  field like `kind` or `ok`. Checking it tells TypeScript which type you have.
- **Narrowing**: TypeScript working out a more exact type after an `if` or `switch`.
- **Type guard**: a function returning `value is T`, telling the compiler what a check
  proved.
- **Exhaustive check**: `const unhandled: never = x` at the end of a `switch`. It stops
  compiling if a case is missing.
- **`as const`**: keep the exact values (`'dark'`, `0x01`) instead of widening them to
  `string` or `number`.
- **Shapes, not names** (structural typing): any object with the right properties
  counts as that type.
- **`noUncheckedIndexedAccess`**: a setting that makes `array[i]` possibly `undefined`,
  so you can't forget to check lengths.

### Android

- **App process**: the running app. Your JavaScript lives inside it.
- **Activity**: the window Android shows. It can be rebuilt (on rotation or resize)
  while the app keeps running.
- **`configChanges`**: the changes an activity handles itself instead of being rebuilt.
  Expo already lists the split-screen ones.
- **`resizeableActivity`**: whether the app may run in split screen.
- **Foreground service**: background work with a visible notification. In return,
  Android doesn't close the app for being in the background.
- **Service type**: what kind of work the service does (`connectedDevice` here).
  Required since Android 14, with its own permission.
- **Wake lock**: keeps the CPU awake with the screen off.
- **Doze**: Android's deep sleep on a still, unplugged phone with the screen off. It
  delays timers and background work. Never happens while charging.
- **`PendingIntent`**: a way for a notification button to reach your app later.
- **Runtime permission**: one the user grants in a popup (`BLUETOOTH_SCAN`,
  `BLUETOOTH_CONNECT`, `POST_NOTIFICATIONS`).
- **`adb`**: the tool for talking to the phone from the PC: install, logs
  (`adb logcat`), commands, `dumpsys`.
- **`dumpsys`**: prints the phone's state (`meminfo`, `batterystats`,
  `activity services`).
- **HyperOS settings**: Xiaomi's Autostart, Battery saver and Recents lock, separate from
  Android's battery optimisation. Effectively required for this app.

### Bluetooth

- **GATT**: how a Bluetooth device organises its data: services, which contain
  characteristics, which can have descriptors.
- **Service / characteristic / descriptor**: see "How Bluetooth data works here" in
  Phase 01's protocol reference.
- **Read / write / notify / indicate**: the four things you can do with a
  characteristic. Notify and indicate both mean "the device sends data by itself";
  indicate means the phone confirms it got it. The control point uses indicate, so the
  app knows a command arrived.
- **CCCD (`0x2902`)**: the setting you write to turn on notify (`0x0001`) or indicate
  (`0x0002`). `BleManager.startNotification` does it for you.
- **MTU**: the biggest packet size, agreed after connecting. 20 bytes by default; this
  app asks for 517.
- **GATT 133**: Android's catch-all Bluetooth error. Common, vague, and fixed by testing
  (Phase 02).
- **RSSI**: signal strength, in dBm. −49 dBm at the treadmill is strong.
- **Peripheral id**: the library's name for a device. On Android, its MAC address.
- **`retrieveServices`**: the library's call for finding a device's services.
- **Little-endian**: numbers stored low byte first. Every FTMS number is.

### This project

- **`core/` vs. `features/`**: `src/core/` is plain TypeScript with no phone-specific
  imports (lint checks), tested on the PC. `src/features/` is the React side and the code
  that uses phone features.
- **Services file**: `src/features/services.tsx`, where long-lived objects are created
  once, and where a fake is swapped for the real thing.
- **Interface + fake** (seam): an interface like `TreadmillService` with a real version
  and a fake one, so everything above it can be built without the real thing. Used for
  the treadmill (01b), workout history (03), settings storage (08), statistics (10).
- **Subscribe returns unsubscribe**: this project's event pattern. `onSample(listener)`
  returns the function that stops listening.
- **Passing in what `core/` can't import**: giving a `core/` class a phone feature (a GUID
  maker, the clock, a file, a vibration) from `features/`.
- **Exponential backoff**: waiting longer after each failed try (1, 2, 4, 8, 16, then 30
  seconds). Phase 02.
- **Throttle vs. debounce**: throttle = update at a limited rate while data keeps coming
  (the dashboard, Phase 03). Debounce = wait until it stops, then act once (speed taps,
  Phase 05).
- **`AbortController` / `AbortSignal`**: the standard way to cancel async work. Phase 02.
- **Keep-alive**: the background service plus the never-ending headless task that keep
  the app and its timers running during a workout. Tested in Phase 00, finished in Phase 07.
