# Phase 07 — Foreground Service

> Every long-running test from here on is unreliable without this, and split screen
> (app visible but not focused) is exactly where Android starts restricting background
> work.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 06

---

## Goal

Keep BLE alive and recording while the screen is off or the app is unfocused.

This phase is almost entirely **Android platform code**, not MAUI XAML: a `Service`
subclass, a manifest permission change, and a notification built with Android's own
`NotificationCompat` APIs. It follows the same "you write the logic, the agent
explains and reviews" rule as everything else in this project; there's just no XAML
surface here for the agent to hand you fully built. The one place declarative XML gets
shown in full below is the manifest diff (task 7.1), for the same reason as UI XAML:
it's config, not logic.

### Understanding what you're building (read this before the tasks)

Android aggressively suspends work in apps that aren't visible on screen, to save
battery. That's exactly what this phase's intro warns about: split screen (app
visible but not focused) is where Android starts restricting background work. By
Phase 06, recording already works (`WorkoutSampleBuffer` and `WorkoutRepository`
are solid), but none of it survives the screen turning off unless something tells
Android to leave the process alone. A **foreground service** with a persistent,
user-visible notification does that: in exchange for a notification the user can
always see and dismiss-proof ("elapsed time, distance, speed," per this phase's
Features list), Android agrees not to kill the process.

**Why a plain background task isn't enough.** Just running the BLE read loop and
the sample buffer on an ordinary background `Task` (no `Service`, no
notification) works fine as long as the app is the thing on screen. The moment
the user locks the phone or switches to another app mid-workout (a 20–60 minute
session, easily), Android's power management throttles and eventually kills
background work regardless of what the app intends. That's enforced OS policy,
not a setting the app can request around. The realistic failure isn't a clean
stop, it's a silent one: the BLE connection drops and `WorkoutSample` rows stop
appearing mid-workout, the exact gap Phase 06's `Flags` bit 0 gap marker exists to
record after the fact rather than prevent. So "just use a background task" isn't
a simpler-but-adequate alternative here. It predictably breaks the one thing this
phase exists for (`../README.md`'s own framing: "every long-running test from here
on is unreliable without this").

A related question: why declare `connectedDevice` specifically, rather than the
plainest possible foreground service? Android 14+ requires apps to state which of
several defined types of long-running work a foreground service is doing, and
background BLE specifically requires the `connectedDevice` type plus its own
`FOREGROUND_SERVICE_CONNECTED_DEVICE` permission (task 7.1). Get the type wrong or
omit it and there's no build error and no exception. This phase's Implementation
requirements call out exactly this: "the failure mode is a silent scan failure
rather than an exception," a far worse debugging session than getting it right
the first time would have cost.

**The pattern, named plainly.** `WorkoutRecordingService` is deliberately both
**started and bound**, two Android service lifecycles layered on one class, named
explicitly in this phase's Learning goals. *Started* (`StartForegroundService`,
task 7.5) means the service keeps running independent of whoever launched it. It
doesn't stop just because the screen that started it goes dark. *Bound*
(`BindService`, task 7.6) means the UI can hold a direct reference to the running
instance (`WorkoutRecordingServiceConnection.Service`) to read live
elapsed/distance/speed for the in-app dashboard, rather than only being able to
fire one-way `Intent`s at it. The cost is real: two lifecycles to reason about at
once, and task 7.6 says as much. A service that's bound but never started dies
the moment the activity unbinds, which is exactly backwards for a recording that
must outlive the app being backgrounded. The payoff matches this project's shape:
recording must survive UI teardown (started), while the dashboard wants
push-style live access when the app *is* in front (bound). One responsibility,
owning the BLE connection and the buffer, gets exposed two different ways to two
different consumers: Android's process manager, and this app's own UI. A one-off
background job with no live UI to talk to, say a single background upload, would
only need "started." Adding a binder for it would be ceremony with no consumer
to use it.

## Learning goals

- Android's `Service` component and the **started + bound** hybrid pattern: started so
  it survives whatever created it, bound so the UI can talk to it directly instead of
  only firing `Intent`s at it
- Foreground services specifically: why they exist (Android otherwise kills background
  work), the mandatory notification, and `foregroundServiceType` as a declared
  contract with the OS about *what kind* of long-running work this is
- `PendingIntent`: how a notification action button hands control back into your app
  code without your app needing to already be in the foreground when the user taps it
- Runtime permissions beyond BLE: `POST_NOTIFICATIONS` (Android 13+), requested with
  the exact same MAUI `Permissions.BasePlatformPermission` pattern Phase 00 already
  used for `BLUETOOTH_SCAN`/`BLUETOOTH_CONNECT`
- Where Android C# attributes end and hand-written manifest XML begins in this
  project: `MainActivity`'s `[Activity(...)]` attribute is already merged into the
  manifest at build time. Permissions are the one thing still hand-declared, and
  this phase adds to that list rather than introducing a new mechanism
- Resolving DI-registered services from inside a class Android constructs for you
  (a `Service` isn't built by the MAUI container, so constructor injection doesn't
  reach it)

## Reference docs

| Topic | Link | Relevant to |
|---|---|---|
| Foreground service types (`connectedDevice`, etc.) | https://developer.android.com/develop/background-work/services/fgs/service-types | Which type this service declares, and why |
| Foreground service types required (Android 14+) | https://developer.android.com/about/versions/14/changes/fgs-types-required | Why omitting the type is a silent failure, not a build error |
| Services overview | https://developer.android.com/develop/background-work/services | Started vs. bound, the two lifecycles this service straddles |
| Foreground services overview | https://developer.android.com/develop/background-work/services/fgs | `startForeground()`, the 5-second rule, why the notification is mandatory |
| Bound services overview | https://developer.android.com/develop/background-work/services/bound-services | Task 7.6 — how the UI gets live access to the running service |
| Foreground Services — .NET for Android | https://learn.microsoft.com/en-us/xamarin/android/app-fundamentals/services/foreground-services | The `[Service(ForegroundServiceType = ...)]` attribute pattern task 7.2 uses |
| `Service.StartForeground` method | https://learn.microsoft.com/en-us/dotnet/api/android.app.service.startforeground | The overload that takes a foreground-service-type parameter — task 7.2 |
| Create a notification (incl. actions) | https://developer.android.com/develop/ui/views/notifications/build-notification | Task 7.3 — the notification and its two action buttons |
| `NotificationCompat.Builder` reference | https://developer.android.com/reference/androidx/core/app/NotificationCompat.Builder | The exact builder API task 7.3's skeleton calls |
| `PendingIntent` reference | https://developer.android.com/reference/android/app/PendingIntent | What an action button actually hands to the OS |
| Notification runtime permission | https://developer.android.com/develop/ui/views/notifications/notification-permission | Task 7.4 — `POST_NOTIFICATIONS` |
| Android: communicate in the background over BLE | https://developer.android.com/develop/connectivity/bluetooth/ble/background | The background BLE constraints this whole phase exists to satisfy |
| Dependency injection in .NET MAUI | https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/dependency-injection | Background for how DI normally works here, before task 7.2 explains the one exception |
| `IPlatformApplication.Current` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.iplatformapplication.current | Task 7.2 — how a `Service` (not constructed by the MAUI container) reaches the DI container anyway |

## Features

- Foreground service started when a workout starts, stopped when it ends
- Persistent notification: elapsed time, distance, speed
- Notification actions: pause / stop
- **BLE connection and sample recording owned by the service, not the UI.** The UI
  binds to it.

---

## Implementation requirements

- `foregroundServiceType="connectedDevice"` **and** the
  `FOREGROUND_SERVICE_CONNECTED_DEVICE` permission. Since Android 15 a generic
  foreground service is not sufficient for background BLE, and **the failure mode is a
  silent scan failure rather than an exception**, easy to misdiagnose for hours.
  (Tasks 7.1, 7.2)
- Pass the type constant to `startForeground()` as well as declaring it. (Task 7.2)
- `POST_NOTIFICATIONS` runtime permission. (Tasks 7.1, 7.4)
- Start the service **only from a user-visible action** (the Start button). Never from
  the background, never from `BOOT_COMPLETED`. (Task 7.5)
- **Never send a treadmill Start command from a notification action.** Pause and stop
  only. (Task 7.2)

**On timeouts:** the 6-hour foreground service limit introduced in Android 15 applies
only to `dataSync` and `mediaProcessing`. `connectedDevice` is not subject to it, so a
two-hour workout is fine and no `onTimeout()` handling is needed. Phase 14 test 2 is
the empirical check.

---

## Your tasks

### 7.1 — Manifest permissions

Touches: `src/MyHi.Companion/Platforms/Android/AndroidManifest.xml`.

Three new permissions. Unlike `MainActivity`'s activity entry, which is generated from
its `[Activity(...)]` C# attribute at build time, permissions in this project are
hand-declared in the manifest. Add these next to the existing
`BLUETOOTH_SCAN`/`BLUETOOTH_CONNECT` lines:

```xml
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE" />
<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
```

`FOREGROUND_SERVICE_CONNECTED_DEVICE` is specific to this service's declared type
(task 7.2). A generic foreground-service permission is not sufficient for background
BLE since Android 15, and the failure mode when it's missing is a **silent** scan
failure, not an exception. `POST_NOTIFICATIONS` is unrelated to BLE; it's the
Android 13+ runtime permission for showing any notification at all, including this
service's mandatory one.

Concrete steps:
1. Open the manifest, add the three lines inside `<manifest>`, alongside the existing
   two `<uses-permission>` entries.
2. Build the app project to confirm the XML is still well-formed:
   ```powershell
   dotnet build src/MyHi.Companion/MyHi.Companion.csproj -f net10.0-android
   ```

### 7.2 — `WorkoutRecordingService` skeleton

Creates: `src/MyHi.Companion/Platforms/Android/WorkoutRecordingService.cs`.

An Android `Service`, declared with its foreground type via a C# attribute, the same
mechanism `MainActivity.cs` uses for `[Activity(...)]`, rather than by hand-editing
the manifest. Note the namespace: `MainActivity.cs` and `MainApplication.cs` both use
plain `namespace MyHi.Companion;` even though they live under `Platforms/Android/`.
Match that existing convention rather than introducing a new one.

```csharp
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace MyHi.Companion;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class WorkoutRecordingService : Service
{
    public const string ActionPause = "com.myhi.companion.action.PAUSE";
    public const string ActionStop = "com.myhi.companion.action.STOP";
    private const int NotificationId = 1001;

    private readonly LocalBinder _binder;

    // TODO: fields for whatever this service needs to own — ITreadmillService,
    // WorkoutRepository, WorkoutSampleBuffer. A Service is instantiated by Android,
    // not by the MAUI DI container, so these can't be constructor parameters here.
    // Resolve them in OnCreate() via
    // IPlatformApplication.Current!.Services.GetRequiredService<T>() instead — see
    // the doc link above for why this is the one place in the app that reaches into
    // the container manually instead of taking a constructor parameter.

    public WorkoutRecordingService() => _binder = new LocalBinder(this);

    public override void OnCreate()
    {
        base.OnCreate();
        // TODO: resolve dependencies, call WorkoutNotificationBuilder.EnsureChannel (7.3)
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        switch (intent?.Action)
        {
            case ActionPause:
                // TODO: call ITreadmillService.PauseAsync(), refresh the notification
                break;
            case ActionStop:
                // TODO: call ITreadmillService.StopAsync(), flush the sample buffer,
                // WorkoutRepository.CompleteWorkout(...), then StopSelf()
                break;
            default:
                // TODO: build the initial notification (task 7.3) and call
                // StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice)
                break;
        }

        return StartCommandResult.Sticky;
    }

    public override IBinder OnBind(Intent? intent) => _binder;

    public override void OnDestroy()
    {
        // TODO: final flush of any buffered samples before the process can die
        base.OnDestroy();
    }

    public sealed class LocalBinder(WorkoutRecordingService service) : Binder
    {
        public WorkoutRecordingService Service => service;
    }
}
```

Concrete steps:
1. Create the file with the shape above.
2. Fill in `OnCreate`'s dependency resolution.
3. Fill in the three `OnStartCommand` branches. Re-read `ITreadmillService`'s doc
   comment on `StartAsync` (in whichever project Phase 01b moved it to): **never call
   it from here.** Pause and Stop only. See the safety note in "Implementation
   requirements" above.
4. Fill in `OnDestroy`'s final flush.
5. Build the app project. A `Service` with no `<service>` manifest entry and no
   `[Service]` attribute silently never runs. Confirm the attribute is actually
   being picked up by checking the merged manifest in
   `obj/Debug/net10.0-android/android/AndroidManifest.xml` after building, if
   anything about task 7.5 later seems not to be starting the service at all.

### 7.3 — Notification with pause/stop actions

Creates: `src/MyHi.Companion/Platforms/Android/WorkoutNotificationBuilder.cs`.

```csharp
using Android.App;
using Android.Content;
using AndroidX.Core.App;

namespace MyHi.Companion;

public static class WorkoutNotificationBuilder
{
    private const string ChannelId = "workout_recording";

    public static void EnsureChannel(Context context)
    {
        // TODO: NotificationManager.CreateNotificationChannel — required once,
        // API 26+, before the first notification on this channel is posted.
        // Importance should be Low: this is a persistent status notification, not
        // an alert, and Low avoids an intrusive sound/heads-up on every update.
    }

    public static Notification Build(Context context, TimeSpan elapsed, double distanceMeters, double speedKph)
    {
        var pauseIntent = BuildActionPendingIntent(context, WorkoutRecordingService.ActionPause, requestCode: 1);
        var stopIntent = BuildActionPendingIntent(context, WorkoutRecordingService.ActionStop, requestCode: 2);

        return new NotificationCompat.Builder(context, ChannelId)
            // TODO: SetSmallIcon, SetOngoing(true), SetContentTitle/SetContentText
            // formatted from elapsed/distanceMeters/speedKph — metric, per this
            // project's rule; convert to display units only if a Phase 08 setting
            // says so, and only in the string you build here, never in the stored data
            .AddAction(0, "Pause", pauseIntent)
            .AddAction(0, "Stop", stopIntent)
            .Build();
    }

    private static PendingIntent BuildActionPendingIntent(Context context, string action, int requestCode)
    {
        // TODO:
        // var intent = new Intent(context, typeof(WorkoutRecordingService)) { Action = action };
        // return PendingIntent.GetService(context, requestCode, intent,
        //     PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        // Immutable is required on API 31+ unless the receiver genuinely needs to
        // fill in the intent, which pause/stop doesn't.
        throw new NotImplementedException();
    }
}
```

Concrete steps:
1. Create the file with the shape above.
2. Fill in `EnsureChannel`. Call it once from `WorkoutRecordingService.OnCreate`.
3. Fill in `Build`'s content fields and `BuildActionPendingIntent`.
4. For `SetSmallIcon`: a real status-bar icon is a small monochrome silhouette
   drawable (`Platforms/Android/Resources/drawable/`), not the launcher icon.
   Reusing `Resource.Mipmap.appicon` as a placeholder is fine to get this task
   working end to end. A proper status-bar glyph isn't this phase's learning goal;
   swap it in later.
5. Have `WorkoutRecordingService` call `Build(...)` again, not just once, whenever
   elapsed/distance/speed change enough to be worth showing, and re-post via
   `NotificationManager.Notify(NotificationId, notification)`. Throttle this: a
   notification update on every ~1 Hz sample is wasteful for a value the user only
   glances at; once every few seconds is plenty.

### 7.4 — `POST_NOTIFICATIONS` runtime permission

Creates: `src/MyHi.Companion/Features/Recording/PostNotificationsPermission.cs`.

Same pattern as `BluetoothScanPermission`/`BluetoothConnectPermission` in
`src/MyHi.Companion/Features/Bluetooth/BluetoothPermissions.cs`: a
`Permissions.BasePlatformPermission` subclass naming the one Android permission it
wraps:

```csharp
namespace MyHi.Companion.Features.Recording;

public sealed class PostNotificationsPermission : Permissions.BasePlatformPermission
{
#if ANDROID
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        ("android.permission.POST_NOTIFICATIONS", true),
    ];
#endif
}
```

Concrete steps:
1. Create the file with the shape above. It's small enough that the shape is most
   of it; the interesting part is step 2.
2. Request it from wherever the user taps Start, following the same "only from a
   user-visible action" rule `BluetoothReadinessService.RequestPermissionsAsync`
   already uses:
   ```csharp
   await Permissions.RequestAsync<PostNotificationsPermission>();
   ```
3. Decide what happens if it's denied. Unlike BLE permission denial (scanning simply
   can't work), a denied `POST_NOTIFICATIONS` on Android 13+ still lets the
   foreground service **run**; it just can't show its notification. Recording can
   reasonably continue; the UI should say so rather than silently showing nothing.

### 7.5 — Starting the service from the UI

Touches: wherever your Phase 04 Start-button command lives.

Concrete steps:
1. Request `PostNotificationsPermission` (7.4) if not already granted.
2. Build an `Intent` targeting `WorkoutRecordingService` and call
   `Context.StartForegroundService(intent)`, **not** `StartService`. That
   distinction matters on API 26+: `StartForegroundService` gives the service a
   five-second window to call `StartForeground()` before the OS considers it
   misbehaving and kills it.
3. Confirm this call site is reachable **only** from the Start button's tap handler,
   never from app launch, never from a restored or backgrounded state. Search the
   codebase for any other place a similar `Intent` could plausibly get built, and
   make sure this is the only one.

### 7.6 — Binding the UI to the running service

Creates: `src/MyHi.Companion/Platforms/Android/WorkoutRecordingServiceConnection.cs`.

The UI needs live access to the service instance, to read its current elapsed
time/distance/speed for the in-app dashboard, not just the notification. That's
what *binding* gets you, as opposed to only firing an `Intent` at it.

```csharp
using Android.Content;
using Android.OS;
using Java.Lang;

namespace MyHi.Companion;

public sealed class WorkoutRecordingServiceConnection : Object, IServiceConnection
{
    public WorkoutRecordingService? Service { get; private set; }

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public void OnServiceConnected(ComponentName? name, IBinder? binder)
    {
        // TODO: cast binder to WorkoutRecordingService.LocalBinder, set Service,
        // raise Connected
    }

    public void OnServiceDisconnected(ComponentName? name)
    {
        // TODO: Service = null; raise Disconnected
    }
}
```

Concrete steps:
1. Create the file with the shape above.
2. Fill in both callbacks.
3. From `MainActivity` (or a thin wrapper it constructs), call
   `BindService(intent, connection, Bind.None)` **after** the service is already
   started (7.5). Binding without also starting means the service dies the moment
   the activity unbinds, wrong for a recording that must survive the app being
   backgrounded.
4. This is genuinely fiddly platform plumbing. Bring what you've written to the
   review checkpoint before wiring a ViewModel to it, rather than debugging silently
   failing binder casts solo for hours.

### Review checkpoint

Before running the `[HUMAN]` tests: the agent reviews the manifest diff, the filled-in
`OnStartCommand`, and the binder wiring together. This is the phase where a silent
Android platform failure (a missing type, a missing permission, a wrong
`PendingIntent` flag) is genuinely easy to lose hours to, and a second pair of eyes
on the exact API calls before the phone comes out is worth it.

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
`../phase-00-probe-app/PHASE-00-FINDINGS.md`.

The app should still **prompt and explain** rather than require them, but on this
device they are effectively mandatory.

---

## Tests

| Test | Expected | |
|------|----------|---|
| Lock phone, walk 10 min | No disconnect, samples continuous | `[HUMAN]` |
| Switch to YouTube 10 min | No disconnect | `[HUMAN]` |
| Notification pause/stop | Works; UI reflects it | `[HUMAN]` |
| Swipe app from recents mid-workout | Service survives, or workout saved cleanly | `[HUMAN]` |
| 60-minute locked walk | Zero disconnects, sample gaps < 5 s | `[HUMAN]` |
| Service owns the connection | Unit/integration: UI teardown does not drop BLE | |

## Acceptance

- [ ] One-hour locked workout, no disconnect, no sample gap over 5 s
- [ ] All four HyperOS boxes ticked and recorded in `../phase-00-probe-app/PHASE-00-FINDINGS.md`
