using System.Threading;
using Microsoft.Extensions.Logging;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;

namespace MyHi.Companion.Features.Bluetooth;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Discovering,
    Ready,
}

public sealed record GattCharacteristicInfo(
    Guid Id,
    string ShortUuid,
    string? Name,
    CharacteristicPropertyType Properties,
    ICharacteristic Native);

public sealed record GattServiceInfo(
    Guid Id,
    string ShortUuid,
    string? Name,
    bool IsPrimary,
    IReadOnlyList<GattCharacteristicInfo> Characteristics);

/// <summary>
/// Connect + discovery for the GATT tree screen (TASKS.md 0.4). No auto-reconnect —
/// that is Phase 02. Every other diagnostic screen reads <see cref="Services"/> and
/// issues GATT operations through <see cref="RunExclusiveAsync{T}"/> so reads,
/// writes, and subscriptions from different screens never race on the same link.
/// </summary>
public sealed class TreadmillConnection : IDisposable
{
    private readonly IAdapter _adapter;
    private readonly ILogger<TreadmillConnection> _logger;
    private readonly SemaphoreSlim _gattGate = new(1, 1);

    public TreadmillConnection(IBluetoothLE bluetoothLe, ILogger<TreadmillConnection> logger)
    {
        _adapter = bluetoothLe.Adapter;
        _logger = logger;
        _adapter.DeviceConnectionLost += OnConnectionLost;
        _adapter.DeviceDisconnected += OnDeviceDisconnected;
    }

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public IDevice? Device { get; private set; }

    public IReadOnlyList<GattServiceInfo> Services { get; private set; } = [];

    public int NegotiatedMtu { get; private set; }

    public event EventHandler<ConnectionState>? StateChanged;

    public event EventHandler<int>? GattEvent;

    public async Task ConnectAsync(IDevice device, CancellationToken ct = default)
    {
        SetState(ConnectionState.Connecting);

        try
        {
            // autoConnect=false for the first attempt, per 05-FTMS-Protocol.md §8 and the GATT 133 traps in TASKS.md 0.4.
            await _adapter.ConnectToDeviceAsync(device, new ConnectParameters(autoConnect: false), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GATT connect failed for {Mac}", BleScanner.ReadMacAddress(device));
            SetState(ConnectionState.Disconnected);
            throw;
        }

        Device = device;
        SetState(ConnectionState.Discovering);

        await Task.Delay(200, ct);

        try
        {
            NegotiatedMtu = await device.RequestMtuAsync(517);
            _logger.LogInformation("Negotiated MTU: {Mtu}", NegotiatedMtu);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MTU request failed; assuming default 23");
            NegotiatedMtu = 23;
        }

        var services = await device.GetServicesAsync(ct);
        var infos = new List<GattServiceInfo>();
        foreach (var service in services)
        {
            IReadOnlyList<ICharacteristic> characteristics;
            try
            {
                characteristics = await service.GetCharacteristicsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate characteristics for service {Service}", BleUuidHelpers.ToShortForm(service.Id));
                characteristics = [];
            }

            var charInfos = characteristics
                .Select(c => new GattCharacteristicInfo(c.Id, BleUuidHelpers.ToShortForm(c.Id), KnownBleNames.Lookup(c.Id), c.Properties, c))
                .ToList();

            infos.Add(new GattServiceInfo(service.Id, BleUuidHelpers.ToShortForm(service.Id), KnownBleNames.Lookup(service.Id), service.IsPrimary, charInfos));
        }

        Services = infos;

        if (Services.All(s => s.ShortUuid != "1826"))
        {
            _logger.LogWarning("0x1826 Fitness Machine service was not found on this device");
        }

        SetState(ConnectionState.Ready);
    }

    public async Task DisconnectAsync()
    {
        if (Device is { } device)
        {
            try
            {
                // close() before reconnecting, not just disconnect() — GATT 133 mitigation.
                await _adapter.DisconnectDeviceAsync(device);
            }
            finally
            {
                device.Dispose();
            }
        }

        Device = null;
        Services = [];
        SetState(ConnectionState.Disconnected);
    }

    /// <summary>Serialises GATT operations from every screen through one queue.</summary>
    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await _gattGate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            _gattGate.Release();
        }
    }

    public Task RunExclusiveAsync(Func<Task> action, CancellationToken ct = default) =>
        RunExclusiveAsync(async () =>
        {
            await action();
            return true;
        }, ct);

    private void OnConnectionLost(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceErrorEventArgs e)
    {
        _logger.LogWarning("GATT connection lost: {Message}", e.ErrorMessage);
        GattEvent?.Invoke(this, -1);
        Device = null;
        Services = [];
        SetState(ConnectionState.Disconnected);
    }

    private void OnDeviceDisconnected(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs e)
    {
        if (State != ConnectionState.Disconnected)
        {
            Device = null;
            Services = [];
            SetState(ConnectionState.Disconnected);
        }
    }

    private void SetState(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        _adapter.DeviceConnectionLost -= OnConnectionLost;
        _adapter.DeviceDisconnected -= OnDeviceDisconnected;
        _gattGate.Dispose();
    }
}
