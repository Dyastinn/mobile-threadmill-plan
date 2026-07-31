using MyHi.Companion.Core.Capture;

namespace MyHi.Companion.Tests.Capture;

public class CaptureRecorderTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("myhi-capture-tests-").FullName;

    private string PathFor(string name) => Path.Combine(_tempDir, name);

    [Fact]
    public void Writes_valid_jsonl_one_event_per_line()
    {
        var path = PathFor("session.jsonl");
        using (var recorder = new CaptureRecorder(path, () => new DateTimeOffset(2026, 7, 31, 14, 22, 31, 402, TimeSpan.Zero)))
        {
            recorder.WriteWrite("2AD9", [0x02, 0x8A, 0x02]);
            recorder.WriteIndicate("2AD9", [0x80, 0x02, 0x01]);
            recorder.WriteConsole(6.5, 420, 310);
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);

        var events = CaptureFileReader.ReadValidEvents(path);
        Assert.Equal(3, events.Count);

        Assert.Equal(CaptureEventKind.Write, events[0].Kind);
        Assert.Equal("2AD9", events[0].Uuid);
        Assert.Equal("02 8A 02", events[0].Hex);

        Assert.Equal(CaptureEventKind.Indicate, events[1].Kind);
        Assert.Equal("80 02 01", events[1].Hex);

        Assert.Equal(CaptureEventKind.Console, events[2].Kind);
        Assert.Equal(6.5, events[2].SpeedKph);
        Assert.Equal(420, events[2].DistanceMeters);
        Assert.Equal(310, events[2].TimeSeconds);
    }

    [Fact]
    public void Assigns_monotonic_ids_that_note_events_can_reference()
    {
        var path = PathFor("session.jsonl");
        long writeId;
        using (var recorder = new CaptureRecorder(path))
        {
            writeId = recorder.WriteWrite("2AD9", [0x07]);
            recorder.WriteNote(writeId, ok: true, text: "belt actually started");
        }

        var events = CaptureFileReader.ReadValidEvents(path);
        var note = events.Single(e => e.Kind == CaptureEventKind.Note);
        Assert.Equal(writeId, note.RefId);
        Assert.True(note.Ok);
        Assert.Equal("belt actually started", note.Text);
    }

    [Fact]
    public void Is_append_only_across_multiple_recorder_instances()
    {
        var path = PathFor("session.jsonl");
        using (var first = new CaptureRecorder(path))
        {
            first.WriteWrite("2AD9", [0x00]);
        }

        using (var second = new CaptureRecorder(path))
        {
            second.WriteWrite("2AD9", [0x01]);
        }

        var events = CaptureFileReader.ReadValidEvents(path);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Survives_a_mid_write_kill_reading_only_complete_lines()
    {
        var path = PathFor("session.jsonl");
        using (var recorder = new CaptureRecorder(path))
        {
            recorder.WriteWrite("2AD9", [0x00]);
            recorder.WriteIndicate("2AD9", [0x80, 0x00, 0x01]);
        }

        // Simulate a crash mid-write: a third line that never finished flushing.
        File.AppendAllText(path, """{"id":3,"t":"2026-07-31T14:22:33.000Z","kind":"notify","uuid":"2ACD","hex":"08 0""");

        var events = CaptureFileReader.ReadValidEvents(path);

        Assert.Equal(2, events.Count);
        Assert.Equal(CaptureEventKind.Write, events[0].Kind);
        Assert.Equal(CaptureEventKind.Indicate, events[1].Kind);
    }

    [Fact]
    public void CreateFileName_matches_session_naming_convention()
    {
        var when = new DateTimeOffset(2026, 7, 31, 19, 30, 0, TimeSpan.Zero);
        Assert.Equal("session-2026-07-31-1930.jsonl", CaptureSession.CreateFileName(when));
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }
}
