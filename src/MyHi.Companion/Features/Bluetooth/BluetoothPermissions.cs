namespace MyHi.Companion.Features.Bluetooth;

/// <summary>
/// Modern Bluetooth permission path only (minSdk 31) — see 00-Project-Plan.md and
/// TASKS.md 0.2. <c>neverForLocation</c> is declared in AndroidManifest.xml; there is
/// no legacy BLUETOOTH/BLUETOOTH_ADMIN/ACCESS_FINE_LOCATION path to request here.
/// </summary>
public sealed class BluetoothScanPermission : Permissions.BasePlatformPermission
{
#if ANDROID
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        ("android.permission.BLUETOOTH_SCAN", true),
    ];
#endif
}

public sealed class BluetoothConnectPermission : Permissions.BasePlatformPermission
{
#if ANDROID
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        ("android.permission.BLUETOOTH_CONNECT", true),
    ];
#endif
}
