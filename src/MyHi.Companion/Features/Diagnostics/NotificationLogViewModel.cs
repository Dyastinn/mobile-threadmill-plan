using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyHi.Companion.Core.Formatting;
using MyHi.Companion.Features.Bluetooth;
using MyHi.Companion.Features.Shared;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace MyHi.Companion.Features.Diagnostics;

public sealed record NotificationLogRow(long CaptureId, DateTimeOffset TimestampUtc, string Uuid, int Length, string Hex);

public sealed record FlagsSeen(string FlagsHex, int Count);

/// <summary>Per-characteristic subscribe toggle with a rolling 10 s rate counter (TASKS.md 0.6).</summary>
public sealed partial class NotificationChannel : ObservableObject
{
    private readonly Queue<DateTimeOffset> _recent = new();

    public required string ShortUuid { get; init; }

    public required string DisplayName { get; init; }

    public ICharacteristic? Native { get; set; }

    [ObservableProperty]
    private bool isSubscribed;

    [ObservableProperty]
    private double rateHz;

    public void RecordArrival(DateTimeOffset atUtc)
    {
        _recent.Enqueue(atUtc);
        var cutoff = atUtc - TimeSpan.FromSeconds(10);
        while (_recent.Count > 0 && _recent.Peek() < cutoff)
        {
            _recent.Dequeue();
        }

        RateHz = Math.Round(_recent.Count / 10.0, 2);
    }
}

public sealed partial class NotificationLogViewModel : BaseViewModel, IDisposable
{
    private const int MaxUiRows = 500;
    private readonly TreadmillConnection _connection;
    private readonly CaptureSessionManager _captures;
    private readonly ILogger<NotificationLogViewModel> _logger;
    private readonly Dictionary<string, int> _flagsSeen = new();

    public NotificationLogViewModel(TreadmillConnection connection, CaptureSessionManager captures, ILogger<NotificationLogViewModel> logger)
    {
        _connection = connection;
        _captures = captures;
        _logger = logger;

        Channels =
        [
            new NotificationChannel { ShortUuid = "2ACD", DisplayName = "2ACD — Treadmill Data" },
            new NotificationChannel { ShortUuid = "2ADA", DisplayName = "2ADA — Fitness Machine Status" },
            new NotificationChannel { ShortUuid = "2AD3", DisplayName = "2AD3 — Training Status" },
            new NotificationChannel { ShortUuid = "2A37", DisplayName = "2A37 — Heart Rate Measurement" },
        ];
    }

    public IReadOnlyList<NotificationChannel> Channels { get; }

    public ObservableCollection<NotificationLogRow> Rows { get; } = [];

    public ObservableCollection<FlagsSeen> FlagsTracker { get; } = [];

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private NotificationLogRow? selectedRow;

    /// <summary>
    /// Bound to the CollectionView's SelectedItem rather than a TapGestureRecognizer
    /// inside the item template — nested gesture recognizers are unreliable on
    /// Android's CollectionView. Reset to null immediately so tapping the same row
    /// again still opens the confirm dialog.
    /// </summary>
    partial void OnSelectedRowChanged(NotificationLogRow? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedRow = null;
        ConfirmRowCommand.Execute(value);
    }

    /// <summary>
    /// The Switch's IsToggled binding is TwoWay, so by the time this runs,
    /// <see cref="NotificationChannel.IsSubscribed"/> already holds the target state
    /// the operator asked for — true means "please subscribe", not "already subscribed".
    /// </summary>
    [RelayCommand]
    private async Task ToggleChannelAsync(NotificationChannel channel)
    {
        if (channel.IsSubscribed)
        {
            await SubscribeAsync(channel);
        }
        else
        {
            await UnsubscribeAsync(channel);
        }
    }

    private async Task SubscribeAsync(NotificationChannel channel)
    {
        var characteristic = FindCharacteristic(channel.ShortUuid);
        if (characteristic is null)
        {
            StatusMessage = $"{channel.ShortUuid} was not discovered on this device.";
            channel.IsSubscribed = false;
            return;
        }

        channel.Native = characteristic;
        characteristic.ValueUpdated += OnValueUpdated;

        try
        {
            await _connection.RunExclusiveAsync(() => characteristic.StartUpdatesAsync());
            channel.IsSubscribed = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartUpdatesAsync failed for {Uuid}", channel.ShortUuid);
            characteristic.ValueUpdated -= OnValueUpdated;
            channel.IsSubscribed = false;
            StatusMessage = $"Subscribe failed for {channel.ShortUuid}: {ex.Message}";
        }
    }

    private async Task UnsubscribeAsync(NotificationChannel channel)
    {
        if (channel.Native is { } characteristic)
        {
            characteristic.ValueUpdated -= OnValueUpdated;
            try
            {
                await _connection.RunExclusiveAsync(() => characteristic.StopUpdatesAsync());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StopUpdatesAsync failed for {Uuid}", channel.ShortUuid);
            }
        }

        channel.IsSubscribed = false;
    }

    private ICharacteristic? FindCharacteristic(string shortUuid) =>
        _connection.Services
            .SelectMany(s => s.Characteristics)
            .FirstOrDefault(c => string.Equals(c.ShortUuid, shortUuid, StringComparison.OrdinalIgnoreCase))
            ?.Native;

    private void OnValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var bytes = e.Characteristic.Value ?? [];
        var uuid = BleUuidHelpers.ToShortForm(e.Characteristic.Id);
        var now = DateTimeOffset.UtcNow;

        var channel = Channels.FirstOrDefault(c => string.Equals(c.ShortUuid, uuid, StringComparison.OrdinalIgnoreCase));
        channel?.RecordArrival(now);

        var recorder = _captures.EnsureSession();
        var captureId = recorder.WriteNotify(uuid, bytes);

        if (string.Equals(uuid, "2ACD", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 2)
        {
            var flagsHex = HexHelpers.ToHex(bytes.AsSpan(0, 2));
            _flagsSeen[flagsHex] = _flagsSeen.GetValueOrDefault(flagsHex) + 1;
            RefreshFlagsTracker();
        }

        if (IsPaused)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Rows.Insert(0, new NotificationLogRow(captureId, now, uuid, bytes.Length, HexHelpers.ToHex(bytes)));
            while (Rows.Count > MaxUiRows)
            {
                Rows.RemoveAt(Rows.Count - 1);
            }
        });
    }

    private void RefreshFlagsTracker()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            FlagsTracker.Clear();
            foreach (var (hex, count) in _flagsSeen.OrderByDescending(kv => kv.Value))
            {
                FlagsTracker.Add(new FlagsSeen(hex, count));
            }
        });
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private async Task ConfirmRowAsync(NotificationLogRow row)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
        {
            return;
        }

        var ok = await page.DisplayAlertAsync("Confirm packet", $"{row.Uuid} @ {row.TimestampUtc:HH:mm:ss.fff}\n{row.Hex}\n\nDid this match the physical result?", "Yes", "No");
        var note = await page.DisplayPromptAsync("Note", "What happened physically?", initialValue: string.Empty);
        _captures.EnsureSession().WriteNote(row.CaptureId, ok, note ?? string.Empty);
        StatusMessage = "Recorded.";
    }

    public void Dispose()
    {
        foreach (var channel in Channels)
        {
            if (channel.Native is { } native)
            {
                native.ValueUpdated -= OnValueUpdated;
            }
        }
    }
}
