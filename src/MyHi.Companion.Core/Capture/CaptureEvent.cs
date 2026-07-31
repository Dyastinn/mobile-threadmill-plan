using System.Text.Json.Serialization;

namespace MyHi.Companion.Core.Capture;

/// <summary>Event kinds from captures/README.md. Serialised lower-case.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CaptureEventKind>))]
public enum CaptureEventKind
{
    Read,
    Write,
    Notify,
    Indicate,
    Console,
    Note,
    Adv,
    Gatt,
}

/// <summary>
/// One line of a session capture file. Every event carries a monotonic <see cref="Id"/>
/// so a later "note" event can reference the exact byte-level event it is annotating
/// (captures/README.md shows `"ref":"&lt;event id&gt;"` on note lines).
/// </summary>
public sealed class CaptureEvent
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("t")]
    public required DateTimeOffset TimestampUtc { get; init; }

    [JsonPropertyName("kind")]
    public required CaptureEventKind Kind { get; init; }

    // read / write / notify / indicate
    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    [JsonPropertyName("hex")]
    public string? Hex { get; init; }

    // console
    [JsonPropertyName("speedKph")]
    public double? SpeedKph { get; init; }

    [JsonPropertyName("distanceM")]
    public double? DistanceMeters { get; init; }

    [JsonPropertyName("timeSec")]
    public int? TimeSeconds { get; init; }

    // note
    [JsonPropertyName("ref")]
    public long? RefId { get; init; }

    [JsonPropertyName("ok")]
    public bool? Ok { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    // adv
    [JsonPropertyName("name")]
    public string? DeviceName { get; init; }

    [JsonPropertyName("mac")]
    public string? MacAddress { get; init; }

    [JsonPropertyName("addressType")]
    public string? AddressType { get; init; }

    [JsonPropertyName("rawAdv")]
    public string? RawAdvertisement { get; init; }

    [JsonPropertyName("serviceUuids")]
    public string? ServiceUuids { get; init; }

    // gatt
    [JsonPropertyName("event")]
    public string? GattEvent { get; init; }

    [JsonPropertyName("status")]
    public int? GattStatus { get; init; }
}
