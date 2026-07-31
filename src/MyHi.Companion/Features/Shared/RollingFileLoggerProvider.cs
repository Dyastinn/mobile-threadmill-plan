using System.Threading;
using Microsoft.Extensions.Logging;

namespace MyHi.Companion.Features.Shared;

/// <summary>
/// Logs to logcat (via Microsoft.Extensions.Logging.Debug, wired separately in
/// MauiProgram) and to a rolling set of files, per TASKS.md 0.10. GATT status codes
/// (133 will appear) must be greppable rather than buried in an inner exception, so
/// every log call should include the numeric code in the message itself.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();

    public string LogDirectory { get; }

    public string CurrentLogFilePath { get; }

    public RollingFileLoggerProvider(string logDirectory, int filesToKeep = 5)
    {
        LogDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
        PruneOldFiles(logDirectory, filesToKeep - 1);

        CurrentLogFilePath = Path.Combine(logDirectory, $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var stream = new FileStream(CurrentLogFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = false };
    }

    private static void PruneOldFiles(string directory, int keep)
    {
        var files = Directory.GetFiles(directory, "app-*.log").OrderByDescending(f => f).ToList();
        foreach (var stale in files.Skip(keep))
        {
            try
            {
                File.Delete(stale);
            }
            catch (IOException)
            {
                // Best effort; a locked file from a previous crashed run is not fatal.
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(categoryName, this);

    internal void WriteLine(string line)
    {
        lock (_gate)
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();
    }
}

internal sealed class RollingFileLogger(string categoryName, RollingFileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var line = $"{DateTimeOffset.Now:HH:mm:ss.fff} [{logLevel}] {categoryName}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        provider.WriteLine(line);
    }
}
