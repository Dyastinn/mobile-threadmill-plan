using Plugin.BLE.Abstractions.Contracts;

namespace MyHi.Companion.Features.Bluetooth;

/// <summary>
/// One scanned device, de-duplicated by MAC. Carries the raw advertisement so the
/// scan screen can answer "is 0x1826 advertised" and "what address type" without a
/// second tool (ASSUMPTIONS.md A5, A6).
/// </summary>
public sealed class DiscoveredDevice
{
    public required IDevice NativeDevice { get; init; }
    public required string Name { get; init; }
    public required string MacAddress { get; init; }
    public required string AddressType { get; init; }
    public required int Rssi { get; init; }
    public required bool AdvertisesFtmsService { get; init; }
    public required string RawAdvertisementHex { get; init; }

    /// <summary>FS- prefix identifies a FitShow module (DEVICE.md). Fallback filter if 0x1826 isn't advertised.</summary>
    public bool HasFitShowNamePrefix => Name.StartsWith("FS-", StringComparison.Ordinal);
}
