# Phase 07 — Background Service

> Every long test from here on depends on this. Split screen (app visible but not
> focused) is exactly where Android starts holding apps back.

**Treadmill needed:** yes · **Size:** M · **Waits for:** Phase 06 · **Depends on:** V5

---

## Goal

Keep Bluetooth, your code, and recording running while the screen is off or another
app is in front.

Phase 00 (task 0.12) already built a **test version**: a Kotlin service that shows a
notification and keeps a "keep-alive" task running, and runbook Part H measured whether
that's enough on this phone. In this phase you take it over and finish it: go through
the test version line by line with the agent, then add what it skipped (live
notification text, Pause/Stop buttons, the notification permission, a Start that only
happens on a tap, and the TypeScript that connects it to the workout).

**First, read V5 in `PHASE-00-FINDINGS.md`:**

- **Holds:** follow this README as written.
- **Partly holds:** follow this README, but start by looking at what stalled with the
  agent. Usually it's a HyperOS setting or the wake lock.
- **Fails:** skip to "If V5 failed" at the bottom. Bluetooth and recording move into
  Kotlin.

This phase is mostly **Kotlin**, plus one TypeScript file. You write it; the agent
explains and reviews. Kotlin is new, so each Kotlin task starts with the agent walking
through the syntax. It reads a lot like TypeScript: `val`/`var`, `fun`, `?` for "can be
null", `when` instead of `switch`.

### The idea

To save battery, Android pauses or closes apps that aren't on screen. A **foreground
service** is the deal you make with Android: show a notification the user can always
see, and Android leaves the app alone.

React Native adds a second problem. Keeping the *app* alive isn't the same as keeping
your *code* running normally: React Native stops `setTimeout` and `setInterval` when
the screen is off, unless a **headless JS task** is running. So the service does two
things: it's a foreground service (the app stays alive), and it runs a headless task
that deliberately never finishes until the workout ends (the timers keep firing). The
React Native class it's built on, `HeadlessJsTaskService`, also keeps the CPU awake
while that task runs (a "partial wake lock").

**Why not just run in the background normally?** It works while the app is on screen.
Lock the phone during a 20–60 minute walk, and Android slows and then stops it, and
React Native's timers stop even sooner. Nothing crashes: reconnecting quietly never
happens and samples quietly stop being saved. That's exactly what this phase prevents.

**Why the `connectedDevice` type?** Android 14+ requires a foreground service to say
what kind of work it does. Bluetooth in the background needs `connectedDevice` and its
own permission. Get it wrong and there's no error, Bluetooth just quietly stops
working: a horrible thing to debug.

**One simpler choice.** In a pure Android app, the service would *own* the Bluetooth
connection, and the screens would connect to the service to read live values. Here, the
connection, the engine and the recorder already live in your services file, which
belongs to the app itself, not to any screen. So the service only has to keep the app
alive and show the notification. The rule this depends on: **anything that must keep
running with the screen off lives in the services file, never in a component.**

## What you'll learn

- **Android services**: the notification you must show, the 5-second rule after
  starting one, and the service type.
- **Headless JS**: React Native code running without a screen, and why a task that never
  finishes is the keep-alive.
- **`PendingIntent`**: how a notification button gets back into your app, even when the
  app isn't in front.
- **Expo Modules**: how a Kotlin function becomes callable from TypeScript, and how a
  Kotlin event reaches a TypeScript listener.
- **`POST_NOTIFICATIONS`**: the Android 13+ permission for showing notifications,
  requested like Phase 00's Bluetooth permissions.
- **Where native settings live**: the module's own `AndroidManifest.xml` is merged into
  the app when it builds, so you never edit `src/android/`.

## Read first

| Topic | Link | For |
|---|---|---|
| Foreground services | https://developer.android.com/develop/background-work/services/fgs | The notification, the 5-second rule |
| Service types | https://developer.android.com/develop/background-work/services/fgs/service-types | `connectedDevice` and what it needs first |
| Types required on Android 14+ | https://developer.android.com/about/versions/14/changes/fgs-types-required | Why a missing type fails silently |
| Services overview | https://developer.android.com/develop/background-work/services | Started vs. bound, and why this one is only started |
| Headless JS | https://reactnative.dev/docs/headless-js-android | `HeadlessJsTaskService`, `getTaskConfig`, `registerHeadlessTask` |
| Expo Modules overview | https://docs.expo.dev/modules/overview/ | What a local module is |
| Expo Modules API | https://docs.expo.dev/modules/module-api/ | `Function`, `Events`, `sendEvent` (task 7.3) |
| Notifications | https://developer.android.com/develop/ui/views/notifications/build-notification | Task 7.3 |
| `NotificationCompat.Builder` | https://developer.android.com/reference/androidx/core/app/NotificationCompat.Builder | Task 7.3 |
| `PendingIntent` | https://developer.android.com/reference/android/app/PendingIntent | The notification buttons |
| Notification permission | https://developer.android.com/develop/ui/views/notifications/notification-permission | Task 7.4 |
| Bluetooth in the background | https://developer.android.com/develop/connectivity/bluetooth/ble/background | Why this phase exists |
| Doze | https://developer.android.com/training/monitoring-device-state/doze-standby | What the wake lock protects against |
| Kotlin basics | https://kotlinlang.org/docs/basic-syntax.html | Read once before task 7.2 |

## Features

- The service starts when a workout starts and stops when it ends
- A notification with elapsed time, distance and speed
- Pause and Stop buttons on the notification
- **Bluetooth and recording keep going with the screen off**

---

## Rules

- `foregroundServiceType="connectedDevice"` **and** the
  `FOREGROUND_SERVICE_CONNECTED_DEVICE` permission. Without them, Bluetooth fails
  silently in the background. (7.2)
- Pass the same type to `startForeground()` too. (7.2)
- On Android 14+, the service can only start once `BLUETOOTH_CONNECT` is granted, or
  `startForeground` throws. (7.5)
- Ask for `POST_NOTIFICATIONS`. (7.4)
- Start the service **only from the Start button**. Never in the background, never at
  boot, never when the app opens. (7.5)
- **Never start the treadmill from a notification button.** Pause and Stop only. (7.3)
- The keep-alive task **must not finish** until the workout ends. If it finishes early,
  the timers stop (that's exactly the bug in expo/expo#51304).

**Time limits:** Android 15's 6-hour limit only applies to other service types, not
`connectedDevice`. Long workouts are fine.

---

## Tasks

### 7.1 — Go through the test version (review first)

No new code. With the agent, read what Phase 00 built until you could explain each line:

1. `src/modules/recording-service/expo-module.config.json`: how Expo finds the module.
2. `src/modules/recording-service/android/src/main/AndroidManifest.xml`: the four
   permissions and the `<service>` entry. Why `android:exported="false"`?
3. `WorkoutRecordingService.kt`: `onStartCommand`, `getTaskConfig`, and what
   `HeadlessJsTaskService` does in between. If something's unclear, open its source in
   `node_modules/react-native/ReactAndroid/`.
4. `RecordingServiceModule.kt`: how `Function("start") { ... }` becomes callable from
   TypeScript.
5. `src/features/background/keepAlive.ts` and `src/index.ts`: why the task is
   registered before `expo-router/entry` is imported.

Then write one paragraph in your own words: what keeps the app alive, what keeps the
timers running, and what keeps the CPU awake. Three different things. That paragraph is
the checkpoint.

### 7.2 — Check the permissions and the service type

Mostly checking, no new code if the test version is right.

1. After a build, check the phone itself:
   ```powershell
   adb shell dumpsys package com.myhi.companion | Select-String -Pattern "FOREGROUND_SERVICE|POST_NOTIFICATIONS|WAKE_LOCK"
   ```
   All four permissions should be listed.
2. Start a fake workout with the keep-alive on, then:
   ```powershell
   adb shell dumpsys activity services com.myhi.companion
   ```
   Look for `isForeground=true` and a service type of `0x10` (`connectedDevice`). If
   the type is missing, nothing else in this phase will work.
3. Check `onStartCommand` returns `START_NOT_STICKY`, not `START_STICKY`. If Android
   kills the app anyway, the workout's state is gone with it; restarting an empty service
   would help nothing, and on Android 12+ it can even crash. Recovery is Phase 06's job
   (the in-progress row).

### 7.3 — Notification text and Pause/Stop buttons

Change `WorkoutRecordingService.kt`, `RecordingServiceModule.kt` and
`src/modules/recording-service/index.ts`.

The service's final shape (the `TODO`s are yours):

```kotlin
package expo.modules.recordingservice

class WorkoutRecordingService : HeadlessJsTaskService() {
  companion object {
    const val ACTION_PAUSE = "com.myhi.companion.action.PAUSE"
    const val ACTION_STOP = "com.myhi.companion.action.STOP"
    const val NOTIFICATION_ID = 1001
    const val CHANNEL_ID = "workout_recording"

    /** Set by RecordingServiceModule, so button taps reach TypeScript. */
    var onAction: ((String) -> Unit)? = null
  }

  override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
    when (intent?.action) {
      ACTION_PAUSE -> { onAction?.invoke("pause"); return START_NOT_STICKY }
      ACTION_STOP -> { onAction?.invoke("stop"); return START_NOT_STICKY }
    }
    // First start: show the notification first (the 5-second rule), then let
    // HeadlessJsTaskService start the keep-alive task.
    // TODO: WorkoutNotification.ensureChannel(this), build the notification, then
    // ServiceCompat.startForeground(this, NOTIFICATION_ID, notification,
    //     ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
    return super.onStartCommand(intent, flags, startId)
  }

  override fun getTaskConfig(intent: Intent?): HeadlessJsTaskConfig? =
    // task name, no data, 0 = never time out, allowed while the app is on screen
    HeadlessJsTaskConfig("RecordingKeepAlive", Arguments.createMap(), 0, true)
}
```

The early `return` for the two buttons matters: `super.onStartCommand` starts a task, so
letting a button tap fall through would start a *second* keep-alive every time.

A notification helper, `WorkoutNotification.kt`:

```kotlin
object WorkoutNotification {
  fun ensureChannel(context: Context) {
    // TODO: create the channel with IMPORTANCE_LOW (no sound on every update)
  }

  fun build(context: Context, title: String, text: String): Notification {
    val pause = actionIntent(context, WorkoutRecordingService.ACTION_PAUSE, requestCode = 1)
    val stop = actionIntent(context, WorkoutRecordingService.ACTION_STOP, requestCode = 2)
    return NotificationCompat.Builder(context, WorkoutRecordingService.CHANNEL_ID)
      // TODO: setSmallIcon, setOngoing(true), setOnlyAlertOnce(true), title, text
      .addAction(0, "Pause", pause)
      .addAction(0, "Stop", stop)
      .build()
  }

  private fun actionIntent(context: Context, action: String, requestCode: Int): PendingIntent {
    // TODO: an Intent for WorkoutRecordingService with this action, wrapped in
    // PendingIntent.getService(..., FLAG_UPDATE_CURRENT or FLAG_IMMUTABLE)
    TODO()
  }
}
```

(`FLAG_IMMUTABLE` is required on Android 12+, and these buttons don't need anything
else.)

The module gets an `update` function and an event:

```kotlin
class RecordingServiceModule : Module() {
  override fun definition() = ModuleDefinition {
    Name("RecordingService")
    Events("onAction")

    OnCreate {
      WorkoutRecordingService.onAction = { action -> sendEvent("onAction", mapOf("action" to action)) }
    }

    Function("start") { title: String, text: String -> /* from the test version */ }
    Function("update") { title: String, text: String ->
      // TODO: post the notification again with the same NOTIFICATION_ID; that replaces it
    }
    Function("stop") { /* from the test version */ }
  }
}
```

And `index.ts` exports `start`, `update`, `stop` and a typed
`addActionListener(listener: (action: 'pause' | 'stop') => void)`.

1. Fill in the TODOs one file at a time, and rebuild after each
   (`npx expo run:android --device`). Kotlin changes need a rebuild; saving isn't enough.
2. For `setSmallIcon`, `android.R.drawable.stat_notify_sync` is fine for now. A proper
   icon is Phase 13.
3. Don't update the notification on every sample. Every 5 seconds is plenty (7.6).

### 7.4 — The notification permission

Create `src/features/workout/workoutPermissions.ts`. Same pattern as Phase 00's
Bluetooth permissions.

1. Write `ensureWorkoutPermissions()`, returning `{ bluetooth, notifications }`. It
   checks, and if needed asks for, `BLUETOOTH_CONNECT` and `POST_NOTIFICATIONS`.
2. Call it only from the Start button.
3. If notifications are denied: the service still runs, you just lose the notification
   and its buttons. Recording can carry on; tell the user. If `BLUETOOTH_CONNECT` is
   missing: don't start at all (`startForeground` would throw).

### 7.5 — Starting a workout: one deliberate tap

Create `src/features/workout/workoutSession.ts` (yours) and
`src/features/workout/WorkoutButton.tsx` (given).

Nothing in the app actually starts a workout yet. `WorkoutSession` is the one place
that does: the only caller of `RecordingService.start()` and `treadmill.start()` in the
whole app.

```ts
export class WorkoutSession {
  constructor(
    private readonly engine: WorkoutEngine,
    private readonly treadmill: TreadmillService,
    private readonly log: Logger,
  ) {}

  /** Called ONLY from the Start button. */
  async start(): Promise<{ ok: boolean; message: string | null }> {
    // TODO, in order:
    // 1. ensureWorkoutPermissions(); no Bluetooth permission -> return a message
    // 2. RecordingService.start('Workout', 'Starting...'). This also starts the
    //    keep-alive task (the service does that); there's nothing else to call.
    // 3. engine.tryStart()
    // 4. if you have control: treadmill.start(), then send the target speed again
    //    if V2's "Start forgets the speed" is still true
    // 5. if anything after step 2 fails: stopKeepAlive() and RecordingService.stop(),
    //    so a failed start never leaves a notification behind
  }

  async stop(): Promise<void> {
    // TODO: treadmill.stop() (if you have control), then engine.tryStop().
    // Don't stop the service here: task 7.6 does that when the workout finishes,
    // so it also happens when a workout ends some other way.
  }
}
```

1. Write `WorkoutSession` and add it to the services file as `session`.
2. Search the code for every `RecordingService.start` and `treadmill.start`. Each must
   be reachable **only** from `WorkoutSession.start`, which is reachable **only** from a
   button tap.
3. The button (given), `src/features/workout/WorkoutButton.tsx`:

```tsx
import { useState } from 'react';
import { Alert, View } from 'react-native';

import { useWorkoutState } from './useWorkoutState';
import { useServices } from '@/features/services';
import { Button } from '@/features/shared/theme/Button';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';

export function WorkoutButton() {
  const { session } = useServices();
  const state = useWorkoutState();
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onStart() {
    setBusy(true);
    const result = await session.start();
    setBusy(false);
    setMessage(result.message);
  }

  function onStop() {
    Alert.alert('Finish this workout?', 'Recording stops and the workout is saved.', [
      { text: 'Keep going', style: 'cancel' },
      { text: 'Finish', onPress: () => void session.stop() },
    ]);
  }

  const running = state === 'active' || state === 'paused';

  return (
    <View style={{ gap: spacing.sm }}>
      {running ? (
        <Button label="Finish workout" onPress={onStop} />
      ) : (
        <Button label="Start workout" onPress={() => void onStart()} disabled={busy} />
      )}
      {message !== null && <Text variant="caption">{message}</Text>}
    </View>
  );
}
```

   Add `<WorkoutButton />` to the dashboard, above `<TreadmillControls />`.

### 7.6 — The TypeScript side

Add to `workoutSession.ts`:

1. In the constructor, listen to `engine.onStateChange`. On `finished`:
   `stopKeepAlive()` and `RecordingService.stop()`. Every way a workout can end (the
   button, the notification, the 60-second timeout, the safety key) reaches `finished`,
   so the cleanup can't be missed.
2. Listen to `RecordingService.addActionListener`. `'pause'` →
   `engine.tryPause('userRequested')` and `treadmill.pause()`. `'stop'` → the same as
   `stop()`. There's no `'start'`, and there must never be one.
3. Listen to `treadmill.onSample` and, at most every 5 seconds during a workout, call
   `RecordingService.update(title, text)` with time, distance and speed (formatted with
   Phase 03's `format.ts`). Metric; Phase 08's unit setting changes only this text,
   never saved data.
4. Keep every unsubscribe function, and call them all in `dispose()`.

### Review

Before the treadmill tests, the agent goes through your Kotlin, the `dumpsys` output from
7.2, and `workoutSession.ts`. This is where a silent Android problem (a missing type or
permission, a wrong flag, a keep-alive that finishes early) can cost hours, so a second
look first is worth it.

---

## HyperOS settings (effectively required)

On this phone (Poco X6 Pro, HyperOS), **turning off Android's battery optimisation isn't
enough.** Xiaomi has its own controls, and they're the ones that close apps.

- [ ] Android battery optimisation off *(already done)*
- [ ] **Autostart** on (a separate HyperOS menu)
- [ ] **Battery saver** set to **"No restrictions"** (HyperOS's own setting in app info)
- [ ] App **locked in Recents** (padlock in the app switcher)

*The menu names change between HyperOS versions.*

**Check all four before Phase 14**, and record them in `PHASE-00-FINDINGS.md`. They should
already be set from Part H. Check again after reinstalling: some HyperOS versions reset
them.

The app should explain these, not demand them. A "Before your first workout" screen with
a button per setting (`Linking.openSettings()` opens the app's info page) would fit in
Phase 13.

---

## If V5 failed: Bluetooth and recording move into Kotlin

Only if Part H failed: the app stayed alive, but your code didn't reliably get Bluetooth
data or timer ticks. Then the work has to happen inside the service, where Android does
keep running it.

In outline (the details get planned with the agent when needed, not now):

- The Kotlin module gets a small Bluetooth client (connect, listen to `0x2ACD`/`0x2ADA`,
  send commands), and saves to the same database (same tables).
- TypeScript stops listening to the Bluetooth library during a workout, and reads the
  current values from the module when the screen is on.
- `TreadmillService` stays the interface. A new `NativeTreadmillService` implements it
  using the module, and nothing above it changes. This is what the interface was for.

Expect this to roughly double Phase 07. Plan it again with the agent before starting.

---

## Tests

| Test | Expected | |
|------|----------|---|
| Lock the phone, walk 10 min | No disconnect, no gaps | treadmill |
| YouTube in front for 10 min | No disconnect | treadmill |
| Notification Pause/Stop | Work; the app shows it | treadmill |
| The notification has no Start button | Checked by eye | treadmill |
| Swipe the app away mid-workout | Keeps recording, or the workout is saved cleanly | treadmill |
| 60-minute walk, screen locked | No disconnects, no gap over 5 s | treadmill |
| Leave the dashboard and come back mid-workout | No disconnect, no gap | treadmill |
| `dumpsys` shows type `0x10` | Task 7.2 | |

All on the release build, phone unplugged (the phone acts differently while charging).

## Done when

- [ ] A one-hour locked workout: no disconnects, no gap over 5 seconds
- [ ] All four HyperOS settings done and recorded in `PHASE-00-FINDINGS.md`
- [ ] `RecordingService.start` and `treadmill.start` can only be reached from the Start
      button
