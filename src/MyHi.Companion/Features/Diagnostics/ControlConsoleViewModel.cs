using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyHi.Companion.Core.Formatting;
using MyHi.Companion.Features.Bluetooth;
using MyHi.Companion.Features.Shared;

namespace MyHi.Companion.Features.Diagnostics;

/// <summary>Formatted for the response panel exactly as TASKS.md 0.7 specifies.</summary>
public sealed record ControlExchangeDisplay(string SentLine, string ResponseLine, string? DecodedBlock);

/// <summary>
/// The control point console — the centrepiece of Phase 00. Enables indications on
/// entry, gates everything else behind Request Control, and shows the raw response
/// plus its decoded meaning for every command (05-FTMS-Protocol.md §7).
/// </summary>
public sealed partial class ControlConsoleViewModel : BaseViewModel
{
    private readonly ControlPointClient _controlPoint;
    private readonly TreadmillConnection _connection;
    private readonly CaptureSessionManager _captures;
    private readonly ILogger<ControlConsoleViewModel> _logger;

    public ControlConsoleViewModel(
        ControlPointClient controlPoint,
        TreadmillConnection connection,
        CaptureSessionManager captures,
        ILogger<ControlConsoleViewModel> logger)
    {
        _controlPoint = controlPoint;
        _connection = connection;
        _captures = captures;
        _logger = logger;
    }

    public ObservableCollection<ControlExchangeDisplay> Entries { get; } = [];

    public bool IndicationsEnabled => _controlPoint.IndicationsEnabled;

    public bool HasControl => _controlPoint.HasControl;

    [ObservableProperty]
    private string targetSpeedInput = "2.0";

    [ObservableProperty]
    private string? targetSpeedPreviewHex;

    [ObservableProperty]
    private string freeHexInput = string.Empty;

    partial void OnTargetSpeedInputChanged(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var kph))
        {
            try
            {
                TargetSpeedPreviewHex = HexHelpers.ToHex(FtmsCommands.SetTargetSpeed(kph));
            }
            catch (Exception)
            {
                TargetSpeedPreviewHex = "(out of range)";
            }
        }
        else
        {
            TargetSpeedPreviewHex = null;
        }
    }

    [RelayCommand]
    private async Task EnterAsync()
    {
        var characteristic = _connection.Services
            .SelectMany(s => s.Characteristics)
            .FirstOrDefault(c => c.ShortUuid == "2AD9")
            ?.Native;

        if (characteristic is null)
        {
            StatusMessage = "0x2AD9 control point was not discovered on this device.";
            return;
        }

        var enabled = await _controlPoint.BindAsync(characteristic);
        StatusMessage = enabled
            ? "Indications enabled on 0x2AD9. Request Control before anything else."
            : "Failed to enable indications on 0x2AD9 — commands will likely be dropped.";
        OnPropertyChanged(nameof(IndicationsEnabled));
        OnPropertyChanged(nameof(HasControl));
    }

    [RelayCommand]
    private Task RequestControlAsync() => SendAsync(FtmsCommands.RequestControl());

    [RelayCommand]
    private Task ResetAsync() => SendAsync(FtmsCommands.Reset());

    [RelayCommand]
    private Task PauseAsync() => SendAsync(FtmsCommands.Pause());

    [RelayCommand]
    private Task StopAsync() => SendAsync(FtmsCommands.Stop());

    [RelayCommand]
    private async Task StartAsync()
    {
        var page = CurrentPage();
        if (page is null)
        {
            return;
        }

        var confirmed = await page.DisplayAlertAsync(
            "Start",
            "This can start the belt moving.\n\nThe physical safety key is the emergency stop. This app is not.",
            "Start", "Cancel");

        if (confirmed)
        {
            await SendAsync(FtmsCommands.StartOrResume());
        }
    }

    [RelayCommand]
    private async Task SetTargetSpeedAsync()
    {
        if (!double.TryParse(TargetSpeedInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var kph))
        {
            StatusMessage = "Enter a valid speed in km/h.";
            return;
        }

        byte[] bytes;
        try
        {
            bytes = FtmsCommands.SetTargetSpeed(kph);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cannot encode {kph}: {ex.Message}";
            return;
        }

        var page = CurrentPage();
        if (page is null)
        {
            return;
        }

        var confirmed = await page.DisplayAlertAsync(
            "Set Target Speed",
            $"Sending {HexHelpers.ToHex(bytes)} for {kph:0.0} km/h. This can start the belt moving.\n\nThe physical safety key is the emergency stop. This app is not.",
            "Send", "Cancel");

        if (confirmed)
        {
            await SendAsync(bytes);
        }
    }

    [RelayCommand]
    private async Task SendFreeHexAsync()
    {
        if (!HexHelpers.TryFromHex(FreeHexInput, out var bytes) || bytes.Length == 0)
        {
            StatusMessage = "That is not parseable hex.";
            return;
        }

        await SendAsync(bytes);
    }

    private async Task SendAsync(byte[] bytes)
    {
        IsBusy = true;
        try
        {
            var exchange = await _controlPoint.SendAsync(bytes);
            LogExchange(exchange);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LogExchange(ControlPointExchange exchange)
    {
        var recorder = _captures.EnsureSession();
        recorder.WriteWrite("2AD9", exchange.SentBytes);
        if (exchange.ResponseBytes is { } bytes)
        {
            recorder.WriteIndicate("2AD9", bytes);
        }

        Entries.Insert(0, Format(exchange));
        OnPropertyChanged(nameof(HasControl));
    }

    private static ControlExchangeDisplay Format(ControlPointExchange ex)
    {
        var sentLine = $"→ {HexHelpers.ToHex(ex.SentBytes)}   (sent {ex.SentAtUtc:HH:mm:ss.fff})";

        if (ex.TimedOut)
        {
            return new ControlExchangeDisplay(sentLine, "← (no indication within 3 s — timeout)", null);
        }

        if (ex.ResponseBytes is not { } responseBytes)
        {
            return new ControlExchangeDisplay(sentLine, "← (no response)", null);
        }

        var rtt = ex.RoundTrip is { } ts ? $", +{ts.TotalMilliseconds:0} ms" : string.Empty;
        var responseLine = $"← {HexHelpers.ToHex(responseBytes)}   (indication {ex.ResponseAtUtc:HH:mm:ss.fff}{rtt})";

        var decoded = ex.Decoded is { } d
            ? $"Response Code  0x{d.ResponseCode:X2}\nRequest Op     0x{d.RequestOpCodeRaw:X2}  {d.DescribeRequestOpCode()}\nResult         0x{d.ResultCodeRaw:X2}  {d.DescribeResultCode()}"
            : "(response shorter than 3 bytes — cannot decode)";

        return new ControlExchangeDisplay(sentLine, responseLine, decoded);
    }

    private static Page? CurrentPage() => Application.Current?.Windows.FirstOrDefault()?.Page;
}
