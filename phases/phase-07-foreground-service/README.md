# Phase 07 — Foreground Service

> Every long-running test from here on is unreliable without this, and split screen —
> app visible but not focused — is exactly where Android starts restricting background
> work.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 06

---

## Goal

Keep BLE alive and recording while the screen is off or the app is unfocused.

## Features

- Foreground service started when a workout starts, stopped when it ends
- Persistent notification: elapsed time, distance, speed
- Notification actions: pause / stop
- **BLE connection and sample recording owned by the service, not the UI.** The UI
  binds to it.

---

## Implementation requirements

- `foregroundServiceType="connectedDevice"` in the manifest **and** the
  `FOREGROUND_SERVICE_CONNECTED_DEVICE` permission. Since Android 15 a generic
  foreground service is not sufficient for background BLE, and **the failure mode is a
  silent scan failure rather than an exception** — easy to misdiagnose for hours.
- Pass the type constant to `startForeground()` as well as declaring it in the manifest.
- `POST_NOTIFICATIONS` runtime permission.
- Start the service **only from a user-visible action** (the Start button). Never from
  the background, never from `BOOT_COMPLETED`.
- **Never send a treadmill Start command from a notification action.** Pause and stop
  only.

**On timeouts:** the 6-hour foreground service limit introduced in Android 15 applies
only to `dataSync` and `mediaProcessing`. `connectedDevice` is not subject to it, so a
two-hour workout is fine and no `onTimeout()` handling is needed. Phase 14 test 2 is
the empirical check.

---

## HyperOS setup checklist — `[HUMAN]`, and effectively mandatory

The target phone is a Poco X6 Pro 5G on HyperOS. **Standard Android battery
optimisation being disabled is not sufficient.** These are separate Xiaomi controls and
they are the ones that actually kill long-running services.

- [ ] Android battery optimisation disabled *(already confirmed done)*
- [ ] **Autostart** enabled for the app — separate HyperOS menu
- [ ] App's **Battery saver** set to **"No restrictions"** — HyperOS's own setting in
      app info, distinct from Android's toggle
- [ ] App **locked in Recents** (padlock in the task switcher) to resist memory reclaim

*These controls exist and matter; the exact menu names vary between HyperOS versions.*

**Verify all four before Phase 14.** If skipped, endurance tests fail for reasons
unrelated to the code and the resulting debugging is wasted. Record their state in
`../../DEVICE.md`.

The app should still **prompt and explain** rather than require them — but on this
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
- [ ] All four HyperOS boxes ticked and recorded in `../../DEVICE.md`
