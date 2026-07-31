namespace MyHi.Companion.Core.Ftms;

/// <summary>
/// Fitness Machine Control Point (0x2AD9) opcodes this app sends.
/// See 05-FTMS-Protocol.md §7. Only the opcodes this treadmill needs are modelled;
/// resistance/power/heart-rate targets do not apply to a treadmill without those
/// capabilities.
/// </summary>
public enum FtmsOpCode : byte
{
    RequestControl = 0x00,
    Reset = 0x01,
    SetTargetSpeed = 0x02,
    SetTargetInclination = 0x03,
    StartOrResume = 0x07,
    StopOrPause = 0x08,
}

/// <summary>Control point result codes, from the 0x80-prefixed indication. Machine-level; distinct from AppErrorCode.</summary>
public enum FtmsResultCode : byte
{
    Success = 0x01,
    OpCodeNotSupported = 0x02,
    InvalidParameter = 0x03,
    OperationFailed = 0x04,
    ControlNotPermitted = 0x05,
}

/// <summary>
/// Builds the exact bytes to write to 0x2AD9. Deliberately does not clamp or
/// validate range — Phase 00's console is a diagnostic instrument whose job is to
/// discover what the shim accepts, including out-of-range input (Probe Part D5).
/// Range clamping belongs to the real ITreadmillService (Phase 01).
/// </summary>
public static class FtmsCommands
{
    public const byte StopParameter = 0x01;
    public const byte PauseParameter = 0x02;

    public static byte[] RequestControl() => [(byte)FtmsOpCode.RequestControl];

    public static byte[] Reset() => [(byte)FtmsOpCode.Reset];

    /// <summary>
    /// uint16 LE, 0.01 km/h units. Example: 6.5 km/h -> 650 -> 0x028A -> bytes `02 8A 02`.
    /// </summary>
    public static byte[] SetTargetSpeed(double kph)
    {
        var raw = ToUInt16Hundredths(kph, nameof(kph));
        return [(byte)FtmsOpCode.SetTargetSpeed, (byte)(raw & 0xFF), (byte)(raw >> 8)];
    }

    /// <summary>sint16 LE, 0.1 % units. Not applicable to this treadmill (no incline); modelled for completeness.</summary>
    public static byte[] SetTargetInclination(double percent)
    {
        var raw = checked((short)Math.Round(percent * 10, MidpointRounding.AwayFromZero));
        return [(byte)FtmsOpCode.SetTargetInclination, (byte)(raw & 0xFF), (byte)((raw >> 8) & 0xFF)];
    }

    public static byte[] StartOrResume() => [(byte)FtmsOpCode.StartOrResume];

    /// <summary>Stop or Pause requires its parameter byte — `08` alone is malformed.</summary>
    public static byte[] Pause() => [(byte)FtmsOpCode.StopOrPause, PauseParameter];

    public static byte[] Stop() => [(byte)FtmsOpCode.StopOrPause, StopParameter];

    private static ushort ToUInt16Hundredths(double value, string paramName)
    {
        if (double.IsNaN(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");
        }

        var scaled = Math.Round(value * 100, MidpointRounding.AwayFromZero);
        if (scaled > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Value exceeds the uint16 0.01-unit range (max {ushort.MaxValue / 100.0}).");
        }

        return (ushort)scaled;
    }
}

/// <summary>
/// Decoded control point response indication: `80 | requestOpCode | resultCode | params...`.
/// This is the one decoding exception Phase 00 permits — an operator staring at
/// `80 00 01` needs to be told that means "Request Control -> Success".
/// </summary>
public sealed record ControlPointResponse(
    byte ResponseCode,
    byte RequestOpCodeRaw,
    byte ResultCodeRaw,
    IReadOnlyList<byte> Parameters)
{
    public const byte ExpectedResponseCode = 0x80;

    public FtmsOpCode? RequestOpCode =>
        Enum.IsDefined(typeof(FtmsOpCode), RequestOpCodeRaw) ? (FtmsOpCode)RequestOpCodeRaw : null;

    public FtmsResultCode? ResultCode =>
        Enum.IsDefined(typeof(FtmsResultCode), ResultCodeRaw) ? (FtmsResultCode)ResultCodeRaw : null;

    public bool IsSuccess => ResultCode == FtmsResultCode.Success;

    public string DescribeRequestOpCode() => RequestOpCode switch
    {
        FtmsOpCode.RequestControl => "Request Control",
        FtmsOpCode.Reset => "Reset",
        FtmsOpCode.SetTargetSpeed => "Set Target Speed",
        FtmsOpCode.SetTargetInclination => "Set Target Inclination",
        FtmsOpCode.StartOrResume => "Start or Resume",
        FtmsOpCode.StopOrPause => "Stop or Pause",
        _ => $"Unknown (0x{RequestOpCodeRaw:X2})",
    };

    public string DescribeResultCode() => ResultCode switch
    {
        FtmsResultCode.Success => "Success",
        FtmsResultCode.OpCodeNotSupported => "Op Code not supported",
        FtmsResultCode.InvalidParameter => "Invalid Parameter",
        FtmsResultCode.OperationFailed => "Operation Failed",
        FtmsResultCode.ControlNotPermitted => "Control Not Permitted",
        _ => $"Unknown (0x{ResultCodeRaw:X2})",
    };
}

public static class ControlPointResponseParser
{
    /// <summary>
    /// Parses a control point indication. Returns false (never throws) for a
    /// buffer shorter than 3 bytes — a short buffer is a finding to log, not a crash.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out ControlPointResponse? response)
    {
        if (data.Length < 3)
        {
            response = null;
            return false;
        }

        var parameters = data.Length > 3 ? data[3..].ToArray() : [];
        response = new ControlPointResponse(data[0], data[1], data[2], parameters);
        return true;
    }
}
