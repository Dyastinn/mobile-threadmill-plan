// treadmillService.ts
//
// The seam between the BLE/FTMS layer and the rest of the application.
//
// WHY THIS EXISTS
// Phases 03, 04, 06, 07, 09, 10 and 11 are built and tested against this
// interface using FakeTreadmillService, with no treadmill and no Bluetooth
// involved. That removes the hardware from the inner development loop for most
// of the project.
//
// DESIGN NOTES
// - Subscribe functions that return an unsubscribe function, rather than an
//   EventEmitter class or RxJS observables. It's exactly the shape React's
//   useEffect cleanup and useSyncExternalStore expect, so a hook subscribes and
//   cleans up in two lines. RxJS isn't justified for three event streams.
// - Synchronous getters (getConnectionState(), ...) alongside the events, so a
//   consumer that subscribes late can read the current value instead of waiting
//   for the next change.
// - No thread marshalling. All of the app's TypeScript runs on React Native's
//   single JavaScript thread; native BLE events are delivered onto it one at a
//   time. Listeners run synchronously, in subscription order.
// - Every sample field is `number | null`. Field presence is per-packet in FTMS,
//   not per-device, so "supported" and "present in this packet" are different
//   things. null (not undefined) because it maps straight onto a SQLite NULL.
// - Control methods resolve to a ControlResult rather than rejecting. Control
//   point failures are expected operating conditions, not exceptions.
// - Nothing here imports react, react-native or any BLE library. That's what lets
//   it live in src/core/ and be tested in plain Node.

// =====================================================================
// The contract
// =====================================================================

export type Unsubscribe = () => void;

export interface TreadmillService {
  // ---- State (synchronous snapshots) ----

  getConnectionState(): ConnectionState;

  /** Populated once discovery completes. null before then. */
  getCapabilities(): TreadmillCapabilities | null;

  /**
   * Read from characteristic 0x2AD4. null if unavailable.
   * Every speed UI limit must derive from this. Never hardcode a range.
   */
  getSpeedRange(): SpeedRange | null;

  /** True only after Request Control (0x00) succeeded. */
  hasControl(): boolean;

  // ---- Events ----

  onConnectionStateChange(listener: (change: ConnectionStateChange) => void): Unsubscribe;

  /** Decoded 0x2ACD notification. Expect roughly 1 Hz. */
  onSample(listener: (sample: TreadmillSample) => void): Unsubscribe;

  /** Decoded 0x2ADA notification: machine-initiated events. */
  onMachineEvent(listener: (event: MachineEvent) => void): Unsubscribe;

  // ---- Connection ----

  /** Resolves once Ready, rejects on failure. Abort the signal to give up early. */
  connect(deviceId: string, signal?: AbortSignal): Promise<void>;
  disconnect(): Promise<void>;

  // ---- Control ----
  // All of these require getCapabilities()?.hasControlPoint.
  // Implementations MUST serialise control point writes: one outstanding
  // command, wait for the indication (3 s timeout) before sending the next.

  /**
   * Writes Request Control (0x00). Must succeed before any other control
   * command, and must be re-issued after reconnect and after a
   * controlPermissionLost machine event.
   */
  requestControl(): Promise<ControlResult>;

  /**
   * Sets target speed. Implementations clamp to the speed range and round to
   * the device increment (clampSpeed below) before writing. Callers should
   * debounce user input upstream (~300 ms) so rapid taps produce one write of
   * the final target.
   */
  setSpeed(kph: number): Promise<ControlResult>;

  /**
   * Start or Resume (0x07).
   * SAFETY: never call this without a deliberate on-screen user action.
   * Not from a notification action, not from restored state, not from
   * auto-reconnect.
   */
  start(): Promise<ControlResult>;

  /** Stop or Pause with parameter 0x02. */
  pause(): Promise<ControlResult>;

  /**
   * Stop or Pause with parameter 0x01.
   * This is NOT an emergency stop. The physical safety key is the emergency
   * stop; a command over an unreliable radio link is not. Label it "Stop" in
   * the UI and say so.
   */
  stop(): Promise<ControlResult>;
}

// =====================================================================
// Types
// =====================================================================

export type ConnectionState = 'disconnected' | 'connecting' | 'discovering' | 'ready';

export interface ConnectionStateChange {
  readonly state: ConnectionState;
  readonly error?: AppErrorCode;
}

/** One decoded 0x2ACD packet. Every field nullable: presence is per-packet. */
export interface TreadmillSample {
  /** Date.now() when the notification arrived on the phone. */
  readonly receivedAtMs: number;

  readonly speedKph: number | null;
  readonly averageSpeedKph: number | null;

  /** Metres. Decoded from a uint24 field. */
  readonly distanceMeters: number | null;

  readonly calories: number | null;
  readonly caloriesPerHour: number | null;
  readonly heartRate: number | null;
  readonly elapsedSeconds: number | null;
  readonly inclinePercent: number | null;
}

/**
 * Decoded 0x2ADA event: what just happened, not current state.
 * A discriminated union on `kind`: a `switch (event.kind)` narrows the type in
 * each case, so `event.kph` only exists where it's meaningful.
 */
export type MachineEvent =
  | { readonly kind: 'reset' }
  | { readonly kind: 'stoppedByUser' }
  | { readonly kind: 'pausedByUser' }
  | { readonly kind: 'stoppedBySafetyKey' } // treat as a hard stop; never auto-restart
  | { readonly kind: 'startedByUser' }
  | { readonly kind: 'targetSpeedChanged'; readonly kph: number }
  | { readonly kind: 'controlPermissionLost' } // re-request control before re-enabling controls
  | { readonly kind: 'unknown'; readonly opCode: number };

/** Decoded from 0x2ACC. Advisory only on this device: log it, never gate on it. */
export interface TreadmillCapabilities {
  readonly supportsAverageSpeed: boolean;
  readonly supportsTotalDistance: boolean;
  readonly supportsInclination: boolean;
  readonly supportsExpendedEnergy: boolean;
  readonly supportsHeartRate: boolean;
  readonly supportsElapsedTime: boolean;

  /** Target Setting Features bit 0. Advisory: Phase 05 gates on the live handshake. */
  readonly supportsSpeedControl: boolean;

  /** True if 0x2AD9 was discovered at all. */
  readonly hasControlPoint: boolean;

  readonly rawMachineFeatures: number;
  readonly rawTargetFeatures: number;
}

export interface SpeedRange {
  readonly minKph: number;
  readonly maxKph: number;
  readonly incrementKph: number;
}

/**
 * Clamp to the range, then snap to the nearest device increment.
 * The final round to 2 decimal places matters: in floating point,
 * 1.0 + 3 * 0.1 is 1.3000000000000003, which would otherwise be encoded as
 * 130.00000000000003 hundredths of a km/h.
 */
export function clampSpeed(range: SpeedRange, kph: number): number {
  const clamped = Math.min(Math.max(kph, range.minKph), range.maxKph);
  const steps = Math.round((clamped - range.minKph) / range.incrementKph);
  const snapped = Math.round((range.minKph + steps * range.incrementKph) * 100) / 100;
  return Math.min(Math.max(snapped, range.minKph), range.maxKph);
}

export interface ControlResult {
  readonly success: boolean;
  readonly code: FtmsResultCode;
  readonly message?: string;
}

export const controlOk = (): ControlResult => ({ success: true, code: FtmsResultCode.Success });

export const controlFail = (code: FtmsResultCode, message?: string): ControlResult => ({
  success: false,
  code,
  message,
});

/**
 * FTMS control point result codes. Machine-level.
 * Deliberately separate from AppErrorCode: do not merge the two schemes.
 *
 * The const-object-plus-type pattern: `FtmsResultCode.Timeout` is a value you
 * can use at runtime, and `FtmsResultCode` is also the type "one of these
 * numbers". See docs/learning/00a-React-And-TypeScript-Essentials.md.
 */
export const FtmsResultCode = {
  Success: 0x01,
  OpCodeNotSupported: 0x02,
  InvalidParameter: 0x03,
  OperationFailed: 0x04,
  ControlNotPermitted: 0x05,

  // Client-side, not from the device
  Timeout: 0xf0,
  NotConnected: 0xf1,
  NotSupported: 0xf2,
} as const;
export type FtmsResultCode = (typeof FtmsResultCode)[keyof typeof FtmsResultCode];

/**
 * Application-level errors. See phase-02-connection-hardening/README.md's
 * "Application error codes" reference.
 */
export const AppErrorCode = {
  BluetoothDisabled: 1001,
  PermissionDenied: 1002,
  DeviceNotFound: 1003,
  ConnectionTimeout: 1004,
  GattError: 1005,
  ServiceMissing: 1006,
  CharacteristicMissing: 1007,
  NotificationFailed: 1008,
  ControlWriteFailed: 1009,
  ControlNotGranted: 1010,
  ControlResponseTimeout: 1011,
  MalformedPacket: 1012,
} as const;
export type AppErrorCode = (typeof AppErrorCode)[keyof typeof AppErrorCode];

// =====================================================================
// FakeTreadmillService: Phase 01b deliverable
// =====================================================================
//
// Replays a synthetic session so Phases 03, 04, 06, 07 and 09-11 can be built
// and tested with no hardware. It pays for itself in the first afternoon.
//
// It must eventually be able to simulate, on demand:
//   - a normal session: warm-up, steady, cool-down
//   - a mid-session connection drop and recovery (exercises the grace window)
//   - packets with fields absent (exercises null handling)
//   - control commands that fail with ControlNotPermitted
//   - a counter reset mid-session (only relevant if the probe finds cumulative
//     counters; see Phase 00 runbook Part C7)
//
// Swapped in and out in exactly one place: src/features/services.tsx.

export interface TreadmillSimulation {
  begin(scenario: SimulationScenario): void;
  triggerDisconnect(durationMs: number): void;
  triggerCounterReset(): void;
}

export type SimulationScenario =
  | 'normalWalk'
  | 'intervalWalk'
  | 'dropoutMidSession'
  | 'sparseFields'
  | 'controlRejected';
