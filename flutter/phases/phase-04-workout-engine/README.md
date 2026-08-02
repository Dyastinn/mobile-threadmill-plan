# Phase 04 — Workout Engine (Flutter track)

**Hardware:** none for development · **Size:** M · **Blocked by:** Phases 01, 03
**Hard dependency:** V1 counter semantics from `PHASE-00-FINDINGS.md`

## Goal

Workout lifecycle, **independent of connection lifecycle**. This entire phase
is logic, lives entirely in `myhi_companion_core`, and is unit-testable with
`dart test` — no widgets, no Flutter dependency at all.

## The concept

`WorkoutEngine` tracks two genuinely separate things: what stage the workout
is at (`idle`/`active`/`paused`/`finished`) and whether the app can currently
see the treadmill. A dropped connection for a few seconds doesn't cancel the
workout — it waits a beat (a 60-second grace window) before assuming the
session is over. Two small state machines running concurrently, bridged only
by `WorkoutPauseReason`, rather than one combined enum: they vary
independently (`active`+disconnected is a real, common state; cramming both
into one enum means reasoning about a combinatorial grid most of whose cells
never happen).

## Workout state machine

```
idle --start--> active --pause--> paused --resume--> active
active --stop--> finished
paused --stop/timeout--> finished
```

Runs concurrently with Phase 02's connection state. One diagram can't express
"connection lost mid-workout," the failure most likely to actually happen.

## `WorkoutEngine`

```dart
// myhi_companion_core/lib/workout/workout_engine.dart
enum WorkoutState { idle, active, paused, finished }
enum WorkoutPauseReason { userRequested, machineRequested, connectionLost }

typedef WorkoutSampleRecord = ({
  int elapsedActiveSeconds,
  double? speedKph,
  double? distanceMeters,
  int? calories,
  int? heartRate,
  bool isConnectionGap,
});

class WorkoutEngine {
  final TreadmillService _treadmill;
  WorkoutPauseReason? _pauseReason;
  int _graceGeneration = 0; // same cancel-via-generation pattern as Phase 02's backoff loop
  double? _distanceBaselineMeters;

  WorkoutState _state = WorkoutState.idle;
  WorkoutState get state => _state;

  final _stateController = StreamController<WorkoutState>.broadcast();
  final _sampleController = StreamController<WorkoutSampleRecord>.broadcast();
  Stream<WorkoutState> get stateChanges => _stateController.stream;
  Stream<WorkoutSampleRecord> get samplesRecorded => _sampleController.stream;

  WorkoutEngine(this._treadmill) {
    _treadmill.connectionStateChanges.listen(_onConnectionStateChanged);
    _treadmill.machineEvents.listen(_onMachineEvent);
    _treadmill.samples.listen(_onSample);
  }

  bool tryStart() {
    if (_state != WorkoutState.idle) return false;
    // TODO: capture the counter baseline (see "Counter semantics" below)
    _setState(WorkoutState.active);
    return true;
  }

  bool tryPause(WorkoutPauseReason reason) {
    if (_state != WorkoutState.active) return false;
    _pauseReason = reason;
    _setState(WorkoutState.paused);
    return true;
  }

  bool tryResume() {
    if (_state != WorkoutState.paused) return false;
    _pauseReason = null;
    _setState(WorkoutState.active);
    return true;
  }

  bool tryStop() {
    if (_state != WorkoutState.active && _state != WorkoutState.paused) return false;
    _graceGeneration++; // cancel any running grace timer
    _setState(WorkoutState.finished);
    return true;
  }

  void _setState(WorkoutState next) {
    _state = next;
    _stateController.add(next);
  }

  void _onConnectionStateChanged(ConnectionStateChange change) {
    if (change.state == ConnectionState.disconnected && _state == WorkoutState.active) {
      tryPause(WorkoutPauseReason.connectionLost);
      _runGraceTimer(++_graceGeneration);
    } else if (change.state == ConnectionState.ready && _pauseReason == WorkoutPauseReason.connectionLost) {
      _graceGeneration++; // cancels the running grace timer below
      tryResume();
    }
  }

  Future<void> _runGraceTimer(int myGeneration) async {
    await Future.delayed(const Duration(seconds: 60));
    if (myGeneration != _graceGeneration) return; // reconnect happened first
    tryStop(); // expiry -> finished, saving whatever was recorded
  }

  void _onMachineEvent(MachineEvent event) {
    switch (event.kind) {
      case MachineEventKind.stoppedByUser:
      case MachineEventKind.pausedByUser:
        tryPause(WorkoutPauseReason.machineRequested);
      case MachineEventKind.startedByUser:
        tryResume();
      case MachineEventKind.stoppedBySafetyKey:
        tryStop(); // hard stop — never attempt to restart over BLE
      case MachineEventKind.controlPermissionLost:
        break; // handled in Phase 05
      default:
        break;
    }
  }

  void _onSample(TreadmillSample sample) {
    // TODO (counter semantics, below): compute recorded distance/calories
    // from `sample`, per-session or delta-against-baseline depending on
    // PHASE-00-FINDINGS.md V1. Emit a WorkoutSampleRecord with
    // isConnectionGap: true for exactly the first sample after a
    // connectionLost pause resolves; false otherwise.
  }
}
```

Dart's lack of a direct `CancellationTokenSource` equivalent shows up again
here — the grace timer uses the same generation-counter cancellation
technique Phase 02's `ReconnectionManager` established, applied to "stop
waiting for a reconnect" instead of "stop retrying a connect."

## Counter semantics — read V1 before writing this

| V1 verdict | What the engine does |
|---|---|
| **Per-session** | Record reported values directly |
| **Cumulative** | Every value is a delta against a workout-start baseline; detect a mid-workout reset (value decreases) and re-baseline, staying monotonically increasing |

**Do not guess.** Guessing wrong makes every stored workout wrong, silently,
forever — same rule as the original track. `_onSample`'s TODO stays a TODO
until `PHASE-00-FINDINGS.md` V1 has an answer.

`elapsedActiveSeconds` is **not** the device's own elapsed-time field —
track active seconds directly (increment once per accepted sample while
`active`), since the device's counter may include paused time and this
project's schema excludes it.

## Reference docs

- [`treadmill_service.dart`](../../packages/myhi_companion_core/lib/treadmill/treadmill_service.dart)
- [`../../../phases/phase-01-protocol-decode/README.md`](../../../phases/phase-01-protocol-decode/README.md) — Fitness Machine Status (`0x2ADA`) op codes
- [`../../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md`](../../../phases/phase-00-probe-app/PHASE-00-FINDINGS.md) V1 — **read before writing `_onSample`**

## Tests

- `[Theory]`-style table over every edge in the state diagram, including
  illegal ones: `tryPause()` from `idle` returns `false`, state unchanged.
- Using `FakeTreadmillService`: start, fire a disconnect, assert `paused`;
  reconnect within the window, assert `active` again.
- Same shape with a shortened grace period (constructor parameter, not a
  real 60-second wait in a test) and no reconnect: assert `finished`.
- Gap-marker test: disconnect, reconnect, assert exactly one
  `WorkoutSampleRecord` has `isConnectionGap: true`.
- If V1 is cumulative: a sample sequence with a decreasing raw counter
  partway through — assert the recorded distance stays monotonically
  increasing across it.

## Acceptance

- [ ] No illegal state reachable
- [ ] Connection loss never loses more than the grace window
- [ ] Gaps appear as breaks (recorded via `isConnectionGap`), never interpolated
