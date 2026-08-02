# Phase 07 — Foreground Service (Flutter track)

> Every long-running test from here on is unreliable without this.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 06

## Goal

Keep BLE alive and recording while the screen is off or the app is
unfocused — identical goal to the original track, for the identical reason:
Android suspends background work aggressively, and a foreground service with
a persistent notification is the OS's own escape hatch from that.

## The concept

Same reasoning as the original: a plain background `Future`/`Timer` with no
foreground service works only while the app is on screen. The moment the
phone locks, Android's power management throttles and eventually kills
background work, OS policy the app can't opt out of by wanting to keep
running. The realistic failure isn't a clean stop — it's a silent one:
`WorkoutSample` rows just stop appearing mid-workout.

Android 14+ requires declaring *which kind* of long-running work a
foreground service is doing. This project needs `connectedDevice`, plus its
own `FOREGROUND_SERVICE_CONNECTED_DEVICE` permission. Get it wrong and
there's no build error, no exception — the failure mode is a silent scan
failure, exactly as easy to lose hours to as it was in the original track.

## Technology decision: `flutter_foreground_task`

**What problem does it solve?** Flutter's plugin architecture doesn't give
Dart code a native `Service` subclass the way MAUI's `[Service(...)]`
attribute does — a long-running Android foreground service either has to be
hand-written in Kotlin and bridged via a platform channel, or driven through
a plugin that already wraps that native code.

**Why are we using it?** `flutter_foreground_task` wraps exactly the native
APIs this phase needs — `startForegroundService`, the mandatory notification
with action buttons, and `foregroundServiceType` declaration — behind a Dart
API, and is the most widely used package for this specific pattern.

**⚠ The open risk, worth naming honestly, the way the original track flagged
LiveCharts2's pre-1.0 status.** `flutter_foreground_task` runs its service
callback in a **separate Dart isolate** from the UI. Isolates don't share
memory — no passing a live object reference across, only messages. That's a
genuine complication this phase's MAUI equivalent never had: `flutter_blue_plus`'s
connection and the `TreadmillService`/`WorkoutEngine`/`WorkoutSampleBuffer`
chain built in Phases 01–06 all currently assume they run on the main
isolate alongside the UI. Two real options, to settle at this phase's start,
not mid-implementation:

1. **Keep the BLE connection on the main isolate**, and have the foreground
   service isolate do *only* the minimum needed to keep Android happy
   (`startForeground()`, the notification, receiving pause/stop taps and
   forwarding them to the main isolate via `FlutterForegroundTask`'s
   port-based messaging). This is the lower-risk option: BLE plugin
   behavior across isolate boundaries is a less-traveled path than
   `flutter_blue_plus`'s documented main-isolate usage.
2. **Move the BLE connection into the service isolate**, closer to the
   original track's "the service owns the connection" architecture. Cleaner
   in principle (recording genuinely can't be affected by UI teardown), but
   means proving `flutter_blue_plus` actually works reliably from a
   background isolate first — check the plugin's own issue tracker for
   current reports before committing to this path.

**Recommendation:** start with option 1. It's a smaller deviation from
what's already built through Phase 06, and the actual requirement (recording
survives the UI going away) is satisfiable either way, since `WorkoutEngine`
and `WorkoutSampleBuffer` don't depend on being on any particular isolate —
they depend on not being torn down, which keeping them main-isolate-resident
plus a foreground service (which prevents the whole process from being
killed) already achieves.

**Alternatives considered:**

1. **`flutter_background_service`** — similar shape, similar isolate model,
   comparable maturity. A reasonable substitute if `flutter_foreground_task`'s
   Android 14+ `foregroundServiceType` support lags behind.
2. **Hand-written native `Service` + `MethodChannel`** — full control, same
   approach the original track used natively in C#, but means owning Kotlin
   service code personally, the exact cost a plugin exists to avoid. Worth
   it only if both packages above prove inadequate for the `connectedDevice`
   type specifically.

## Implementation requirements

Same non-negotiables as the original track:

- `foregroundServiceType="connectedDevice"` **and** the
  `FOREGROUND_SERVICE_CONNECTED_DEVICE` permission.
- `POST_NOTIFICATIONS` runtime permission (Android 13+).
- Start the service **only from a user-visible action** (the Start button).
  Never from the background, never at boot.
- **Never send a treadmill Start command from a notification action.** Pause
  and Stop only.

## Tasks

### 7.1 — Manifest permissions

```xml
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE" />
<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
```

Plus whatever `flutter_foreground_task`'s own setup doc requires in
`AndroidManifest.xml` for its service declaration and
`foregroundServiceType` — follow the package's current install guide for
the exact `<service>` entry, since plugin-generated manifest snippets are
the kind of detail worth verifying against the installed version rather
than copying from memory.

### 7.2 — Service entry point and notification

```dart
@pragma('vm:entry-point')
void startCallback() {
  FlutterForegroundTask.setTaskHandler(WorkoutRecordingTaskHandler());
}

class WorkoutRecordingTaskHandler extends TaskHandler {
  @override
  void onStart(DateTime timestamp, TaskStarter starter) {
    // TODO: nothing BLE-related here under recommendation 1 above — this
    // isolate's job is the notification and relaying pause/stop taps back
    // to the main isolate via FlutterForegroundTask.sendDataToMain(...).
  }

  @override
  void onRepeatEvent(DateTime timestamp) {
    // TODO: update the notification's elapsed/distance/speed text, using
    // the latest values relayed from the main isolate (see 7.3).
  }

  @override
  void onDestroy(DateTime timestamp, bool isTimeout) {}

  @override
  void onNotificationButtonPressed(String id) {
    // TODO: id == 'pause' or 'stop' — forward to the main isolate.
    FlutterForegroundTask.sendDataToMain({'action': id});
  }
}
```

Starting the service, from the Start button's tap handler only:

```dart
Future<void> startRecordingService() async {
  await FlutterForegroundTask.startService(
    notificationTitle: 'MyHi Companion',
    notificationText: 'Recording your workout',
    notificationButtons: [
      const NotificationButton(id: 'pause', text: 'Pause'),
      const NotificationButton(id: 'stop', text: 'Stop'),
    ],
    callback: startCallback,
  );
}
```

### 7.3 — Relaying live data to the notification

On the main isolate, wherever `WorkoutEngine`'s samples are consumed,
forward elapsed/distance/speed to the service isolate periodically (every
few seconds — an update on every ~1 Hz sample is wasteful for a value the
user only glances at):

```dart
FlutterForegroundTask.sendDataToTask({
  'elapsedSeconds': sample.elapsedActiveSeconds,
  'distanceMeters': sample.distanceMeters,
  'speedKph': sample.speedKph,
});
```

`WorkoutRecordingTaskHandler.onReceiveData` (not shown above) receives this
and calls `FlutterForegroundTask.updateService(notificationText: ...)`.

### 7.4 — Receiving pause/stop on the main isolate

```dart
FlutterForegroundTask.addTaskDataCallback((data) {
  if (data case {'action': 'pause'}) workoutEngine.tryPause(WorkoutPauseReason.userRequested);
  if (data case {'action': 'stop'}) workoutEngine.tryStop();
});
```

### 7.5 — `POST_NOTIFICATIONS` runtime permission

```dart
if (!await FlutterForegroundTask.checkNotificationPermission().then((p) => p == NotificationPermission.granted)) {
  await FlutterForegroundTask.requestNotificationPermission();
}
```

Request this from the Start button's tap handler, before calling
`startRecordingService()`. A denied permission on Android 13+ still lets the
foreground service **run** — it just can't show its notification. Recording
can reasonably continue; say so in the UI rather than showing nothing.

## HyperOS setup checklist — `[HUMAN]`, effectively mandatory

Identical to the original track — these are phone-level, not framework-level:

- [ ] Android battery optimisation disabled
- [ ] **Autostart** enabled (HyperOS-specific menu)
- [ ] App's **Battery saver** set to **"No restrictions"**
- [ ] App **locked in Recents**

Verify all four before Phase 14; record their state in `PHASE-00-FINDINGS.md`.

## Tests

Same manual matrix as the original: lock phone and walk 10 minutes (no
disconnect), switch to another app 10 minutes (no disconnect), notification
pause/stop actually works, swipe from Recents mid-workout (service survives
or workout saves cleanly), a full 60-minute locked walk with zero
disconnects and sample gaps under 5s. All `[HUMAN]`.

## Acceptance

- [ ] One-hour locked workout, no disconnect, no sample gap over 5s
- [ ] All four HyperOS boxes ticked and recorded in `PHASE-00-FINDINGS.md`
- [ ] The main-isolate/service-isolate split (recommendation 1, or a
      deliberately chosen alternative) is documented in code, not implicit
