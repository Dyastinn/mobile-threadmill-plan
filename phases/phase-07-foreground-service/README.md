# Phase 07 — Foreground Service

> Every long-running test from here on is unreliable without this, and split screen
> (app visible but not focused) is exactly where Android starts restricting background
> work.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 06 · **Shaped by:** V5

---

## Goal

Keep BLE alive, JavaScript running, and the workout recording while the screen is off
or the app is unfocused.

Phase 00's task 0.12 already built a **spike** of this: a local Expo module whose
Kotlin service holds a foreground notification and keeps a headless JS task alive, and
runbook Part H measured whether that was enough on this phone. This phase is you
taking ownership of that module and finishing it: reading every line of the spike with
the agent, then adding what a spike skips (live notification text, pause/stop
actions, the notification permission, a start path that's only ever a deliberate tap,
and the JavaScript side that ties it to the workout).

**First, read V5 in `../phase-00-probe-app/PHASE-00-FINDINGS.md`.**

- **Holds:** this README as written.
- **Partly holds:** this README as written, but start with a review checkpoint on
  exactly which signal stalled. The fix is usually one of the HyperOS controls, or the
  wake lock.
- **Fails:** skip to "If V5 failed" at the bottom. The service's job grows: BLE and
  recording move into Kotlin.

This phase is mostly **Android platform code in Kotlin**, plus one TypeScript file. It
follows the same "you write the logic, the agent explains and reviews" rule as
everything else in this project; Kotlin is new, so every Kotlin task starts with the
agent walking through the syntax it uses (it reads a lot like TypeScript: `val`/`var`,
`fun`, `?` for nullable, `when` for `switch`).

### Understanding what you're building (read this before the tasks)

Android aggressively suspends work in apps that aren't visible on screen, to save
battery. By Phase 06, recording already works (`WorkoutRecorder` and
`WorkoutRepository` are solid), but none of it survives the screen turning off
unless something tells Android to leave the process alone. A **foreground service**
with a persistent, user-visible notification does that: in exchange for a
notification the user can always see ("elapsed time, distance, speed," per this
phase's Features list), Android agrees not to kill the process, and treats it as
something the user is actively using.

In React Native there's a second half to it, which is what Phase 00's spike existed to
test. Keeping the *process* alive isn't the same as keeping your *JavaScript* running
normally. React Native stops firing `setTimeout`/`setInterval` when the activity is
paused, unless a **headless JS task** is in progress. So the service does two things
at once: it's a foreground service (the process survives) and it runs a headless task
that deliberately never finishes until the workout ends (the timers keep firing).
`HeadlessJsTaskService`, the React Native class the spike extends, also holds a
**partial wake lock** while the task runs, so the CPU doesn't doze off between BLE
notifications.

**Why a plain background task isn't enough.** Just running the BLE listeners and the
sample buffer in ordinary JavaScript works fine as long as the app is the thing on
screen. The moment the user locks the phone or switches to another app mid-workout (a
20–60 minute session, easily), Android's power management throttles and eventually
kills background work regardless of what the app intends, and React Native's timers
stop on their own even sooner. That's enforced platform behaviour, not a setting the
app can request around. The realistic failure isn't a clean stop, it's a silent one:
the reconnect backoff never fires, the flush timer never fires, and `WorkoutSample`
rows stop appearing mid-workout. So "just keep it in JavaScript" isn't a
simpler-but-adequate alternative here. It predictably breaks the one thing this phase
exists for (`../README.md`'s own framing: "every long-running test from here on is
unreliable without this").

A related question: why declare `connectedDevice` specifically, rather than the
plainest possible foreground service? Android 14+ requires apps to state which of
several defined types of long-running work a foreground service is doing, and
background BLE specifically requires the `connectedDevice` type plus its own
`FOREGROUND_SERVICE_CONNECTED_DEVICE` permission. Get the type wrong or omit it and
there's no build error. This phase's Implementation requirements call out exactly
this: "the failure mode is a silent scan failure rather than an exception," a far
worse debugging session than getting it right the first time would have cost.

**The pattern, named plainly.** The service is **started, not bound**, and that's a
simplification worth noticing. In a native Android app (or the MAUI version of this
plan), the service would *own* the BLE connection and the UI would bind to it to read
live values: two lifecycles to reason about at once. Here, the BLE connection, the
engine and the recorder already live in the JavaScript runtime as module-level
objects (`services.tsx`), and that runtime belongs to the app's process, not to any
screen. The UI already has direct access to them. So the service's only jobs are
"keep the process and the timers alive" and "show the notification and forward its
buttons." One responsibility, one lifecycle. The cost is a design constraint you must
keep: nothing that has to survive the screen turning off may live inside a React
component. It all lives in `services.tsx`, where it already is.

## Learning goals

- **Android's `Service` component**, and the foreground-service contract: the
  mandatory notification, the five-second rule after `startForegroundService`, and
  `foregroundServiceType` as a declared contract with the OS about *what kind* of
  long-running work this is
- **Headless JS**: what React Native's `HeadlessJsTaskService` does (start a task,
  hold a wake lock, stop itself when the task finishes), and why a task that never
  settles is the keep-alive
- **`PendingIntent`**: how a notification action button hands control back into your
  app without the app needing to be in the foreground when the user taps it
- **The Expo Modules API**: how a Kotlin function becomes callable from TypeScript,
  and how a Kotlin event reaches a TypeScript listener
- **Runtime permissions beyond BLE**: `POST_NOTIFICATIONS` (Android 13+), requested
  with the same `PermissionsAndroid` pattern Phase 00 used for
  `BLUETOOTH_SCAN`/`BLUETOOTH_CONNECT`
- **Where native configuration lives** under Continuous Native Generation: the module's
  own `AndroidManifest.xml` is merged into the app's at build time, so the permissions
  and the `<service>` entry travel with the module and never require editing
  `src/android/`

## Reference docs

| Topic | Link | Relevant to |
|---|---|---|
| Foreground services overview | https://developer.android.com/develop/background-work/services/fgs | `startForeground()`, the 5-second rule, why the notification is mandatory |
| Foreground service types (`connectedDevice`, etc.) | https://developer.android.com/develop/background-work/services/fgs/service-types | Which type this service declares, and the permissions it requires first |
| Foreground service types required (Android 14+) | https://developer.android.com/about/versions/14/changes/fgs-types-required | Why omitting the type is a silent failure, not a build error |
| Services overview | https://developer.android.com/develop/background-work/services | Started vs. bound, and why this one is only started |
| Headless JS (React Native) | https://reactnative.dev/docs/headless-js-android | `HeadlessJsTaskService`, `getTaskConfig`, `AppRegistry.registerHeadlessTask` |
| Expo Modules API: overview | https://docs.expo.dev/modules/overview/ | What a local module is |
| Expo Modules API: module definition | https://docs.expo.dev/modules/module-api/ | `Function`, `Events`, `sendEvent`: task 7.3 |
| Create a notification (incl. actions) | https://developer.android.com/develop/ui/views/notifications/build-notification | Task 7.3: the notification and its two action buttons |
| `NotificationCompat.Builder` reference | https://developer.android.com/reference/androidx/core/app/NotificationCompat.Builder | The exact builder API task 7.3 calls |
| `PendingIntent` reference | https://developer.android.com/reference/android/app/PendingIntent | What an action button actually hands to the OS |
| Notification runtime permission | https://developer.android.com/develop/ui/views/notifications/notification-permission | Task 7.4: `POST_NOTIFICATIONS` |
| Android: communicate in the background over BLE | https://developer.android.com/develop/connectivity/bluetooth/ble/background | The background BLE constraints this whole phase exists to satisfy |
| Doze and App Standby | https://developer.android.com/training/monitoring-device-state/doze-standby | What the wake lock and the battery-optimisation exemption protect against |
| Kotlin basic syntax | https://kotlinlang.org/docs/basic-syntax.html | Read once before task 7.2 |

## Features

- Foreground service started when a workout starts, stopped when it ends
- Persistent notification: elapsed time, distance, speed
- Notification actions: pause / stop
- **BLE connection and sample recording keep running with the screen off**, because
  the process and the JavaScript timers are both kept alive

---

## Implementation requirements

- `foregroundServiceType="connectedDevice"` **and** the
  `FOREGROUND_SERVICE_CONNECTED_DEVICE` permission. A generic foreground service is
  not sufficient for background BLE on current Android, and **the failure mode is a
  silent scan failure rather than an exception**, easy to misdiagnose for hours.
  (Task 7.2)
- Pass the type constant to `startForeground()` as well as declaring it. (Task 7.2)
- On Android 14+, a `connectedDevice` service may only start once a Bluetooth
  runtime permission (`BLUETOOTH_CONNECT` here) is already granted, or
  `startForeground` throws a `SecurityException`. (Task 7.5)
- `POST_NOTIFICATIONS` runtime permission. (Task 7.4)
- Start the service **only from a user-visible action** (the Start button). Never from
  the background, never from `BOOT_COMPLETED`, never from app launch. (Task 7.5)
- **Never send a treadmill Start command from a notification action.** Pause and stop
  only. (Task 7.3)
- The headless task **must not settle** until the workout ends. A task that resolves
  early ends the keep-alive, and the timers stop. (expo/expo#51304 is a real-world
  case of exactly this: a no-op task that resolved immediately.)

**On timeouts:** the 6-hour foreground service limit introduced in Android 15 applies
only to `dataSync` and `mediaProcessing`. `connectedDevice` is not subject to it, so a
long workout is fine and no `onTimeout()` handling is needed.

---

## Your tasks

### 7.1 — Read the spike, line by line (review checkpoint first)

Not new code. Before changing anything, walk through what Phase 00 built with the
agent, until you could explain each line to someone else:

1. `src/modules/recording-service/expo-module.config.json`: how Expo's autolinking
   finds the module.
2. `src/modules/recording-service/android/src/main/AndroidManifest.xml`: the four
   permissions and the `<service>` entry. Why `android:exported="false"`?
3. `WorkoutRecordingService.kt`: `onStartCommand`, `getTaskConfig`, and what the
   `HeadlessJsTaskService` base class does in between (open its source in
   `node_modules/react-native/ReactAndroid/.../HeadlessJsTaskService` if the agent's
   explanation leaves a gap).
4. `RecordingServiceModule.kt`: how `Function("start") { ... }` becomes something
   TypeScript can call.
5. `src/features/background/keepAlive.ts` and `src/index.ts`: why the task has to be
   registered before `expo-router/entry` is imported.

Write down, in your own words, one paragraph: what keeps the process alive, what
keeps the timers running, and what keeps the CPU awake. Three different mechanisms;
the paragraph is the checkpoint.

### 7.2 — Verify the manifest and the service type

Touches: nothing, if the spike is right. Mostly verification.

1. Build, then confirm the merged result on the phone rather than trusting the XML:
   ```powershell
   adb shell dumpsys package com.myhi.companion | Select-String -Pattern "FOREGROUND_SERVICE|POST_NOTIFICATIONS|WAKE_LOCK"
   ```
   All four permissions should be listed as requested.
2. Start a fake workout with the keep-alive on, then:
   ```powershell
   adb shell dumpsys activity services com.myhi.companion
   ```
   Look for `isForeground=true` and `foregroundServiceType=0x00000010` (that's
   `connectedDevice`). If the type is missing, the declaration or the
   `startForeground` call is wrong, and nothing else in this phase will hold up.
3. In `WorkoutRecordingService.onStartCommand`, confirm it returns
   `START_NOT_STICKY`, not `START_STICKY`. If Android kills the process anyway, the
   workout's JavaScript state is gone with it; restarting an empty service would only
   start a keep-alive with nothing to keep alive, and on Android 12+ starting a
   foreground service from the background can throw. Recovery is Phase 06's job
   (the `Status = 0` row on next launch), not the service's.

### 7.3 — Notification content and pause/stop actions

Touches: `WorkoutRecordingService.kt`, `RecordingServiceModule.kt`,
`src/modules/recording-service/index.ts`.

The spike posts a fixed "Keep-alive spike" notification with no buttons. The real one
shows live elapsed time, distance and speed, and has Pause and Stop actions whose taps
reach your TypeScript.

The service's final shape (the parts marked `TODO` are yours):

```kotlin
package expo.modules.recordingservice

class WorkoutRecordingService : HeadlessJsTaskService() {
  companion object {
    const val ACTION_PAUSE = "com.myhi.companion.action.PAUSE"
    const val ACTION_STOP = "com.myhi.companion.action.STOP"
    const val NOTIFICATION_ID = 1001
    const val CHANNEL_ID = "workout_recording"

    /** Set by RecordingServiceModule so notification taps reach JavaScript. */
    var onAction: ((String) -> Unit)? = null
  }

  override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
    when (intent?.action) {
      ACTION_PAUSE -> { onAction?.invoke("pause"); return START_NOT_STICKY }
      ACTION_STOP -> { onAction?.invoke("stop"); return START_NOT_STICKY }
    }
    // The initial start. startForeground FIRST (the 5-second rule), then let
    // HeadlessJsTaskService start the keep-alive task and take the wake lock.
    // TODO: ensure the channel exists (WorkoutNotification.ensureChannel), build the
    // first notification, and call
    // ServiceCompat.startForeground(this, NOTIFICATION_ID, notification,
    //     ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
    return super.onStartCommand(intent, flags, startId)
  }

  override fun getTaskConfig(intent: Intent?): HeadlessJsTaskConfig? =
    // name, extras, timeout 0 = never time out, allowed while the app is in the foreground
    HeadlessJsTaskConfig("RecordingKeepAlive", Arguments.createMap(), 0, true)
}
```

The early `return` for the two actions matters: `super.onStartCommand` asks
`getTaskConfig` for a task and starts it, so letting an action intent fall through
would start a *second* keep-alive task on every tap.

And a small notification helper, `WorkoutNotification.kt`:

```kotlin
object WorkoutNotification {
  fun ensureChannel(context: Context) {
    // TODO: NotificationManagerCompat.from(context).createNotificationChannel(...)
    // with IMPORTANCE_LOW: a persistent status notification, not an alert. Low avoids
    // a sound or heads-up on every update.
  }

  fun build(context: Context, title: String, text: String): Notification {
    val pause = actionIntent(context, WorkoutRecordingService.ACTION_PAUSE, requestCode = 1)
    val stop = actionIntent(context, WorkoutRecordingService.ACTION_STOP, requestCode = 2)
    return NotificationCompat.Builder(context, WorkoutRecordingService.CHANNEL_ID)
      // TODO: setSmallIcon, setOngoing(true), setOnlyAlertOnce(true),
      // setContentTitle(title), setContentText(text)
      .addAction(0, "Pause", pause)
      .addAction(0, "Stop", stop)
      .build()
  }

  private fun actionIntent(context: Context, action: String, requestCode: Int): PendingIntent {
    // TODO:
    // val intent = Intent(context, WorkoutRecordingService::class.java).setAction(action)
    // return PendingIntent.getService(context, requestCode, intent,
    //     PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    // FLAG_IMMUTABLE is required on API 31+ unless the receiver genuinely needs to fill
    // in the intent, which pause/stop doesn't.
    TODO()
  }
}
```

The module gains an `update` function and an event:

```kotlin
class RecordingServiceModule : Module() {
  override fun definition() = ModuleDefinition {
    Name("RecordingService")
    Events("onAction")

    OnCreate {
      WorkoutRecordingService.onAction = { action -> sendEvent("onAction", mapOf("action" to action)) }
    }

    Function("start") { title: String, text: String -> /* from the spike */ }
    Function("update") { title: String, text: String ->
      // TODO: NotificationManagerCompat.from(context).notify(NOTIFICATION_ID,
      //   WorkoutNotification.build(context, title, text))
      // Re-posting with the same id replaces the notification in place.
    }
    Function("stop") { /* from the spike */ }
  }
}
```

and `src/modules/recording-service/index.ts` exports a typed
`addActionListener(listener: (action: 'pause' | 'stop') => void)` over the module's
event, alongside `start`, `update` and `stop`.

Concrete steps:
1. Fill in the `TODO`s above, one file at a time, building after each
   (`npx expo run:android --device`; native changes need a rebuild, Fast Refresh won't
   pick them up).
2. For `setSmallIcon`: a real status-bar icon is a small monochrome silhouette
   drawable. Using `android.R.drawable.stat_notify_sync` as a placeholder is fine to
   get this working end to end; a proper glyph is Phase 13's concern.
3. Throttle updates from the TypeScript side (task 7.6): a notification update on
   every 1 Hz sample is wasteful for a value the user only glances at; once every 5
   seconds is plenty.

### 7.4 — `POST_NOTIFICATIONS` runtime permission

Touches: `src/features/workout/workoutPermissions.ts` (new).

Same pattern as Phase 00's `bluetoothPermissions.ts`:
`PermissionsAndroid.request(PermissionsAndroid.PERMISSIONS.POST_NOTIFICATIONS)`.

Concrete steps:
1. Write `ensureWorkoutPermissions(): Promise<{ bluetooth: boolean; notifications: boolean }>`,
   which checks (and if needed requests) `BLUETOOTH_CONNECT` and
   `POST_NOTIFICATIONS`.
2. Call it only from the Start button's handler (task 7.5), following the same "only
   from a user-visible action" rule as the Bluetooth prompt.
3. Decide what happens if notifications are denied. Unlike BLE permission denial
   (nothing can work), a denied `POST_NOTIFICATIONS` still lets the foreground service
   **run**; it just can't show its notification, so the user loses the Pause/Stop
   buttons. Recording can reasonably continue; the UI should say so rather than
   silently showing nothing. If `BLUETOOTH_CONNECT` is missing, don't start at all:
   `startForeground` would throw.

### 7.5 — Starting a workout: one deliberate tap

Touches: `src/features/workout/workoutSession.ts` (new, yours),
`src/features/workout/WorkoutButton.tsx` (UI, below).

Until now, nothing in the product UI actually starts a workout. `WorkoutSession` is
the one place that does, and it's the only caller of `RecordingService.start()` and of
`treadmill.start()` in the whole app.

```ts
export class WorkoutSession {
  constructor(
    private readonly engine: WorkoutEngine,
    private readonly treadmill: TreadmillService,
    private readonly log: Logger,
  ) {}

  /** Called ONLY from the Start button's onPress. */
  async start(): Promise<{ ok: boolean; message: string | null }> {
    // TODO, in order:
    // 1. const perms = await ensureWorkoutPermissions(); no bluetooth -> return a message
    // 2. RecordingService.start('Workout', 'Starting...'): the service starts the
    //    'RecordingKeepAlive' headless task itself (getTaskConfig); there's no separate
    //    JS call to start the keep-alive, only stopKeepAlive() to end it
    // 3. engine.tryStart()
    // 4. if the treadmill has control: await treadmill.start(), then re-send the target
    //    speed if V2's "Start doesn't keep the target" finding still holds
    // 5. if anything after step 2 fails: stop the keep-alive and the service again,
    //    so a failed start never leaves a notification for a workout that isn't running
  }

  async stop(): Promise<void> {
    // TODO: treadmill.stop() (if controlled), engine.tryStop(); the keep-alive and
    // the service are stopped by the 'finished' handler in task 7.6, not here, so a
    // workout that finishes some other way (grace timer, safety key) cleans up too
  }
}
```

Concrete steps:
1. Write `WorkoutSession` and create it in `services.tsx` as `session` (the name the
   button below reads).
2. Search the codebase for every call to `RecordingService.start` and
   `treadmill.start`. Each must be reachable **only** from `WorkoutSession.start`,
   which must be reachable **only** from a button's `onPress`: never from app launch,
   never from a restored state, never from a notification, never from reconnect.
3. The UI (written in full), `src/features/workout/WorkoutButton.tsx`:

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

   Add `<WorkoutButton />` to `DashboardScreen.tsx` above `<TreadmillControls />`.

### 7.6 — The TypeScript side: lifecycle and notification text

Touches: `workoutSession.ts`.

Concrete steps:
1. In the constructor, subscribe to `engine.onStateChange`. On `'finished'`:
   `stopKeepAlive()` and `RecordingService.stop()`. Every way a workout can end (Finish
   button, notification Stop, grace-timer expiry, safety key) goes through
   `'finished'`, so this one handler is where cleanup can't be forgotten.
2. Subscribe to `RecordingService.addActionListener`. `'pause'` →
   `engine.tryPause('userRequested')` and `treadmill.pause()` (if controlled);
   `'stop'` → the same as `stop()`. There is no `'start'` action, and there must never
   be one (Implementation requirements above).
3. Subscribe to `treadmill.onSample` and, at most every 5 s while a workout is running,
   call `RecordingService.update(title, text)` with elapsed time, distance and speed,
   formatted with Phase 03's `format.ts`. Metric, per this project's rule (Phase 08
   adds the unit setting; convert only in the string you build here, never in stored
   data).
4. Keep every unsubscribe function and call them in a `dispose()`.

### Review checkpoint

Before running the `[HUMAN]` tests: the agent reviews the Kotlin you wrote, the
`dumpsys` output from task 7.2, and `workoutSession.ts` together. This is the phase
where a silent Android platform failure (a missing type, a missing permission, a wrong
`PendingIntent` flag, a keep-alive task that settles early) is genuinely easy to lose
hours to, and a second pair of eyes on the exact API calls before the phone comes out
is worth it.

---

## HyperOS setup checklist — `[HUMAN]`, and effectively mandatory

The target phone is a Poco X6 Pro 5G on HyperOS. **Standard Android battery
optimisation being disabled is not sufficient.** These are separate Xiaomi controls and
they are the ones that actually kill long-running services.

- [ ] Android battery optimisation disabled *(already confirmed done)*
- [ ] **Autostart** enabled for the app (separate HyperOS menu)
- [ ] App's **Battery saver** set to **"No restrictions"** (HyperOS's own setting in
      app info, distinct from Android's toggle)
- [ ] App **locked in Recents** (padlock in the task switcher) to resist memory reclaim

*These controls exist and matter; the exact menu names vary between HyperOS versions.*

**Verify all four before Phase 14.** If skipped, endurance tests fail for reasons
unrelated to the code and the resulting debugging is wasted. Record their state in
`../phase-00-probe-app/PHASE-00-FINDINGS.md`. (They should already be set from runbook
Part H. Re-check after any app reinstall: some HyperOS versions reset them.)

The app should still **prompt and explain** rather than require them, but on this
device they are effectively mandatory. A one-time "Before your first workout" screen
with a button per setting (`Linking.openSettings()` gets you to the app's info page)
is a reasonable Phase 13 addition.

---

## If V5 failed: BLE and recording move into Kotlin

Only read this if runbook Part H failed. The keep-alive kept the process alive but
JavaScript still didn't get BLE events or timer ticks reliably, so the work has to
happen where Android *does* keep running it: inside the service.

What changes, at the level of the plan (the details get written with the agent when
it's actually needed, not speculatively now):

- The Kotlin module grows a small GATT client (connect, subscribe to `0x2ACD`/`0x2ADA`,
  write the control point) and buffers raw notifications in memory, flushing them to
  the same SQLite database (Android's own `SQLiteDatabase`, same schema).
- TypeScript stops listening to ble-manager during a workout and instead reads
  "current sample" from the module when the screen is on.
- `TreadmillService` stays the seam: a new `NativeTreadmillService` implements it over
  the module, and nothing above it changes. This is exactly the situation the seam was
  built for.

Expect this to roughly double Phase 07's size. Re-plan the phase with the agent before
starting.

---

## Tests

| Test | Expected | |
|------|----------|---|
| Lock phone, walk 10 min | No disconnect, samples continuous | `[HUMAN]` |
| Switch to YouTube 10 min | No disconnect | `[HUMAN]` |
| Notification pause/stop | Works; UI reflects it | `[HUMAN]` |
| Notification has no Start action | Confirmed by eye | `[HUMAN]` |
| Swipe app from recents mid-workout | Service survives and recording continues, or workout saved cleanly | `[HUMAN]` |
| 60-minute locked walk | Zero disconnects, sample gaps < 5 s | `[HUMAN]` |
| Leaving the dashboard doesn't drop BLE | Navigate away and back mid-workout; no disconnect, no gap | `[HUMAN]` |
| `dumpsys` shows `connectedDevice` | `foregroundServiceType=0x00000010` (task 7.2) | |

All on the release APK, phone unplugged (Doze doesn't happen on a charger).

## Acceptance

- [ ] One-hour locked workout, no disconnect, no sample gap over 5 s
- [ ] All four HyperOS boxes ticked and recorded in `../phase-00-probe-app/PHASE-00-FINDINGS.md`
- [ ] `RecordingService.start` and `treadmill.start` are reachable only from the Start
      button
