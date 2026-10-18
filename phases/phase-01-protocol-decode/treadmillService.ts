// treadmillService.ts
//
// The interface between the Bluetooth code and the rest of the app.
// Most phases build against this, using a fake treadmill, so they don't need
// the real one. Nothing here imports React, React Native or a Bluetooth library.

export type Unsubscribe = () => void;

export interface TreadmillService {
  // ---- Current values ----

  getConnectionState(): ConnectionState;

  /** null until connected. */
  getCapabilities(): TreadmillCapabilities | null;

  /** From 0x2AD4. null if unavailable. All speed limits in the UI come from this. */
  getSpeedRange(): SpeedRange | null;

  /** true only after Request Control (0x00) succeeded. */
  hasControl(): boolean;

  // ---- Events ----
  // Each one returns a function that stops listening. Call it when you're done.

  onConnectionStateChange(listener: (change: ConnectionStateChange) => void): Unsubscribe;

  /** One decoded 0x2ACD packet, about once a second. */
  onSample(listener: (sample: TreadmillSample) => void): Unsubscribe;

  /** One decoded 0x2ADA event: something the treadmill just did. */
  onMachineEvent(listener: (event: MachineEvent) => void): Unsubscribe;

  // ---- Connection ----

  /** Resolves when ready. Abort the signal to give up. */
  connect(deviceId: string, signal?: AbortSignal): Promise<void>;
  disconnect(): Promise<void>;

  // ---- Control ----
  // These never throw. A failure comes back as a ControlResult.
  // Only one command may be in flight at a time; the real version queues them.

  /** Must succeed before any other command, and again after every reconnect. */
  requestControl(): Promise<ControlResult>;

  /** Clamps to the speed range first. Debounce taps before calling (~300 ms). */
  setSpeed(kph: number): Promise<ControlResult>;

  /** SAFETY: only from a deliberate tap. Never automatically. */
  start(): Promise<ControlResult>;

  pause(): Promise<ControlResult>;

  /** Not an emergency stop. The safety key is the emergency stop. */
  stop(): Promise<ControlResult>;
}

// ---- Types ----

export type ConnectionState = 'disconnected' | 'connecting' | 'discovering' | 'ready';

export interface ConnectionStateChange {
  readonly state: ConnectionState;
  readonly error?: AppErrorCode;
}

/** One 0x2ACD packet. A field is null when this packet didn't include it. */
export interface TreadmillSample {
  /** Date.now() when the packet arrived. */
  readonly receivedAtMs: number;
  readonly speedKph: number | null;
  readonly averageSpeedKph: number | null;
  readonly distanceMeters: number | null;
  readonly calories: number | null;
  readonly caloriesPerHour: number | null;
  readonly heartRate: number | null;
  readonly elapsedSeconds: number | null;
  readonly inclinePercent: number | null;
}

/** Something the treadmill just did. `kind` says which. */
export type MachineEvent =
  | { readonly kind: 'reset' }
  | { readonly kind: 'stoppedByUser' }
  | { readonly kind: 'pausedByUser' }
  | { readonly kind: 'stoppedBySafetyKey' } // hard stop: never restart automatically
  | { readonly kind: 'startedByUser' }
  | { readonly kind: 'targetSpeedChanged'; readonly kph: number }
  | { readonly kind: 'controlPermissionLost' } // request control again before allowing commands
  | { readonly kind: 'unknown'; readonly opCode: number };

/** From 0x2ACC. This treadmill's list is wrong, so only log it. Never decide from it. */
export interface TreadmillCapabilities {
  readonly supportsAverageSpeed: boolean;
  readonly supportsTotalDistance: boolean;
  readonly supportsInclination: boolean;
  readonly supportsExpendedEnergy: boolean;
  readonly supportsHeartRate: boolean;
  readonly supportsElapsedTime: boolean;
  readonly supportsSpeedControl: boolean;
  /** true if 0x2AD9 exists. */
  readonly hasControlPoint: boolean;
  readonly rawMachineFeatures: number;
  readonly rawTargetFeatures: number;
}

export interface SpeedRange {
  readonly minKph: number;
  readonly maxKph: number;
  readonly incrementKph: number;
}

/** Keeps a speed inside the range and on a real step (e.g. 0.1 km/h). */
export function clampSpeed(range: SpeedRange, kph: number): number {
  const clamped = Math.min(Math.max(kph, range.minKph), range.maxKph);
  const steps = Math.round((clamped - range.minKph) / range.incrementKph);
  // Round to 2 decimals: in JavaScript, 1.0 + 3 * 0.1 is 1.3000000000000003.
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

/** Result codes from the treadmill's control point. Separate from AppErrorCode. */
export const FtmsResultCode = {
  Success: 0x01,
  OpCodeNotSupported: 0x02,
  InvalidParameter: 0x03,
  OperationFailed: 0x04,
  ControlNotPermitted: 0x05,
  // Ours, not the treadmill's:
  Timeout: 0xf0,
  NotConnected: 0xf1,
  NotSupported: 0xf2,
} as const;
export type FtmsResultCode = (typeof FtmsResultCode)[keyof typeof FtmsResultCode];

/** App errors. Listed in phase-02-connection-hardening/README.md. */
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

// ---- For the fake treadmill (Phase 01b) ----
// Ways to make the fake misbehave in tests. Add each one only when a test needs it.

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
