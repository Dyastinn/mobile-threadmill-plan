using System.Collections.ObjectModel;
using Android.Bluetooth;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace MyHi.Companion.Features.Bluetooth;

/// <summary>Three positions because whether 0x1826 is advertised is itself an open question (ASSUMPTIONS.md A5).</summary>
public enum ScanFilterMode
{
    ServiceUuid,
    NamePrefix,
    Off,
}

/// <summary>
/// Wraps Plugin.BLE's IAdapter for the scan screen (TASKS.md 0.3): 30 s timeout,
/// debounced start, de-duplicated by MAC, three filter positions.
/// </summary>
public sealed class BleScanner : IDisposable
{
    private static readonly TimeSpan MinRestartInterval = TimeSpan.FromSeconds(2);

    private readonly IAdapter _adapter;
    private DateTimeOffset _lastStartUtc = DateTimeOffset.MinValue;

    public BleScanner(IBluetoothLE bluetoothLe)
    {
        _adapter = bluetoothLe.Adapter;
        _adapter.ScanTimeout = 30_000;
        _adapter.DeviceAdvertised += OnDeviceAdvertised;
        _adapter.ScanTimeoutElapsed += (_, _) => ScanTimedOut?.Invoke(this, System.EventArgs.Empty);
    }

    public ObservableCollection<DiscoveredDevice> Devices { get; } = [];

    public ScanFilterMode FilterMode { get; set; } = ScanFilterMode.ServiceUuid;

    public bool IsScanning => _adapter.IsScanning;

    public event EventHandler? ScanTimedOut;

    /// <summary>Debounced: a call within 2 s of the last start, or while already scanning, is a no-op.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsScanning || DateTimeOffset.UtcNow - _lastStartUtc < MinRestartInterval)
        {
            return;
        }

        _lastStartUtc = DateTimeOffset.UtcNow;
        Devices.Clear();

        ScanFilterOptions? options = null;
        Func<IDevice, bool>? deviceFilter = null;

        switch (FilterMode)
        {
            case ScanFilterMode.ServiceUuid:
                options = new ScanFilterOptions { ServiceUuids = [BleUuids.FromShort16(AdvertisementInspector.FitnessMachineServiceUuid)] };
                break;
            case ScanFilterMode.NamePrefix:
                deviceFilter = d => d.Name?.StartsWith("FS-", StringComparison.Ordinal) == true;
                break;
            case ScanFilterMode.Off:
                break;
        }

        await _adapter.StartScanningForDevicesAsync(options, deviceFilter, allowDuplicatesKey: true, cancellationToken: ct);
    }

    public Task StopAsync() => _adapter.StopScanningForDevicesAsync();

    private void OnDeviceAdvertised(object? sender, DeviceEventArgs e)
    {
        var device = e.Device;
        var mac = ReadMacAddress(device);
        var updated = new DiscoveredDevice
        {
            NativeDevice = device,
            Name = string.IsNullOrEmpty(device.Name) ? "(unnamed)" : device.Name,
            MacAddress = mac,
            AddressType = ReadAddressType(device),
            Rssi = device.Rssi,
            AdvertisesFtmsService = AdvertisementInspector.ContainsService16Bit(device.AdvertisementRecords, AdvertisementInspector.FitnessMachineServiceUuid),
            RawAdvertisementHex = AdvertisementInspector.ToRawHex(device.AdvertisementRecords),
        };

        var existingIndex = -1;
        for (var i = 0; i < Devices.Count; i++)
        {
            if (Devices[i].MacAddress == mac)
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            Devices[existingIndex] = updated;
        }
        else
        {
            Devices.Add(updated);
        }
    }

    internal static string ReadMacAddress(IDevice device) =>
        device.NativeDevice is BluetoothDevice native && !string.IsNullOrEmpty(native.Address) ? native.Address : "unknown";

    internal static string ReadAddressType(IDevice device)
    {
        if (device.NativeDevice is not BluetoothDevice native)
        {
            return "unknown";
        }

        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            return "unknown (needs Android 15+)";
        }

        try
        {
            return native.AddressType switch
            {
                Android.Bluetooth.AddressType.Public => "public",
                Android.Bluetooth.AddressType.Random => "random",
                Android.Bluetooth.AddressType.Anonymous => "anonymous",
                _ => "unknown",
            };
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    public void Dispose()
    {
        _adapter.DeviceAdvertised -= OnDeviceAdvertised;
    }
}
