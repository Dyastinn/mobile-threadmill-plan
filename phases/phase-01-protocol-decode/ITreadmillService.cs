// ITreadmillService.cs
//
// The seam between the BLE/FTMS layer and the rest of the application.
//
// WHY THIS EXISTS
// Phases 4, 5, 7, 9, 10, 11 and 12 are built and tested against this interface
// using FakeTreadmillService, with no treadmill and no Bluetooth involved. That
// removes the hardware from the inner development loop for most of the project.
//
// DESIGN NOTES
// - Plain events rather than IObservable<T>. IObservable without System.Reactive
//   operators is painful, and taking the dependency isn't justified for three
//   event streams. If composition needs get real later, adapt at the edge.
// - Events are raised on the UI thread. Marshalling happens once, here at the
//   service boundary, so no consumer has to think about it.
// - Every sample field is nullable. Field presence is per-packet in FTMS, not
//   per-device, so "supported" and "present in this packet" are different things.
// - Control methods return a result rather than throwing. Control point failures
//   are expected operating conditions, not exceptions.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyHi.Companion.Treadmill;

public interface ITreadmillService
{
    // ---- State ----

    ConnectionState State { get; }

    /// <summary>Populated once discovery completes. Null before then.</summary>
    TreadmillCapabilities? Capabilities { get; }

    /// <summary>
    /// Read from characteristic 0x2AD4. Null if unavailable.
    /// Every speed UI limit must derive from this. Never hardcode a range.
    /// </summary>
    SpeedRange? SpeedRange { get; }

    /// <summary>True only after Request Control (0x00) succeeded.</summary>
    bool HasControl { get; }

    // ---- Events (raised on the UI thread) ----

    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>Decoded 0x2ACD notification. Expect roughly 1 Hz.</summary>
    event EventHandler<TreadmillSample>? SampleReceived;

    /// <summary>Decoded 0x2ADA notification: machine-initiated events.</summary>
    event EventHandler<MachineEvent>? MachineEventReceived;

    // ---- Connection ----

    Task ConnectAsync(string deviceId, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    // ---- Control ----
    // All of these require Capabilities.SupportsSpeedControl.
    // Implementations MUST serialise control point writes: one outstanding
    // command, wait for the indication (3s timeout) before sending the next.

    /// <summary>
    /// Writes Request Control (0x00). Must succeed before any other control
    /// command, and must be re-issued after reconnect and after a
    /// ControlPermissionLost machine event.
    /// </summary>
    Task<ControlResult> RequestControlAsync(CancellationToken ct = default);

    /// <summary>
    /// Sets target speed. Implementations clamp to SpeedRange and round to the
    /// device increment before writing. Callers should debounce user input
    /// upstream (~300 ms) so rapid taps produce one write of the final target.
    /// </summary>
    Task<ControlResult> SetSpeedAsync(double kph, CancellationToken ct = default);

    /// <summary>
    /// Start or Resume (0x07).
    /// SAFETY: never call this without a deliberate on-screen user action.
    /// Not from a notification action, not from restored state, not from
    /// auto-reconnect.
    /// </summary>
    Task<ControlResult> StartAsync(CancellationToken ct = default);

    /// <summary>Stop or Pause with parameter 0x02.</summary>
    Task<ControlResult> PauseAsync(CancellationToken ct = default);

    /// <summary>
    /// Stop or Pause with parameter 0x01.
    /// This is NOT an emergency stop. The physical safety key is the emergency
    /// stop; a command over an unreliable radio link is not. Label it "Stop" in
    /// the UI and say so.
    /// </summary>
    Task<ControlResult> StopAsync(CancellationToken ct = default);
}

// =====================================================================
// Types
// =====================================================================

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Discovering,
    Ready
}

public sealed record ConnectionStateChangedEventArgs(
    ConnectionState State,
    AppErrorCode? Error = null);

/// <summary>
/// One decoded 0x2ACD packet. All fields nullable: presence is per-packet.
/// Struct to avoid allocation on the notification hot path.
/// </summary>
public readonly record struct TreadmillSample
{
    public DateTimeOffset TimestampUtc { get; init; }

    public double? SpeedKph { get; init; }
    public double? AverageSpeedKph { get; init; }

    /// <summary>Metres. Decoded from a uint24 field.</summary>
    public double? DistanceMeters { get; init; }

    public int? Calories { get; init; }
    public int? CaloriesPerHour { get; init; }
    public int? HeartRate { get; init; }
    public int? ElapsedSeconds { get; init; }
    public double? InclinePercent { get; init; }
}

/// <summary>Decoded 0x2ADA event. What just happened, not current state.</summary>
public sealed record MachineEvent(MachineEventKind Kind, double? Value = null);

public enum MachineEventKind
{
    Unknown,
    Reset,
    StoppedByUser,
    PausedByUser,
    StoppedBySafetyKey,   // treat as a hard stop; never auto-restart
    StartedByUser,
    TargetSpeedChanged,
    ControlPermissionLost // re-request control before re-enabling controls
}

/// <summary>Decoded from 0x2ACC. Drives what the UI shows at all.</summary>
public sealed record TreadmillCapabilities
{
    public bool SupportsAverageSpeed { get; init; }
    public bool SupportsTotalDistance { get; init; }
    public bool SupportsInclination { get; init; }
    public bool SupportsExpendedEnergy { get; init; }
    public bool SupportsHeartRate { get; init; }
    public bool SupportsElapsedTime { get; init; }

    /// <summary>Target Setting Features bit 0. If false, hide all speed controls.</summary>
    public bool SupportsSpeedControl { get; init; }

    /// <summary>True if 0x2AD9 was discovered at all.</summary>
    public bool HasControlPoint { get; init; }

    public uint RawMachineFeatures { get; init; }
    public uint RawTargetFeatures { get; init; }
}

public sealed record SpeedRange(double MinKph, double MaxKph, double IncrementKph)
{
    public double Clamp(double kph)
    {
        var clamped = Math.Clamp(kph, MinKph, MaxKph);
        var steps = Math.Round((clamped - MinKph) / IncrementKph);
        return Math.Clamp(MinKph + steps * IncrementKph, MinKph, MaxKph);
    }
}

public sealed record ControlResult(bool Success, FtmsResultCode Code, string? Message = null)
{
    public static ControlResult Ok() => new(true, FtmsResultCode.Success);
    public static ControlResult Fail(FtmsResultCode c, string? m = null) => new(false, c, m);
}

/// <summary>
/// FTMS control point result codes. Machine-level.
/// Deliberately separate from AppErrorCode — do not merge the two schemes.
/// </summary>
public enum FtmsResultCode : byte
{
    Success            = 0x01,
    OpCodeNotSupported = 0x02,
    InvalidParameter   = 0x03,
    OperationFailed    = 0x04,
    ControlNotPermitted = 0x05,

    // Client-side, not from the device
    Timeout            = 0xF0,
    NotConnected       = 0xF1,
    NotSupported       = 0xF2
}

/// <summary>Application-level errors. See phase-02-connection-hardening/README.md's
/// "Application error codes" reference.</summary>
public enum AppErrorCode
{
    BluetoothDisabled       = 1001,
    PermissionDenied        = 1002,
    DeviceNotFound          = 1003,
    ConnectionTimeout       = 1004,
    GattError               = 1005,
    ServiceMissing          = 1006,
    CharacteristicMissing   = 1007,
    NotificationFailed      = 1008,
    ControlWriteFailed      = 1009,
    ControlNotGranted       = 1010,
    ControlResponseTimeout  = 1011,
    MalformedPacket         = 1012
}

// =====================================================================
// FakeTreadmillService — Phase 3 deliverable
// =====================================================================
//
// Replays a synthetic session so Phases 4, 5, 7, 9-12 can be built and tested
// with no hardware. Roughly 40 lines of real logic; it pays for itself in the
// first afternoon.
//
// It must be able to simulate, on demand:
//   - a normal session: warm-up, steady, cool-down
//   - a mid-session connection drop and recovery (exercises the grace window)
//   - packets with fields absent (exercises nullable handling)
//   - control commands that fail with ControlNotPermitted
//   - a counter reset mid-session (only relevant if the probe finds cumulative
//     counters — see 05a Part C7)
//
// Register it behind a debug flag or a build configuration so it can be swapped
// in without editing consumer code.

public interface ITreadmillSimulation
{
    void Begin(SimulationScenario scenario);
    void TriggerDisconnect(TimeSpan duration);
    void TriggerCounterReset();
}

public enum SimulationScenario
{
    NormalWalk,
    IntervalWalk,
    DropoutMidSession,
    SparseFields,
    ControlRejected
}
