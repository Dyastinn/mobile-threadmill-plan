# Phase 02 — Connection Hardening (Flutter track)

**Hardware:** required · **Size:** M · **Blocked by:** Phase 01

## Goal

Same as the original track: a connection that survives range loss, Bluetooth
toggles, treadmill power cycles, and app restarts. Phase 00 connects once, by
hand; this phase makes it stay connected and come back on its own.

## The concept

`ReconnectionManager` wraps `TreadmillService` rather than adding retry logic
inside it — composition, not modification, so a bug in the new retry loop
can't corrupt the already-working "connect once, cleanly" path. Backoff
schedule: 1s, 2s, 4s, 8s, 16s, then a steady 30s, capped and cancellable —
fast recovery for "walked out of range and back," a sustainable low rate when
the treadmill is genuinely off for a while. Same Retry-pattern-with-backoff
reasoning as any client depending on something unreliable; not BLE-specific.

## Reference docs

- [`../../../phases/phase-02-connection-hardening/README.md`](../../../phases/phase-02-connection-hardening/README.md) — connection sequence, GATT 133 mitigations, application error codes. Protocol/platform-level facts, framework-independent.
- [`../../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](../../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md) — MAC/address type, Part E resilience results.

## Tasks

### 2.1 — Remember the last device

`shared_preferences` is this track's `Preferences` equivalent:

```dart
class LastDevicePreferences {
  static const _macKey = 'lastDeviceMac';
  static const _nameKey = 'lastDeviceName';

  Future<void> save(String mac, String? name) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_macKey, mac);
    if (name != null) await prefs.setString(_nameKey, name);
  }

  Future<(String?, String?)> load() async {
    final prefs = await SharedPreferences.getInstance();
    return (prefs.getString(_macKey), prefs.getString(_nameKey));
  }
}
```

If `PHASE-00-FINDINGS.md`'s address type is "random resolvable," match by
name instead of MAC — same caveat as the original track.

### 2.2 — `reconnectBackoff`: a pure, testable function

Lives in `myhi_companion_core` — `dart test`, no Flutter/Android needed:

```dart
Duration reconnectBackoff(int attemptNumber) {
  if (attemptNumber < 1) throw ArgumentError('attemptNumber must be >= 1');
  const steps = [1, 2, 4, 8, 16];
  if (attemptNumber <= steps.length) {
    return Duration(seconds: steps[attemptNumber - 1]);
  }
  return const Duration(seconds: 30);
}
```

Test covers attempts 1–5 against the schedule, plus attempts 6 and 20 both
returning 30s.

### 2.3 — `ReconnectionManager`

Owns the "stay connected" policy on top of `TreadmillService`. Dart has no
direct `CancellationTokenSource` equivalent for cancelling an in-flight
`Future.delayed`; this class uses a **generation counter** instead — each
backoff loop captures its own generation number at start and checks it
hasn't been superseded before each retry. Simpler than juggling cancellation
tokens across delays, and just as effective for "stop the old loop when a
new one starts":

```dart
class ReconnectionManager {
  final TreadmillService _service;
  int _generation = 0;
  bool _userInitiatedDisconnect = false;
  final _stateController = StreamController<ConnectionState>.broadcast();

  ReconnectionManager(this._service) {
    _service.connectionStateChanges.listen(_onInnerStateChanged);
  }

  Stream<ConnectionState> get stateChanges => _stateController.stream;

  void _onInnerStateChanged(ConnectionStateChange change) {
    _stateController.add(change.state);
    if (change.state == ConnectionState.disconnected && !_userInitiatedDisconnect) {
      _runBackoffLoop(++_generation);
    }
  }

  Future<void> _runBackoffLoop(int myGeneration) async {
    var attempt = 1;
    while (myGeneration == _generation) {
      await Future.delayed(reconnectBackoff(attempt));
      if (myGeneration != _generation) return; // superseded by a newer loop
      try {
        // TODO: look up the remembered device (LastDevicePreferences) and
        // await _service.connect(deviceId). Success ends the loop here —
        // the state-change handler above won't schedule another one once
        // state reaches `ready`.
        return;
      } catch (_) {
        attempt++;
      }
    }
  }

  Future<void> disconnect() async {
    _userInitiatedDisconnect = true;
    _generation++; // invalidates any in-flight backoff loop
    await _service.disconnect();
    _userInitiatedDisconnect = false;
  }
}
```

### 2.4 — Confirm GATT 133 mitigations still hold

Not new code — re-check `flutter_blue_plus` connect calls still use
non-autoconnect behavior, a ~200 ms delay before service discovery, and a
full disconnect-and-dispose before reconnecting. `ReconnectionManager` is
about to call this path repeatedly instead of once by hand.

### 2.5 — Re-issue Request Control after reconnect

Track whether control was held at the moment of disconnect; if so, re-run
the Request Control handshake once `ConnectionState.ready` is reached again,
before re-enabling any control UI.

### 2.6 — Fix the production scan filter

`PHASE-00-FINDINGS.md` V4 decides service-UUID vs. name-prefix filtering.
Hardcode that choice in `ReconnectionManager`'s device lookup; the scan
screen keeps all three modes for debugging only, never for this path.

### 2.7 — Connection indicator widget

```dart
class ConnectionIndicator extends ConsumerWidget {
  const ConnectionIndicator({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(connectionStateProvider);
    final connected = state == ConnectionState.ready;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 12,
          height: 12,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            border: Border.all(color: Theme.of(context).dividerColor, width: 1.5),
            color: connected ? Theme.of(context).colorScheme.onSurface : Colors.transparent,
          ),
        ),
        const SizedBox(width: 8),
        Text(state.name, style: Theme.of(context).textTheme.bodySmall),
      ],
    );
  }
}
```

Filled circle = ready; outline-only = anything else. Convey state without a
second color, same rule the original track's monochrome theme used — worth
carrying forward once this track has its own theme doc.

## Tests

Same manual matrix as the original: connect, disconnect, walk out of range
and return, toggle Bluetooth, app restart, treadmill power-cycle, 30-minute
idle hold — all `[HUMAN]`. Automated: `reconnectBackoff`'s schedule and cap
(`dart test`), plus a generation-counter test proving that starting a second
backoff loop actually stops the first (only one `connect` call should land
after both are given time to run).

## Acceptance

- [ ] Connects on 10 of 10 attempts
- [ ] Recovers from all four disruption tests
- [ ] Backoff is capped and cleanly superseded by a newer loop
- [ ] Scan filter choice documented in `PHASE-00-FINDINGS.md`
