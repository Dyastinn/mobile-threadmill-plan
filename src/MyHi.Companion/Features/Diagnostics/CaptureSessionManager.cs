using MyHi.Companion.Core.Capture;

namespace MyHi.Companion.Features.Diagnostics;

/// <summary>
/// Owns the lifecycle of the current session capture file (TASKS.md 0.8). Captures
/// live in app-private storage; the operator shares the file out and commits it
/// under captures/ in the repo per HUMAN-RUNBOOK.md.
/// </summary>
public sealed class CaptureSessionManager
{
    private readonly string _capturesDirectory;

    public CaptureSessionManager(string capturesDirectory)
    {
        _capturesDirectory = capturesDirectory;
        Directory.CreateDirectory(_capturesDirectory);
    }

    public CaptureRecorder? Current { get; private set; }

    public event EventHandler? SessionChanged;

    public CaptureRecorder StartNewSession()
    {
        Current?.Dispose();
        var path = Path.Combine(_capturesDirectory, CaptureSession.CreateFileName(DateTimeOffset.UtcNow));
        Current = new CaptureRecorder(path);
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public CaptureRecorder EnsureSession() => Current ?? StartNewSession();

    public IReadOnlyList<CaptureSessionSummary> ListSessions()
    {
        if (!Directory.Exists(_capturesDirectory))
        {
            return [];
        }

        return Directory.GetFiles(_capturesDirectory, "session-*.jsonl")
            .OrderByDescending(f => f)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var eventCount = CountLines(path);
                return new CaptureSessionSummary(path, info.Name, info.Length, eventCount, info.LastWriteTimeUtc);
            })
            .ToList();
    }

    private static int CountLines(string path)
    {
        try
        {
            return File.ReadLines(path).Count(l => !string.IsNullOrWhiteSpace(l));
        }
        catch (IOException)
        {
            return 0;
        }
    }

    public void DeleteSession(string path)
    {
        if (Current?.FilePath == path)
        {
            Current.Dispose();
            Current = null;
        }

        File.Delete(path);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record CaptureSessionSummary(string FilePath, string FileName, long SizeBytes, int EventCount, DateTime LastWriteUtc);
