namespace MyHi.Companion.Features.Bluetooth;

/// <summary>Well-known FTMS UUIDs (05-FTMS-Protocol.md §1) plus the 16-bit -> 128-bit expansion Plugin.BLE requires.</summary>
public static class BleUuids
{
    public const string FitnessMachineService = "00001826-0000-1000-8000-00805F9B34FB";
    public const string FitnessMachineFeature = "00002ACC-0000-1000-8000-00805F9B34FB";
    public const string TreadmillData = "00002ACD-0000-1000-8000-00805F9B34FB";
    public const string TrainingStatus = "00002AD3-0000-1000-8000-00805F9B34FB";
    public const string SupportedSpeedRange = "00002AD4-0000-1000-8000-00805F9B34FB";
    public const string FitnessMachineControlPoint = "00002AD9-0000-1000-8000-00805F9B34FB";
    public const string FitnessMachineStatus = "00002ADA-0000-1000-8000-00805F9B34FB";
    public const string HeartRateService = "0000180D-0000-1000-8000-00805F9B34FB";
    public const string HeartRateMeasurement = "00002A37-0000-1000-8000-00805F9B34FB";
    public const string DeviceInformationService = "0000180A-0000-1000-8000-00805F9B34FB";
    public const string ClientCharacteristicConfiguration = "00002902-0000-1000-8000-00805F9B34FB";

    /// <summary>Expands a 16-bit assigned-number UUID to the full 128-bit Bluetooth base UUID form Plugin.BLE requires.</summary>
    public static Guid FromShort16(ushort shortUuid) => new($"0000{shortUuid:X4}-0000-1000-8000-00805F9B34FB");
}
