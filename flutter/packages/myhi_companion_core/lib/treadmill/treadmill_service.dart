// treadmill_service.dart
//
// The seam between the BLE/FTMS layer and the rest of the application.
// Dart/Flutter-track equivalent of the original plan's
// `phases/phase-01-protocol-decode/ITreadmillService.cs`. Same role, same
// consumers (Phases 03, 04, 06, 07, 09, 10, 11, 12 build against this, not
// against BLE), adapted to idiomatic Dart rather than translated line by
// line.
//
// WHY THIS EXISTS
// Everything above this interface can be built and tested against
// FakeTreadmillService, with no treadmill and no Bluetooth involved. That
// removes the hardware from the inner development loop for most of the
// project.
//
// DESIGN NOTES (where this differs from the C# version, and why)
// - Streams instead of C# events. Dart doesn't have a separate event
//   language feature; a broadcast Stream is the idiomatic equivalent, and
//   composes naturally with everything else in Flutter (StreamBuilder,
//   riverpod's StreamProvider) the way IObservable would have in C#, without
//   needing to justify an extra dependency the way the original decision
//   record had to for System.Reactive.
// - No explicit UI-thread marshalling. Dart plugins deliver stream events on
//   the same isolate that runs the widget tree by default, so there's no
//   equivalent of MainThread.BeginInvokeOnMainThread to document here. If a
//   real implementation ever does its own isolate work, marshalling back
//   becomes that implementation's job, not a contract this interface has to
//   state.
// - No "I" prefix on the type name. Dart doesn't use Hungarian notation for
//   interfaces; `interface class` (or, here, `abstract interface class`,
//   since this type is never instantiated directly) is how Dart 3 expresses
//   "this is a contract, not a base class to extend."
// - TreadmillSample is a Dart 3 record type, not a class. Records are
//   structural, immutable, and compared by value out of the box — the same
//   properties that motivated `readonly record struct` in the original,
//   without needing an explicit type declaration for something this shaped.
// - Control methods return a result rather than throwing, same as the
//   original: control point failures are expected operating conditions, not
//   exceptions.

/// The seam. Implementations: `FakeTreadmillService` (Phase 01b, in this
/// same package) and a real BLE-backed implementation living in the
/// `myhi_companion` app (Phase 01a), since only the app can depend on
/// `flutter_blue_plus`.
abstract interface class TreadmillService {
  // ---- State ----

  ConnectionState get state;

  /// Populated once discovery completes. Null before then.
  TreadmillCapabilities? get capabilities;

  /// Read from characteristic 0x2AD4. Null if unavailable.
  /// Every speed UI limit must derive from this. Never hardcode a range.
  SpeedRange? get speedRange;

  /// True only after Request Control (0x00) succeeded.
  bool get hasControl;

  // ---- Streams ----

  Stream<ConnectionStateChange> get connectionStateChanges;

  /// Decoded 0x2ACD notification. Expect roughly 1 Hz.
  Stream<TreadmillSample> get samples;

  /// Decoded 0x2ADA notification: machine-initiated events.
  Stream<MachineEvent> get machineEvents;

  // ---- Connection ----

  Future<void> connect(String deviceId);
  Future<void> disconnect();

  // ---- Control ----
  // All of these require capabilities.supportsSpeedControl.
  // Implementations MUST serialise control point writes: one outstanding
  // command, wait for the indication (3s timeout) before sending the next.

  /// Writes Request Control (0x00). Must succeed before any other control
  /// command, and must be re-issued after reconnect and after a
  /// controlPermissionLost machine event.
  Future<ControlResult> requestControl();

  /// Sets target speed. Implementations clamp to speedRange and round to the
  /// device increment before writing. Callers should debounce user input
  /// upstream (~300 ms) so rapid taps produce one write of the final target.
  Future<ControlResult> setSpeed(double kph);

  /// Start or Resume (0x07).
  /// SAFETY: never call this without a deliberate on-screen user action.
  /// Not from a notification handler, not from restored state, not from
  /// auto-reconnect.
  Future<ControlResult> start();

  /// Stop or Pause with parameter 0x02.
  Future<ControlResult> pause();

  /// Stop or Pause with parameter 0x01.
  /// This is NOT an emergency stop. The physical safety key is the
  /// emergency stop; a command over an unreliable radio link is not. Label
  /// it "Stop" in the UI and say so.
  Future<ControlResult> stop();
}

// =====================================================================
// Types
// =====================================================================

enum ConnectionState { disconnected, connecting, discovering, ready }

final class ConnectionStateChange {
  final ConnectionState state;
  final AppErrorCode? error;

  const ConnectionStateChange(this.state, {this.error});
}

/// One decoded 0x2ACD packet. All fields nullable: presence is per-packet,
/// not per-device. A record, not a class — value-typed, immutable, and
/// structurally comparable for free, the same reasons the original used
/// `readonly record struct`.
typedef TreadmillSample = ({
  DateTime timestampUtc,
  double? speedKph,
  double? averageSpeedKph,
  double? distanceMeters, // metres, decoded from a uint24 field
  int? calories,
  int? caloriesPerHour,
  int? heartRate,
  int? elapsedSeconds,
  double? inclinePercent,
});

/// Decoded 0x2ADA event. What just happened, not current state.
final class MachineEvent {
  final MachineEventKind kind;
  final double? value;

  const MachineEvent(this.kind, {this.value});
}

enum MachineEventKind {
  unknown,
  reset,
  stoppedByUser,
  pausedByUser,
  stoppedBySafetyKey, // treat as a hard stop; never auto-restart
  startedByUser,
  targetSpeedChanged,
  controlPermissionLost, // re-request control before re-enabling controls
}

/// Decoded from 0x2ACC. Drives what the UI shows at all.
final class TreadmillCapabilities {
  final bool supportsAverageSpeed;
  final bool supportsTotalDistance;
  final bool supportsInclination;
  final bool supportsExpendedEnergy;
  final bool supportsHeartRate;
  final bool supportsElapsedTime;

  /// Target Setting Features bit 0. If false, hide all speed controls.
  final bool supportsSpeedControl;

  /// True if 0x2AD9 was discovered at all.
  final bool hasControlPoint;

  final int rawMachineFeatures;
  final int rawTargetFeatures;

  const TreadmillCapabilities({
    this.supportsAverageSpeed = false,
    this.supportsTotalDistance = false,
    this.supportsInclination = false,
    this.supportsExpendedEnergy = false,
    this.supportsHeartRate = false,
    this.supportsElapsedTime = false,
    this.supportsSpeedControl = false,
    this.hasControlPoint = false,
    this.rawMachineFeatures = 0,
    this.rawTargetFeatures = 0,
  });
}

final class SpeedRange {
  final double minKph;
  final double maxKph;
  final double incrementKph;

  const SpeedRange({
    required this.minKph,
    required this.maxKph,
    required this.incrementKph,
  });

  double clamp(double kph) {
    final clamped = kph.clamp(minKph, maxKph);
    final steps = ((clamped - minKph) / incrementKph).roundToDouble();
    return (minKph + steps * incrementKph).clamp(minKph, maxKph);
  }
}

final class ControlResult {
  final bool success;
  final FtmsResultCode code;
  final String? message;

  const ControlResult(this.success, this.code, {this.message});

  factory ControlResult.ok() => const ControlResult(true, FtmsResultCode.success);

  factory ControlResult.fail(FtmsResultCode code, [String? message]) =>
      ControlResult(false, code, message: message);
}

/// FTMS control point result codes. Machine-level.
/// Deliberately separate from AppErrorCode — do not merge the two schemes.
enum FtmsResultCode {
  success(0x01),
  opCodeNotSupported(0x02),
  invalidParameter(0x03),
  operationFailed(0x04),
  controlNotPermitted(0x05),

  // Client-side, not from the device
  timeout(0xF0),
  notConnected(0xF1),
  notSupported(0xF2);

  final int byteValue;
  const FtmsResultCode(this.byteValue);
}

/// Application-level errors. See the connection-hardening phase's
/// "Application error codes" reference once it's written for this track —
/// the codes themselves are unchanged from the original plan.
enum AppErrorCode {
  bluetoothDisabled(1001),
  permissionDenied(1002),
  deviceNotFound(1003),
  connectionTimeout(1004),
  gattError(1005),
  serviceMissing(1006),
  characteristicMissing(1007),
  notificationFailed(1008),
  controlWriteFailed(1009),
  controlNotGranted(1010),
  controlResponseTimeout(1011),
  malformedPacket(1012);

  final int code;
  const AppErrorCode(this.code);
}

// =====================================================================
// FakeTreadmillService — Phase 01b deliverable
// =====================================================================
//
// Replays a synthetic session so Phases 03, 04, 06, 07, 09-12 can be built
// and tested with no hardware, `dart test`, no emulator. Roughly the same
// scope as the original's fake: ~40 lines of real logic, pays for itself in
// the first afternoon. Implemented in this package as
// `treadmill_service_fake.dart` (Phase 01b, not this file) once Phase 01b
// starts — this file only declares the seam and the simulation control
// surface it exposes.
//
// It must be able to simulate, on demand:
//   - a normal session: warm-up, steady, cool-down
//   - a mid-session connection drop and recovery (exercises the grace window)
//   - packets with fields absent (exercises nullable handling)
//   - control commands that fail with controlNotPermitted
//   - a counter reset mid-session (only relevant if the probe finds
//     cumulative counters — see the probe checklist's Part C7)

abstract interface class TreadmillSimulation {
  void begin(SimulationScenario scenario);
  void triggerDisconnect(Duration duration);
  void triggerCounterReset();
}

enum SimulationScenario {
  normalWalk,
  intervalWalk,
  dropoutMidSession,
  sparseFields,
  controlRejected,
}
