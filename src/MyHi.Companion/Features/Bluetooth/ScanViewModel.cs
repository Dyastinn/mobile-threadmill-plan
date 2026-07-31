using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyHi.Companion.Features.Diagnostics;
using MyHi.Companion.Features.Shared;

namespace MyHi.Companion.Features.Bluetooth;

public sealed partial class ScanViewModel : BaseViewModel
{
    private readonly BleScanner _scanner;
    private readonly BluetoothReadinessService _readiness;
    private readonly TreadmillConnection _connection;
    private readonly CaptureSessionManager _captures;
    private readonly ILogger<ScanViewModel> _logger;

    public ScanViewModel(
        BleScanner scanner,
        BluetoothReadinessService readiness,
        TreadmillConnection connection,
        CaptureSessionManager captures,
        ILogger<ScanViewModel> logger)
    {
        _scanner = scanner;
        _readiness = readiness;
        _connection = connection;
        _captures = captures;
        _logger = logger;

        Devices = _scanner.Devices;
        _scanner.ScanTimedOut += OnScanTimedOut;
    }

    public ObservableCollection<DiscoveredDevice> Devices { get; }

    public IReadOnlyList<ScanFilterMode> FilterModes { get; } = [ScanFilterMode.ServiceUuid, ScanFilterMode.NamePrefix, ScanFilterMode.Off];

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private DiscoveredDevice? selectedDevice;

    [ObservableProperty]
    private ScanFilterMode filterMode = ScanFilterMode.ServiceUuid;

    [ObservableProperty]
    private BleReadiness? readiness;

    private void OnScanTimedOut(object? sender, EventArgs e)
    {
        IsScanning = false;
        StatusMessage = "Scan timed out after 30 s.";
    }

    [RelayCommand]
    private async Task ToggleScanAsync()
    {
        if (IsScanning)
        {
            await _scanner.StopAsync();
            IsScanning = false;
            return;
        }

        var check = await _readiness.CheckAsync();
        if (check.State == BleReadinessState.PermissionDenied)
        {
            check = await _readiness.RequestPermissionsAsync();
        }

        Readiness = check;
        if (!check.IsReady)
        {
            StatusMessage = check.Message;
            return;
        }

        _scanner.FilterMode = FilterMode;
        StatusMessage = null;
        await _scanner.StartAsync();
        IsScanning = true;
    }

    [RelayCommand]
    private static void OpenSettings() => BluetoothReadinessService.OpenAppSettings();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ConnectAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await _scanner.StopAsync();
        IsScanning = false;
        IsBusy = true;
        try
        {
            await _connection.ConnectAsync(SelectedDevice.NativeDevice);
            await Shell.Current.GoToAsync("gatttree");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connect failed for {Mac}", SelectedDevice.MacAddress);
            StatusMessage = $"Connect failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RecordSelected()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var recorder = _captures.EnsureSession();
        recorder.WriteAdv(SelectedDevice.Name, SelectedDevice.MacAddress, SelectedDevice.AddressType, SelectedDevice.RawAdvertisementHex);
        StatusMessage = $"Recorded advertisement for {SelectedDevice.Name} to the capture session.";
    }

    private bool HasSelection() => SelectedDevice is not null;

    partial void OnSelectedDeviceChanged(DiscoveredDevice? value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        RecordSelectedCommand.NotifyCanExecuteChanged();
    }
}
