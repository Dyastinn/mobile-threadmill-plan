using Plugin.BLE.Abstractions.Contracts;

namespace MyHi.Companion.Features.Bluetooth;

/// <summary>
/// The four distinct states TASKS.md 0.2 requires a useful screen for: granted,
/// denied, permanently denied, and adapter off. Scanning must never silently return
/// zero results for one of these reasons without telling the operator which one.
/// </summary>
public enum BleReadinessState
{
    Ready,
    PermissionDenied,
    PermissionPermanentlyDenied,
    AdapterOff,
    AdapterUnavailable,
}

public sealed record BleReadiness(BleReadinessState State, string Message)
{
    public bool IsReady => State == BleReadinessState.Ready;
}

/// <summary>Checks and requests BLUETOOTH_SCAN / BLUETOOTH_CONNECT, and reflects adapter state.</summary>
public sealed class BluetoothReadinessService
{
    private readonly IBluetoothLE _bluetoothLe;

    public BluetoothReadinessService(IBluetoothLE bluetoothLe)
    {
        _bluetoothLe = bluetoothLe;
        _bluetoothLe.StateChanged += (_, args) => AdapterStateChanged?.Invoke(this, args.NewState);
    }

    public BluetoothState AdapterState => _bluetoothLe.State;

    public event EventHandler<BluetoothState>? AdapterStateChanged;

    public async Task<BleReadiness> CheckAsync()
    {
        if (_bluetoothLe.State == BluetoothState.Unavailable)
        {
            return new BleReadiness(BleReadinessState.AdapterUnavailable, "This device has no usable Bluetooth adapter.");
        }

        if (_bluetoothLe.State is BluetoothState.Off or BluetoothState.TurningOff or BluetoothState.TurningOn)
        {
            return new BleReadiness(BleReadinessState.AdapterOff, "Bluetooth is turned off. Enable it to scan for the treadmill.");
        }

        var scanStatus = await Permissions.CheckStatusAsync<BluetoothScanPermission>();
        var connectStatus = await Permissions.CheckStatusAsync<BluetoothConnectPermission>();

        if (scanStatus == PermissionStatus.Granted && connectStatus == PermissionStatus.Granted)
        {
            return new BleReadiness(BleReadinessState.Ready, "Ready.");
        }

        if (scanStatus == PermissionStatus.Disabled || connectStatus == PermissionStatus.Disabled)
        {
            return new BleReadiness(
                BleReadinessState.PermissionPermanentlyDenied,
                "Bluetooth permission was denied permanently. Open app settings to grant it.");
        }

        return new BleReadiness(BleReadinessState.PermissionDenied, "Bluetooth permission is required to scan for the treadmill.");
    }

    /// <summary>Requests both permissions. Call only from a user-visible action (e.g. tapping Scan), never at launch.</summary>
    public async Task<BleReadiness> RequestPermissionsAsync()
    {
        var scanStatus = await Permissions.RequestAsync<BluetoothScanPermission>();
        var connectStatus = await Permissions.RequestAsync<BluetoothConnectPermission>();

        if (scanStatus != PermissionStatus.Granted || connectStatus != PermissionStatus.Granted)
        {
            return await CheckAsync();
        }

        return await CheckAsync();
    }

    public static void OpenAppSettings() => AppInfo.Current.ShowSettingsUI();
}
