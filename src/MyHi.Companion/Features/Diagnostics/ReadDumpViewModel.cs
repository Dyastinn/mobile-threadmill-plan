using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyHi.Companion.Core.Formatting;
using MyHi.Companion.Features.Bluetooth;
using MyHi.Companion.Features.Shared;

namespace MyHi.Companion.Features.Diagnostics;

/// <summary>One row: `uuid | name-if-known | length | hex bytes` (TASKS.md 0.5). Hex only — never decoded.</summary>
public sealed record ReadDumpRow(string ServiceUuid, string CharUuid, string? Name, int Length, string Hex);

public sealed partial class ReadDumpViewModel : BaseViewModel
{
    private readonly TreadmillConnection _connection;
    private readonly CaptureSessionManager _captures;
    private readonly ILogger<ReadDumpViewModel> _logger;

    public ReadDumpViewModel(TreadmillConnection connection, CaptureSessionManager captures, ILogger<ReadDumpViewModel> logger)
    {
        _connection = connection;
        _captures = captures;
        _logger = logger;
    }

    public ObservableCollection<ReadDumpRow> Rows { get; } = [];

    [RelayCommand]
    private async Task DumpAllAsync()
    {
        IsBusy = true;
        Rows.Clear();
        try
        {
            var recorder = _captures.EnsureSession();

            foreach (var service in _connection.Services)
            {
                foreach (var characteristic in service.Characteristics)
                {
                    if (!characteristic.Properties.HasFlag(Plugin.BLE.Abstractions.CharacteristicPropertyType.Read))
                    {
                        continue;
                    }

                    try
                    {
                        var (bytes, _) = await _connection.RunExclusiveAsync(() => characteristic.Native.ReadAsync());
                        var hex = HexHelpers.ToHex(bytes);
                        Rows.Add(new ReadDumpRow(service.ShortUuid, characteristic.ShortUuid, characteristic.Name, bytes.Length, hex));
                        recorder.WriteRead(characteristic.ShortUuid, bytes);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Read failed for {Uuid}", characteristic.ShortUuid);
                        Rows.Add(new ReadDumpRow(service.ShortUuid, characteristic.ShortUuid, characteristic.Name, 0, "(read failed)"));
                    }
                }
            }

            StatusMessage = $"Dumped {Rows.Count} characteristics.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static async Task CopyRowAsync(ReadDumpRow row) =>
        await Clipboard.Default.SetTextAsync($"{row.CharUuid} {row.Hex}");

    [RelayCommand]
    private async Task CopyAllAsync()
    {
        var sb = new StringBuilder();
        foreach (var row in Rows)
        {
            sb.AppendLine($"{row.ServiceUuid}/{row.CharUuid} | {row.Name} | {row.Length} | {row.Hex}");
        }

        await Clipboard.Default.SetTextAsync(sb.ToString());
        StatusMessage = "Copied all rows to clipboard.";
    }
}
