using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyHi.Companion.Features.Bluetooth;
using MyHi.Companion.Features.Shared;

namespace MyHi.Companion.Features.Diagnostics;

public sealed partial class GattTreeViewModel : BaseViewModel
{
    private readonly TreadmillConnection _connection;

    public GattTreeViewModel(TreadmillConnection connection)
    {
        _connection = connection;
        _connection.StateChanged += OnConnectionStateChanged;
        ConnectionState = _connection.State;
        Services = new ObservableCollection<GattServiceInfo>(_connection.Services);
        NegotiatedMtu = _connection.NegotiatedMtu;
    }

    public ObservableCollection<GattServiceInfo> Services { get; }

    [ObservableProperty]
    private ConnectionState connectionState;

    [ObservableProperty]
    private int negotiatedMtu;

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        ConnectionState = state;
    }

    [RelayCommand]
    private void Refresh()
    {
        Services.Clear();
        foreach (var service in _connection.Services)
        {
            Services.Add(service);
        }

        NegotiatedMtu = _connection.NegotiatedMtu;
        ConnectionState = _connection.State;
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _connection.DisconnectAsync();
        Services.Clear();
    }
}
