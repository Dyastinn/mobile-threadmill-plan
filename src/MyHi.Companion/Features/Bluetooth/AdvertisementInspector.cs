using MyHi.Companion.Core.Formatting;
using Plugin.BLE.Abstractions;

namespace MyHi.Companion.Features.Bluetooth;

/// <summary>
/// Reads facts out of a scanned device's advertisement that the scan screen must
/// answer per TASKS.md 0.3 / ASSUMPTIONS.md A5-A6: is 0x1826 advertised, and what do
/// the raw bytes look like. Deliberately hex-only — no FTMS payload decoding here.
/// </summary>
public static class AdvertisementInspector
{
    public const ushort FitnessMachineServiceUuid = 0x1826;

    public static bool ContainsService16Bit(IReadOnlyList<AdvertisementRecord> records, ushort uuid)
    {
        foreach (var record in records)
        {
            if (record.Type is not (AdvertisementRecordType.UuidsComplete16Bit or AdvertisementRecordType.UuidsIncomple16Bit))
            {
                continue;
            }

            for (var i = 0; i + 1 < record.Data.Length; i += 2)
            {
                var candidate = (ushort)(record.Data[i] | (record.Data[i + 1] << 8));
                if (candidate == uuid)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>One line per AD structure: `{type byte} {data hex}`.</summary>
    public static string ToRawHex(IReadOnlyList<AdvertisementRecord> records)
    {
        if (records.Count == 0)
        {
            return string.Empty;
        }

        var lines = records.Select(r => $"{(byte)r.Type:X2} {HexHelpers.ToHex(r.Data)}");
        return string.Join('\n', lines);
    }
}
