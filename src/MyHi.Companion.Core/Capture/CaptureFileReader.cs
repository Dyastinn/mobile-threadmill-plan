using System.Text.Json;

namespace MyHi.Companion.Core.Capture;

/// <summary>
/// Reads a session JSONL file back. Tolerant of a truncated final line — a crash
/// mid-write must cost at most the last line, never the ones before it.
/// </summary>
public static class CaptureFileReader
{
    public static IReadOnlyList<CaptureEvent> ReadValidEvents(string path)
    {
        var results = new List<CaptureEvent>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            CaptureEvent? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<CaptureEvent>(line);
            }
            catch (JsonException)
            {
                // A partially-written last line looks like this. Stop rather than
                // throw: everything before it is still valid.
                break;
            }

            if (parsed is not null)
            {
                results.Add(parsed);
            }
        }

        return results;
    }
}

/// <summary>Session file naming: captures/session-YYYY-MM-DD-HHmm.jsonl.</summary>
public static class CaptureSession
{
    public static string CreateFileName(DateTimeOffset when) =>
        $"session-{when.UtcDateTime:yyyy-MM-dd-HHmm}.jsonl";
}
