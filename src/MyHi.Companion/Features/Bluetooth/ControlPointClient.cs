using System.Threading;
using Microsoft.Extensions.Logging;
using MyHi.Companion.Core.Formatting;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace MyHi.Companion.Features.Bluetooth;

/// <summary>One request/response round trip on the control point, for the response panel (TASKS.md 0.7).</summary>
public sealed record ControlPointExchange(
    byte[] SentBytes,
    DateTimeOffset SentAtUtc,
    byte[]? ResponseBytes,
    DateTimeOffset? ResponseAtUtc,
    ControlPointResponse? Decoded,
    bool TimedOut)
{
    public TimeSpan? RoundTrip => ResponseAtUtc is { } at ? at - SentAtUtc : null;
}

/// <summary>
/// The mandatory sequence from 05-FTMS-Protocol.md §7: enable indications before
/// writing, Request Control before anything else, one outstanding write at a time
/// with a 3 s timeout, Write With Response always.
/// </summary>
public sealed class ControlPointClient : IDisposable
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger<ControlPointClient> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private ICharacteristic? _controlPoint;
    private TaskCompletionSource<(byte[] Bytes, DateTimeOffset At)>? _pendingIndication;

    public ControlPointClient(ILogger<ControlPointClient> logger)
    {
        _logger = logger;
    }

    public bool IndicationsEnabled { get; private set; }

    public bool HasControl { get; private set; }

    /// <summary>Enables indications on entering the control console. Automatic, per TASKS.md 0.7 step 1.</summary>
    public async Task<bool> BindAsync(ICharacteristic controlPoint)
    {
        Unbind();
        _controlPoint = controlPoint;
        _controlPoint.WriteType = CharacteristicWriteType.WithResponse;
        _controlPoint.ValueUpdated += OnValueUpdated;

        try
        {
            await _controlPoint.StartUpdatesAsync();
            IndicationsEnabled = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable indications on 0x2AD9");
            IndicationsEnabled = false;
        }

        HasControl = false;
        return IndicationsEnabled;
    }

    public void Unbind()
    {
        if (_controlPoint is not null)
        {
            _controlPoint.ValueUpdated -= OnValueUpdated;
        }

        _controlPoint = null;
        IndicationsEnabled = false;
        HasControl = false;
    }

    private void OnValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var bytes = e.Characteristic.Value ?? [];
        _pendingIndication?.TrySetResult((bytes, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Serialised: one outstanding command, 3 s timeout, per 05-FTMS-Protocol.md §7.
    /// Never throws on a machine-level failure or timeout — those are findings the
    /// console displays, not exceptions.
    /// </summary>
    public async Task<ControlPointExchange> SendAsync(byte[] bytes, CancellationToken ct = default)
    {
        if (_controlPoint is null)
        {
            throw new InvalidOperationException("Control point is not bound. Enter the control console while connected first.");
        }

        await _writeGate.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<(byte[], DateTimeOffset)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingIndication = tcs;

            var sentAt = DateTimeOffset.UtcNow;
            var writeStatus = await _controlPoint.WriteAsync(bytes, ct);
            if (writeStatus != 0)
            {
                _logger.LogWarning("Control point write returned non-zero GATT status {Status} for {Hex}", writeStatus, HexHelpers.ToHex(bytes));
            }

            var timeoutTask = Task.Delay(ResponseTimeout, ct);
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completed != tcs.Task)
            {
                _logger.LogWarning("Control point command {Hex} timed out after {Timeout}s with no indication", HexHelpers.ToHex(bytes), ResponseTimeout.TotalSeconds);
                return new ControlPointExchange(bytes, sentAt, null, null, null, TimedOut: true);
            }

            var (responseBytes, responseAt) = await tcs.Task;
            ControlPointResponseParser.TryParse(responseBytes, out var decoded);

            if (bytes.Length > 0 && bytes[0] == (byte)FtmsOpCode.RequestControl)
            {
                HasControl = decoded?.IsSuccess == true;
            }
            else if (decoded?.ResultCode == FtmsResultCode.ControlNotPermitted)
            {
                HasControl = false;
            }

            return new ControlPointExchange(bytes, sentAt, responseBytes, responseAt, decoded, TimedOut: false);
        }
        finally
        {
            _pendingIndication = null;
            _writeGate.Release();
        }
    }

    public Task<ControlPointExchange> RequestControlAsync(CancellationToken ct = default) =>
        SendAsync(FtmsCommands.RequestControl(), ct);

    public void Dispose()
    {
        Unbind();
        _writeGate.Dispose();
    }
}
