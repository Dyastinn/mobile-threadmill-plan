using System.Text.Json;
using System.Threading;
using MyHi.Companion.Core.Formatting;

namespace MyHi.Companion.Core.Capture;

/// <summary>
/// Append-only JSONL session writer. One event per line, flushed to disk on every
/// write so a crash costs at most the last (possibly partial) line, never the file
/// — see captures/README.md.
/// </summary>
public sealed class CaptureRecorder : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly StreamWriter _writer;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Lock _gate = new();
    private long _nextId = 1;

    public string FilePath { get; }

    public CaptureRecorder(string filePath, Func<DateTimeOffset>? clock = null)
    {
        FilePath = filePath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { NewLine = "\n", AutoFlush = false };
    }

    public long WriteRead(string uuid, byte[] bytes) =>
        Append(new CaptureEvent { Id = 0, TimestampUtc = _clock(), Kind = CaptureEventKind.Read, Uuid = uuid, Hex = HexHelpers.ToHex(bytes) });

    public long WriteWrite(string uuid, byte[] bytes) =>
        Append(new CaptureEvent { Id = 0, TimestampUtc = _clock(), Kind = CaptureEventKind.Write, Uuid = uuid, Hex = HexHelpers.ToHex(bytes) });

    public long WriteNotify(string uuid, byte[] bytes) =>
        Append(new CaptureEvent { Id = 0, TimestampUtc = _clock(), Kind = CaptureEventKind.Notify, Uuid = uuid, Hex = HexHelpers.ToHex(bytes) });

    public long WriteIndicate(string uuid, byte[] bytes) =>
        Append(new CaptureEvent { Id = 0, TimestampUtc = _clock(), Kind = CaptureEventKind.Indicate, Uuid = uuid, Hex = HexHelpers.ToHex(bytes) });

    public long WriteConsole(double? speedKph, double? distanceMeters, int? timeSeconds) =>
        Append(new CaptureEvent
        {
            Id = 0,
            TimestampUtc = _clock(),
            Kind = CaptureEventKind.Console,
            SpeedKph = speedKph,
            DistanceMeters = distanceMeters,
            TimeSeconds = timeSeconds,
        });

    public long WriteNote(long refId, bool ok, string text) =>
        Append(new CaptureEvent { Id = 0, TimestampUtc = _clock(), Kind = CaptureEventKind.Note, RefId = refId, Ok = ok, Text = text });

    public long WriteAdv(string deviceName, string macAddress, string addressType, string rawAdvertisement, string? serviceUuids = null) =>
        Append(new CaptureEvent
        {
            Id = 0,
            TimestampUtc = _clock(),
            Kind = CaptureEventKind.Adv,
            DeviceName = deviceName,
            MacAddress = macAddress,
            AddressType = addressType,
            RawAdvertisement = rawAdvertisement,
            ServiceUuids = serviceUuids,
        });

    public long WriteGatt(string gattEvent, int? status) =>
        Append(new CaptureEvent { Id = 0, TimestampUtc = _clock(), Kind = CaptureEventKind.Gatt, GattEvent = gattEvent, GattStatus = status });

    private long Append(CaptureEvent template)
    {
        lock (_gate)
        {
            var id = _nextId++;
            var withId = CloneWithId(template, id);
            var json = JsonSerializer.Serialize(withId, SerializerOptions);
            _writer.WriteLine(json);
            _writer.Flush();
            return id;
        }
    }

    private static CaptureEvent CloneWithId(CaptureEvent e, long id) => new()
    {
        Id = id,
        TimestampUtc = e.TimestampUtc,
        Kind = e.Kind,
        Uuid = e.Uuid,
        Hex = e.Hex,
        SpeedKph = e.SpeedKph,
        DistanceMeters = e.DistanceMeters,
        TimeSeconds = e.TimeSeconds,
        RefId = e.RefId,
        Ok = e.Ok,
        Text = e.Text,
        DeviceName = e.DeviceName,
        MacAddress = e.MacAddress,
        AddressType = e.AddressType,
        RawAdvertisement = e.RawAdvertisement,
        ServiceUuids = e.ServiceUuids,
        GattEvent = e.GattEvent,
        GattStatus = e.GattStatus,
    };

    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();
    }
}
