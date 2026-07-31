namespace MyHi.Companion.Features.Bluetooth;

/// <summary>Short-form UUID extraction and friendly names for the GATT tree / read dump screens.</summary>
public static class BleUuidHelpers
{
    private const string BaseUuidSuffix = "-0000-1000-8000-00805f9b34fb";

    /// <summary>Returns the 4-hex-digit short form for a Bluetooth-base-UUID-derived GUID, else the full GUID string.</summary>
    public static string ToShortForm(Guid uuid)
    {
        var s = uuid.ToString();
        if (s.Length == 36
            && s.EndsWith(BaseUuidSuffix, StringComparison.OrdinalIgnoreCase)
            && s.StartsWith("0000", StringComparison.OrdinalIgnoreCase))
        {
            return s.Substring(4, 4).ToUpperInvariant();
        }

        return s.ToUpperInvariant();
    }
}

/// <summary>Known FTMS/GATT names, for display only — never used to gate behaviour.</summary>
public static class KnownBleNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1800"] = "Generic Access",
        ["180A"] = "Device Information",
        ["180D"] = "Heart Rate",
        ["FFE0"] = "Vendor (FitShow transparent serial)",
        ["FFF0"] = "Vendor (FitShow transparent serial)",
        ["1826"] = "Fitness Machine",
        ["2A00"] = "Device Name",
        ["2A24"] = "Model Number String",
        ["2A26"] = "Firmware Revision String",
        ["2A29"] = "Manufacturer Name String",
        ["2A37"] = "Heart Rate Measurement",
        ["2ACC"] = "Fitness Machine Feature",
        ["2ACD"] = "Treadmill Data",
        ["2AD3"] = "Training Status",
        ["2AD4"] = "Supported Speed Range",
        ["2AD9"] = "Fitness Machine Control Point",
        ["2ADA"] = "Fitness Machine Status",
        ["2902"] = "Client Characteristic Configuration",
    };

    public static string? Lookup(Guid uuid) => Names.GetValueOrDefault(BleUuidHelpers.ToShortForm(uuid));

    public static string? Lookup(string shortOrFullUuid)
    {
        var key = shortOrFullUuid.Length == 4 ? shortOrFullUuid : shortOrFullUuid.Replace("-", "");
        return Names.GetValueOrDefault(key);
    }
}
