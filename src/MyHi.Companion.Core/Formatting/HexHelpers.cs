using System.Globalization;
using System.Text;

namespace MyHi.Companion.Core.Formatting;

/// <summary>
/// Hex encoding used everywhere raw bytes are shown to the operator: uppercase,
/// space-separated, leading zeros preserved. "02 00" and "2 0" are not the same
/// thing and the second is unusable as a parser fixture (see captures/README.md).
/// </summary>
public static class HexHelpers
{
    public static string ToHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(bytes.Length * 3 - 1);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    public static string ToHex(byte[] bytes) => ToHex(bytes.AsSpan());

    /// <summary>
    /// Parses whitespace-separated or contiguous hex ("02 8A 02" or "028A02") into
    /// bytes. Throws <see cref="FormatException"/> on odd-length or non-hex input,
    /// per the free-hex field's "is this parseable hex" contract (Phase 00 task 0.7).
    /// </summary>
    public static byte[] FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        Span<char> compact = hex.Length <= 256 ? stackalloc char[hex.Length] : new char[hex.Length];
        var len = 0;
        foreach (var ch in hex)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            compact[len++] = ch;
        }

        if (len == 0)
        {
            return [];
        }

        if (len % 2 != 0)
        {
            throw new FormatException($"Hex string has an odd number of digits ({len}): '{hex}'");
        }

        var result = new byte[len / 2];
        for (var i = 0; i < result.Length; i++)
        {
            var pair = compact.Slice(i * 2, 2);
            if (!byte.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
            {
                throw new FormatException($"'{pair}' is not a valid hex byte in '{hex}'");
            }
        }

        return result;
    }

    public static bool TryFromHex(string hex, out byte[] result)
    {
        try
        {
            result = FromHex(hex);
            return true;
        }
        catch (FormatException)
        {
            result = [];
            return false;
        }
    }
}
