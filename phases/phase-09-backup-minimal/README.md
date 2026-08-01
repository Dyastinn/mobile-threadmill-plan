# Phase 09 — Backup (minimal)

> Deliberately scoped down. Merge mode, CSV, and version migration are Phase 15. This
> phase exists to make data loss survivable, nothing more.

**Hardware:** none · **Size:** M · **Blocked by:** Phase 08

---

## Goal

Get workout history off this phone and onto another one, without ever leaving the
database in a state worse than before the attempt.

### Understanding what you're building (read this before the tasks)

**The everyday problem.** Imagine you're moving apartments and you can only make
one non-negotiable promise to yourself: nothing that goes into a box disappears
before it comes out the other end. Whether you also unpack faster, color-code the
boxes, or merge duplicate furniture from both places is a nice-to-have for a
later trip. This phase is that promise, applied to a phone: get every workout off
this device and onto another one, intact, even if the process is interrupted
halfway through. The line at the top of this phase's doc says exactly that —
"without ever leaving the database in a state worse than before the attempt."
Everything else — merging two phones' histories, a CSV export for spreadsheets,
an older backup reading correctly in a newer app version — is explicitly Phase
15's problem, not this one's.

**Why "minimal" isn't corner-cutting.** The tempting alternative, once you're
deep in `BackupExporter`/`BackupImporter` with the ZIP format and database
queries fresh in your head, is to build the complete version now: add merge
mode, add CSV, add version migration while you're already in the file. Each of
the three is deferred for a specific, demonstrable reason, not laziness.
**Merge mode** needs conflict-resolution rules for what happens when the same
`WorkoutId` appears on both phones with a different `Notes` field or a different
`EndedAtUtc` — rules nobody can write correctly today, because there's no real
case yet to write them against; guessing now means designing against an imagined
scenario instead of an observed one. **CSV** is a second output format with its
own schema-mapping problem — `samples.json`'s nested, per-workout structure
doesn't flatten into spreadsheet rows the same way `workouts.json` does — and
nothing in this phase's scope actually needs it; the single ZIP already
satisfies "get the data off the phone." **Version migration** is, almost by
definition, something you cannot build correctly before a second format version
exists to migrate *from* — `backupFormatVersion` is in the manifest today
specifically so a real migration can be written later against a real old format,
not an invented one now. So the "Out" column in the Scope section isn't three
shortcuts, it's three things that are genuinely impossible to build well yet,
deferred to the point where they become buildable. What's left in scope — full
export, replace-only import, automatic local backup, pre-import safety backup —
is the complete list of what's needed to make "you can always get your data
back" true today, and no smaller.

**The pattern, named plainly.** The zip-slip guard, decompression cap, and
format-version check in `BackupImporter` (task 9.3, steps 1–3) are the same
**fail-fast-at-the-trust-boundary** idea Phase 01a applied to BLE packets: check
everything *before* trusting any of it, reject loudly and immediately, never
partway through real work. The boundary just moved — there, the untrusted input
was bytes off a Bluetooth radio; here, it's a ZIP file that might be corrupted,
hand-edited, or actively hostile (a `../` entry trying to write outside the
target directory is the same category of problem as a truncated BLE packet: data
from outside the program's control, shaped to look valid). The cost is the same
shape too — a handful of checks (an entry-name allowlist, a summed-length check,
one manifest field read) before any real import work starts. The payoff is what
makes it worth it here specifically: without the allowlist check, a crafted ZIP
entry could write a file anywhere the app has permission to write; without the
pre-import safety backup and the single SQLite transaction (task 9.3, steps 4–5),
an import that fails at row 40,000 of `samples.json` leaves the user with neither
their old data nor their new data — for a feature whose entire purpose is "never
lose the user's workout history," a validation gap here would defeat the phase's
own point. This is exactly what Uncle Bob's framing is actually about: fail-fast
isn't sprinkled everywhere in this codebase, it's applied precisely at the
boundary where control passes from something you don't trust (a file the user
picked off their filesystem) to something you do (your own database).

## Learning goals

- **Splitting a feature across the `Core`/app seam a third time.** The ZIP-building,
  the SQL, the JSON streaming, and every safety check (zip-slip, decompression cap,
  atomic export, single-transaction replace) are pure logic with zero MAUI
  dependency — they belong in `Core`, get real xUnit tests, and are built the same
  "spec, not code" way as `FakeTreadmillService` (Phase 01b) and `AppSettingsService`
  (Phase 08). Only the *transfer* — the share sheet, the file picker, the confirmation
  dialog — touches a MAUI API and lives in the app project.
- **Streaming JSON**, not `JsonSerializer.Deserialize<List<T>>` — the difference
  between reading a whole multi-megabyte array into memory and reading it one element
  at a time. This is the actual mechanism behind "streaming import" in the scope
  below, not just a phrase.
- **Why settings are applied *after* the database transaction commits, never
  during it.** `Preferences` has no transaction of its own — see "Safety
  requirements" below for the exact reasoning, ported straight from the project plan
  because it is not optional.

## Scope

**In:** full export to a single ZIP · import **Replace mode only** · automatic local
backup on workout finish (last 5 kept, app-private storage) · pre-import safety backup.

**Out (Phase 15):** merge mode · CSV export · backup format *migrations* (the
mechanism ships here; migrations do not) · statistics/PR export — those are **derived
from workouts and must be recomputed on import**, never exported. Exporting derived
data creates two sources of truth.

## Backup contents

```
MyHiBackup_2026-07-28_1930.zip
├── manifest.json     (backupFormatVersion, app version, export date, counts)
├── workouts.json     (headers, keyed by WorkoutId GUID)
├── samples.json      (telemetry, keyed by WorkoutId GUID)
├── devices.json
└── settings.json
```

One manifest, not a `metadata.json` + `version.json` pair — two files with overlapping
responsibility drift apart.

`backupFormatVersion` is an **integer independent of the app version**. A UI bugfix
bumping the app version must not invalidate backups.

Identities in the file are `WorkoutId` and `DeviceUid`. **Integer `Id` values are not
exported** and are reassigned on import — see `14-Database.md`'s "Backup mapping"
section, which this phase implements field-for-field (task 9.1 below mirrors its
`Workout`/`WorkoutSample`/`Device` columns exactly).

---

## Transfer mechanism

- **Export: share sheet.** Build the ZIP in `FileSystem.CacheDirectory`, then
  `Share.Default.RequestAsync(new ShareFileRequest(...))`. Zero storage permissions,
  one tap to Drive/Gmail/Nearby Share, sidesteps scoped storage entirely.
- **Optional secondary:** "Save to device" via `CommunityToolkit.Maui` `FileSaver`. It
  has a history of permission failures on API 33+ and of returning unresolvable
  `content://` paths when the user picks a cloud provider. Secondary button, not the
  primary path.
- **Import:** `FilePicker.Default.PickAsync`. **MIME filtering for ZIP is inconsistent**
  across Android file providers (`application/zip`, `application/octet-stream`, `*/*`).
  Accept broadly and validate by reading the archive header, not the extension.
- **Do not** hardcode `Documents/MyHi Companion/Backup/`. Since Android 10 scoped
  storage an app cannot freely create and write that path.

---

## Safety requirements — these are the point of the phase

**Replace must not be destructive on failure.** "Delete current data, then restore" —
if that throws at file 3 of 5 the user has neither. Required:

1. Write an automatic pre-import backup to app-private storage **first**
2. Perform the entire data import inside **one SQLite transaction**
3. Apply settings **after** the transaction commits — `Preferences` sits outside the
   transaction, and losing six toggles is recoverable while losing years of workouts
   is not
4. Offer "undo last import" for one session

Also:

- **Atomic export:** write to `.tmp`, then rename. An interrupted export must not leave
  a truncated file with a valid-looking name.
- **Zip-slip guard:** reject any entry whose name is not in the known filename
  allowlist. Three lines, closes an entire bug class.
- **Decompression cap:** reject archives whose uncompressed size exceeds ~500 MB.
- **Streaming import.** With telemetry included, backups reach tens of MB. Do not
  deserialise the whole document into memory.
- **Block export during an active workout**; **block import while connected or
  mid-workout.**
- **Timestamps are UTC plus stored offset**, serialised as ISO 8601 with offset.
- **Restored saved devices will not auto-connect** on a new phone — the MAC restores
  but the bond does not. Tell the user they need to reconnect once, rather than letting
  it look broken.

---

## Reference docs

- **`ZipFile` class** (`ZipFile.CreateFromDirectory`, `ZipFile.OpenRead`) —
  https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zipfile —
  everything task 9.2/9.3 need is on this one page; no third-party ZIP library.
- **`JsonSerializer.DeserializeAsyncEnumerable`** —
  https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer.deserializeasyncenumerable —
  read this before task 9.3. It streams a root-level JSON *array* one element at a
  time — exactly the shape of `workouts.json`/`samples.json`/`devices.json` — which is
  what makes "streaming import" concrete rather than aspirational.
- **`Microsoft.Data.Sqlite` transactions** —
  https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions —
  the single-transaction replace-import in task 9.3 is exactly the pattern this page
  describes; `MigrationRunner.Apply` (Phase 00) already uses the same
  `BeginTransaction`/`Commit` shape if you want a second example in this codebase.
- **`Share` (`Share.RequestAsync`)** —
  https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/data/share —
  `HomeViewModel.ShareLogAsync` (Phase 00) already calls this for the log file; task
  9.7's export button does the same thing with the backup ZIP instead.
- **`FilePicker`** —
  https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker —
  read the "pick a file" section for `PickOptions`; task 9.7's import button uses it.
- **File system helpers (`FileSystem.CacheDirectory`)** —
  https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-system-helpers —
  the export target directory; note the platform note that the OS may clear this
  storage, which is exactly why the pre-import *safety* backup in task 9.3 goes to
  `FileSystem.AppDataDirectory` instead (app-private, not cache — the two are not
  interchangeable in this phase).
- **`CommunityToolkit.Maui` `FileSaver`** —
  https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/essentials/file-saver —
  already listed in `docs/learning/03-Doc-Links.md`; used only for the optional
  secondary "Save a Copy to Device" button in task 9.8.
- **Getting started with `CommunityToolkit.Maui`** —
  https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/get-started —
  the package isn't referenced yet (check `MyHi.Companion.csproj` — it currently only
  has `CommunityToolkit.Mvvm`, a different package). Task 9.8 adds it and calls
  `.UseMauiCommunityToolkit()`.

---

## Walkthrough

### 9.1 — Export DTOs and manifest (data shapes — write these directly)

Creates: `src/MyHi.Companion.Core/Backup/BackupDtos.cs`.

These are plain data records, not logic — same category as `NavDestination` or
`TreadmillSample`, safe to write in full rather than from a skeleton. Every field maps
1:1 to a column in `14-Database.md`, with the portable GUID identities swapped in for
the integer `Id`/`WorkoutRowId`/`DeviceId` columns that never leave the phone:

```csharp
namespace MyHi.Companion.Core.Backup;

public sealed record BackupManifest(
    int BackupFormatVersion,
    string AppVersion,
    DateTimeOffset ExportedAtUtc,
    int WorkoutCount,
    int SampleCount,
    int DeviceCount);

public sealed record WorkoutExportDto(
    string WorkoutId,              // GUID — Workout.Id (integer) is never exported
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int StartOffsetMinutes,
    int DurationSeconds,
    double DistanceMeters,
    int Calories,
    double AvgSpeedKph,
    double MaxSpeedKph,
    int? AvgHeartRate,
    int? MaxHeartRate,
    int Status,
    string? DeviceUid,             // Device.DeviceUid — not Workout.DeviceId
    string? Notes);

public sealed record WorkoutSampleExportDto(
    string WorkoutId,              // joins back to WorkoutExportDto.WorkoutId,
                                    // not the integer WorkoutRowId
    int ElapsedSec,
    double? SpeedKph,
    double? DistanceM,
    int? Calories,
    int? HeartRate,
    int Flags);

public sealed record DeviceExportDto(
    string DeviceUid,
    string MacAddress,
    string Name,
    DateTimeOffset? LastSeenUtc,
    bool IsPreferred);
```

`settings.json` doesn't need its own DTO — it's just the
`IReadOnlyDictionary<string,string>` that `AppSettingsService` (Phase 08) already
speaks in terms of its `Preferences` keys. Add these two methods to
`AppSettingsService` now (small enough to write directly, no skeleton needed — it's
straight delegation to the store you already wrote):

```csharp
// In AppSettingsService (src/MyHi.Companion.Core/Settings/AppSettingsService.cs)
public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>
{
    [ThemeKey] = Theme.ToString(),
    [UnitsKey] = Units.ToString(),
    // TODO: the remaining bool-backed keys — store their string form ("True"/"False")
    // the same way; RestoreFrom (below) is what parses them back
};

public void RestoreFrom(IReadOnlyDictionary<string, string> snapshot)
{
    // TODO: for each known key present in snapshot, write it back through the
    // corresponding property (Theme = ..., Units = ..., AutoReconnect = ...).
    // Unknown keys are ignored, not an error — a newer backup may carry a setting
    // this version doesn't have yet.
}
```

### 9.2 — `BackupExporter` (the logic — you write this)

Creates: `src/MyHi.Companion.Core/Backup/BackupExporter.cs`.

```csharp
using System.IO.Compression;
using System.Text.Json;
using MyHi.Companion.Core.Data;

namespace MyHi.Companion.Core.Backup;

public sealed class BackupExporter(SqliteConnectionFactory connectionFactory)
{
    public const int CurrentFormatVersion = 1;

    internal static readonly string[] EntryNames =
        ["manifest.json", "workouts.json", "samples.json", "devices.json", "settings.json"];

    /// <summary>
    /// Builds a backup ZIP inside <paramref name="workingDirectory"/> and returns its
    /// path. Deliberately takes a plain directory string, not FileSystem.CacheDirectory
    /// directly — that's a MAUI type this class must never reference; the caller (the
    /// app-layer ViewModel in task 9.7) decides which directory.
    /// </summary>
    public async Task<string> ExportAsync(
        string workingDirectory,
        string appVersion,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        CancellationToken ct = default)
    {
        // TODO, in order:
        // 1. using var connection = connectionFactory.Create();
        // 2. Query Workout LEFT JOIN Device ON Workout.DeviceId = Device.Id — LEFT
        //    JOIN because DeviceId can be NULL — selecting Device.DeviceUid instead of
        //    the integer DeviceId. Map each row to a WorkoutExportDto.
        // 3. Query WorkoutSample JOIN Workout ON WorkoutSample.WorkoutRowId = Workout.Id
        //    to translate WorkoutRowId -> the GUID WorkoutId for every sample. Map to
        //    WorkoutSampleExportDto.
        // 4. Query Device; map to DeviceExportDto.
        // 5. Serialize each list, and settingsSnapshot, to
        //    <workingDirectory>/<entry-name>.json using
        //    JsonSerializer.SerializeAsync against a FileStream — the stream overload,
        //    not the string-returning one, because samples.json can be tens of MB.
        // 6. Build a BackupManifest from the counts you already have (step 2-4's list
        //    lengths) and write manifest.json last.
        // 7. var tempZipPath = Path.Combine(workingDirectory, $"{fileName}.tmp");
        //    ZipFile.CreateFromDirectory(workingDirectory, tempZipPath) — this is what
        //    picks up the five *.json files you just wrote — then File.Move(tempZipPath,
        //    finalPath, overwrite: true). This two-step write-then-rename is the
        //    "atomic export" requirement: a crash mid-zip leaves only a stray .tmp
        //    file, never a truncated file under the real name.
        // 8. Delete the five loose *.json files (they've served their purpose once
        //    zipped) and return finalPath.
        throw new NotImplementedException();
    }
}
```

### 9.3 — `BackupImporter` (the logic — you write this)

Creates: `src/MyHi.Companion.Core/Backup/BackupImporter.cs`.

This is the phase's highest-stakes file — every numbered step below is one of the
"Safety requirements" above turned into code, not extra caution:

```csharp
using System.IO.Compression;
using System.Text.Json;
using MyHi.Companion.Core.Data;

namespace MyHi.Companion.Core.Backup;

public sealed record BackupImportResult(
    int WorkoutCount,
    int SampleCount,
    int DeviceCount,
    IReadOnlyDictionary<string, string> Settings);

public sealed record BackupImportProgress(string Stage, double FractionComplete);

public sealed class BackupValidationException(string message) : Exception(message);

public sealed class BackupImporter(
    SqliteConnectionFactory connectionFactory,
    BackupExporter preImportBackupExporter,
    string preImportBackupDirectory)
{
    private const long MaxUncompressedBytes = 500L * 1024 * 1024;

    public async Task<BackupImportResult> ImportAsync(
        string zipPath,
        IProgress<BackupImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        // TODO, in order — do not reorder these, each one guards the next:
        //
        // 1. ZIP-SLIP GUARD, first pass, before extracting anything:
        //    using var archive = ZipFile.OpenRead(zipPath);
        //    foreach entry in archive.Entries, throw BackupValidationException if
        //    entry.FullName is not exactly one of BackupExporter.EntryNames. This is
        //    the "reject any entry whose name is not in the known allowlist" rule —
        //    three lines, but they have to run before step 2 touches any entry's
        //    content.
        //
        // 2. DECOMPRESSION CAP, same pass: sum entry.Length across all entries; throw
        //    BackupValidationException if the total exceeds MaxUncompressedBytes.
        //
        // 3. Read manifest.json's entry (it's tiny — fine to fully deserialize with
        //    JsonSerializer.Deserialize<BackupManifest>, unlike the three big files
        //    below) and check BackupFormatVersion. If it's greater than
        //    BackupExporter.CurrentFormatVersion, throw BackupValidationException with
        //    a message like "This backup was created by a newer version of the app."
        //    Do not attempt to import a format you don't recognize.
        //
        // 4. PRE-IMPORT SAFETY BACKUP, before touching the current database at all:
        //    await preImportBackupExporter.ExportAsync(preImportBackupDirectory, ...).
        //    This directory is app-private storage (FileSystem.AppDataDirectory in the
        //    caller), not the cache directory export uses — it must survive
        //    independently of whatever happens next. This is what "undo last import"
        //    (task 9.9) replays.
        //
        // 5. using var connection = connectionFactory.Create();
        //    using var tx = connection.BeginTransaction();
        //    DELETE FROM WorkoutSample; DELETE FROM Workout; DELETE FROM Device;
        //    — Replace mode, the whole scope of this phase. Everything from here to
        //    step 9 happens on this one transaction.
        //
        // 6. Stream-parse workouts.json:
        //    await foreach (var dto in JsonSerializer.DeserializeAsyncEnumerable
        //        <WorkoutExportDto>(entryStream, cancellationToken: ct))
        //    — NOT DeserializeAsync<List<WorkoutExportDto>>, which buffers the whole
        //    array. INSERT each row with a prepared SqliteCommand (reassigning
        //    parameters and calling ExecuteNonQuery per row — see 14-Database.md's
        //    "Sample write strategy" for why one prepared command beats one command
        //    per row). Capture the new integer Id SQLite assigns via
        //    `cmd.ExecuteScalar()` on a trailing `SELECT last_insert_rowid();`, and
        //    build a Dictionary<string, long> from WorkoutId -> new Id for step 7.
        //
        // 7. Stream-parse samples.json the same way. For each WorkoutSampleExportDto,
        //    look up its new WorkoutRowId from the dictionary built in step 6 (skip
        //    and log any sample whose WorkoutId isn't in the dictionary — a corrupt or
        //    hand-edited backup, not a crash) and INSERT. Call `progress?.Report(...)`
        //    every few hundred rows — this is the number the progress bar in task
        //    9.7's UI shows, and samples.json is the file large enough for it to
        //    matter.
        //
        // 8. Stream-parse devices.json; INSERT, letting SQLite assign new integer Ids.
        //
        // 9. tx.Commit(). The method must not return successfully until this line has
        //    run — the caller (task 9.7) applies settings.json's contents through
        //    AppSettingsService.RestoreFrom only after this method returns, never
        //    before or during.
        //
        // Return a BackupImportResult with the row counts and the settings.json
        // dictionary (parsed with a normal, non-streaming JsonSerializer.Deserialize —
        // it's small) for the caller to apply.
        throw new NotImplementedException();
    }
}
```

### 9.4 — `AutoBackupService` (the logic — you write this)

Creates: `src/MyHi.Companion.Core/Backup/AutoBackupService.cs`.

```csharp
namespace MyHi.Companion.Core.Backup;

public sealed class AutoBackupService(BackupExporter exporter, string backupDirectory)
{
    public const int MaxRetained = 5;

    public async Task RunAsync(
        string appVersion,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        CancellationToken ct = default)
    {
        // TODO:
        // 1. Directory.CreateDirectory(backupDirectory) — no-op if it already exists.
        // 2. await exporter.ExportAsync(backupDirectory, appVersion, settingsSnapshot, ct);
        // 3. Directory.GetFiles(backupDirectory, "MyHiBackup_*.zip"), sort ascending —
        //    the yyyy-MM-dd_HHmm timestamp embedded in the filename sorts correctly as
        //    a plain string, no need to parse it — and delete every file except the
        //    newest MaxRetained.
        throw new NotImplementedException();
    }
}
```

This runs from wherever Phase 04's workout engine raises its "workout finished" event —
by the time you reach this phase that event exists; wire a call to `RunAsync` into its
handler the same way `HomeViewModel` already wires `_captures.SessionChanged` in
Phase 00. The exact hookup point is a one-line call, not designed here, because it
depends on Phase 04's actual API.

### 9.5 — Tests: the export → wipe → import checksum test

Creates: `src/MyHi.Companion.Tests/Backup/BackupExportImportTests.cs`.

This is the test the phase's acceptance criteria are built around — "no data loss" has
to mean something more precise than eyeballing a row count.

Concrete steps:
1. Same setup pattern as `MigrationRunnerTests` (Phase 00) —
   `Directory.CreateTempSubdirectory`, a `SqliteConnectionFactory` per test, disposed
   in `Dispose()`. Apply the real migration set from Phase 06 (whatever
   `AppDatabase`/`MauiProgram.cs` passes to `MigrationRunner` by the time you reach
   this phase) so the test runs against the actual schema, not a hand-rolled one.
2. Seed 10–50 workouts with samples and a couple of devices directly via SQL (or via
   whatever repository Phase 06 built — either is fine, the point is realistic data,
   including at least one workout with `EndedAtUtc == null`, at least one with
   `AvgHeartRate == null`, and a sample with `Flags` bit 0 set for a connection gap).
3. `[Fact] Export_then_wipe_then_import_reproduces_the_original_data` — export to a
   temp directory, compute a canonical checksum of the seeded data (e.g. SHA-256 over
   every row serialized in a fixed field order — write a small helper for this, it's
   test infrastructure, not phase logic), wipe the three tables, import the exported
   ZIP, compute the same checksum again, assert equality. This is the literal
   "SHA-256 of a canonically-ordered dump matches" row in the table below.
4. `[Fact] Import_rejects_an_archive_entry_outside_the_allowlist` — hand-build a ZIP
   with an extra entry named `../evil.txt` (or any name not in
   `BackupExporter.EntryNames`), assert `ImportAsync` throws `BackupValidationException`
   and that none of `Workout`/`WorkoutSample`/`Device` were touched.
5. `[Fact] Import_rejects_a_newer_format_version` — hand-build a manifest.json with
   `BackupFormatVersion = BackupExporter.CurrentFormatVersion + 1`, assert the same.
6. `dotnet test src/MyHi.Companion.Tests` — all green, including every prior phase.

### 9.6 — Wiring `BackupImporter`'s dependencies in `MauiProgram.cs`

`BackupImporter`'s constructor takes a `BackupExporter` (for the pre-import safety
backup) and a directory string — both need real values only the app project can supply
(`FileSystem.AppDataDirectory`). Register with a factory lambda, the same pattern
`CaptureSessionManager` already uses in `MauiProgram.cs`:

```csharp
// ---- Backup ----
builder.Services.AddSingleton<BackupExporter>();
builder.Services.AddSingleton(sp => new BackupImporter(
    sp.GetRequiredService<SqliteConnectionFactory>(),
    sp.GetRequiredService<BackupExporter>(),
    Path.Combine(FileSystem.AppDataDirectory, "safety-backups")));
builder.Services.AddSingleton(sp => new AutoBackupService(
    sp.GetRequiredService<BackupExporter>(),
    Path.Combine(FileSystem.AppDataDirectory, "auto-backups")));
```

### 9.7 — `BackupViewModel` (the logic — you write this)

Creates: `src/MyHi.Companion/Features/Backup/BackupViewModel.cs`.

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyHi.Companion.Core.Backup;
using MyHi.Companion.Core.Settings;
using MyHi.Companion.Features.Shared;

namespace MyHi.Companion.Features.Backup;

public sealed partial class BackupViewModel : BaseViewModel
{
    private readonly BackupExporter _exporter;
    private readonly BackupImporter _importer;
    private readonly AppSettingsService _settings;
    private string? _lastPreImportBackupPath;

    // TODO: constructor also takes whatever Phase 04 exposes for "is a workout
    // active right now" — this walkthrough can't name its exact type since Phase 04
    // isn't built yet at the time this doc was written. Inject it, subscribe to
    // however it signals a change, and keep IsWorkoutActive below in sync with it.
    public BackupViewModel(BackupExporter exporter, BackupImporter importer, AppSettingsService settings)
    {
        _exporter = exporter;
        _importer = importer;
        _settings = settings;
    }

    [ObservableProperty]
    private bool isWorkoutActive;

    [ObservableProperty]
    private string? progressStage;

    [ObservableProperty]
    private bool canUndoImport;

    [RelayCommand]
    private async Task ExportAsync()
    {
        // TODO:
        // 1. Guard: if IsWorkoutActive, StatusMessage = "..."; return.
        // 2. IsBusy = true; ProgressStage = "Building backup…";
        // 3. var zipPath = await _exporter.ExportAsync(
        //        FileSystem.CacheDirectory, AppInfo.VersionString, _settings.Snapshot());
        // 4. await Share.Default.RequestAsync(new ShareFileRequest
        //        { Title = "MyHi Companion backup", File = new ShareFile(zipPath) });
        // 5. StatusMessage = "Backup shared."; IsBusy = false; ProgressStage = null;
        //    Wrap in try/catch — set StatusMessage to the exception's message on
        //    failure, same pattern as ScanViewModel.ConnectAsync (Phase 00).
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        // TODO:
        // 1. Guard: IsWorkoutActive or IsBusy -> StatusMessage, return.
        // 2. var pick = await FilePicker.Default.PickAsync(new PickOptions
        //        { PickerTitle = "Choose a MyHi Companion backup" });
        //    if (pick is null) return;
        // 3. Confirm before doing anything destructive — same DisplayAlert pattern as
        //    CaptureSessionsViewModel.DeleteAsync (Phase 00): grab
        //    Application.Current?.Windows.FirstOrDefault()?.Page, call
        //    page.DisplayAlertAsync("Replace all workout history?",
        //    "This replaces every workout on this phone with the contents of the
        //    backup. A safety copy of the current data is made first.", "Replace",
        //    "Cancel"). Return if not confirmed.
        // 4. IsBusy = true; var progress = new Progress<BackupImportProgress>(p =>
        //        ProgressStage = $"{p.Stage} ({p.FractionComplete:P0})");
        // 5. var result = await _importer.ImportAsync(pick.FullPath, progress);
        // 6. _settings.RestoreFrom(result.Settings); — only now, after ImportAsync has
        //    already returned (which only happens after its transaction committed).
        // 7. CanUndoImport = true; StatusMessage = $"Imported {result.WorkoutCount}
        //    workouts."; IsBusy = false; ProgressStage = null;
        //    Wrap in try/catch for BackupValidationException specifically — show its
        //    Message directly, it's already written to be user-facing (see task 9.3).
    }

    [RelayCommand(CanExecute = nameof(CanUndoImport))]
    private async Task UndoImportAsync()
    {
        // TODO: re-run _importer.ImportAsync against the pre-import safety backup
        // ImportAsync itself wrote during the last ImportAsync call (task 9.3 step 4).
        // You'll need ImportAsync or BackupImportResult to surface that path — add a
        // field to BackupImportResult for it, or have ImportAsync raise an event; your
        // call which, just make it deliberate.
    }
}
```

### 9.8 — Add `CommunityToolkit.Maui` (for the optional "Save to Device" button)

The package isn't referenced yet — check `src/MyHi.Companion/MyHi.Companion.csproj`,
it currently only has `CommunityToolkit.Mvvm`. Concrete steps:

1. Add the package:
   ```powershell
   dotnet add src/MyHi.Companion/MyHi.Companion.csproj package CommunityToolkit.Maui
   ```
2. In `MauiProgram.cs`, add `using CommunityToolkit.Maui;` and chain
   `.UseMauiCommunityToolkit()` onto the builder, next to `.UseMauiApp<App>()`:
   ```csharp
   builder
       .UseMauiApp<App>()
       .UseMauiCommunityToolkit()
       .ConfigureFonts(fonts => { ... });
   ```
3. Add a `SaveCopyAsync` command to `BackupViewModel` following the "Save to device"
   flow on the `FileSaver` doc page linked above — export the same way as 9.7 step 3,
   then `await FileSaver.Default.SaveAsync(fileName, stream, cancellationToken)`
   instead of `Share.Default.RequestAsync`. Wrap in try/catch: the doc page's known
   permission failures on API 33+ are exactly why this is the *secondary* button, not
   the primary one — a failure here should not read as "backup failed," just "couldn't
   save a copy," since the primary export already succeeded.

### 9.9 — UI: `BackupPage.xaml` (agent-authored)

Per the collaboration model in `../README.md`, this is UI — full XAML below, using
only tokens/styles from `docs/learning/04-Monochrome-Theme.md`.

Creates: `src/MyHi.Companion/Features/Backup/BackupPage.xaml` and `BackupPage.xaml.cs`.

**`BackupPage.xaml`:**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:backup="clr-namespace:MyHi.Companion.Features.Backup"
             x:Class="MyHi.Companion.Features.Backup.BackupPage"
             x:DataType="backup:BackupViewModel"
             Title="Backup">

    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="12">

            <Label Text="Export" Style="{StaticResource SubHeadline}" HorizontalOptions="Start" />
            <Label Text="Builds a single ZIP with your full workout history, saved devices, and settings."
                   Style="{StaticResource Caption}" />

            <Border Padding="14" IsVisible="{Binding IsWorkoutActive}">
                <Label Text="Export is disabled while a workout is active. Stop or finish the workout first."
                       Style="{StaticResource Caption}" />
            </Border>

            <Button Text="Export Backup"
                    Command="{Binding ExportCommand}"
                    IsEnabled="{Binding IsWorkoutActive, Converter={StaticResource InverseBoolConverter}}" />

            <Button Text="Save a Copy to Device"
                    Style="{StaticResource SecondaryButton}"
                    Command="{Binding SaveCopyCommand}"
                    IsEnabled="{Binding IsWorkoutActive, Converter={StaticResource InverseBoolConverter}}" />

            <BoxView HeightRequest="1" Margin="0,8" />

            <Label Text="Import" Style="{StaticResource SubHeadline}" HorizontalOptions="Start" />
            <Label Text="Replaces all workout history on this phone with the contents of a backup ZIP. A safety copy of the current data is made automatically first."
                   Style="{StaticResource Caption}" />

            <Button Text="Choose Backup File…"
                    Command="{Binding ImportCommand}"
                    IsEnabled="{Binding IsWorkoutActive, Converter={StaticResource InverseBoolConverter}}" />

            <Button Text="Undo Last Import"
                    Style="{StaticResource SecondaryButton}"
                    Command="{Binding UndoImportCommand}"
                    IsVisible="{Binding CanUndoImport}" />

            <VerticalStackLayout Spacing="8" IsVisible="{Binding IsBusy}">
                <ActivityIndicator IsRunning="{Binding IsBusy}" />
                <Label Text="{Binding ProgressStage}" Style="{StaticResource Caption}" HorizontalOptions="Center" />
            </VerticalStackLayout>

            <Border Padding="14" IsVisible="{Binding StatusMessage, Converter={StaticResource NotNullConverter}}">
                <Label Text="{Binding StatusMessage}" />
            </Border>

        </VerticalStackLayout>
    </ScrollView>

</ContentPage>
```

`InverseBoolConverter` and `NotNullConverter` are both already registered in `App.xaml`
(Phase 00) — no new converters needed.

**`BackupPage.xaml.cs`** — identical pattern to every other page:

```csharp
namespace MyHi.Companion.Features.Backup;

public partial class BackupPage : ContentPage
{
    public BackupPage(BackupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
```

### 9.10 — Wire it up

Touches: `MauiProgram.cs`, `AppShell.xaml.cs`, `Features/Shared/HomeViewModel.cs`.

Concrete steps:
1. In `MauiProgram.cs`, add:
   ```csharp
   builder.Services.AddTransient<BackupViewModel>();
   builder.Services.AddTransient<BackupPage>();
   ```
   next to the registrations from task 9.6.
2. In `AppShell.xaml.cs`, add `Routing.RegisterRoute("backup", typeof(BackupPage));`.
3. In `HomeViewModel.cs`, add a `NavDestination`:
   ```csharp
   new NavDestination("Backup", "Export workout history to a ZIP, or restore from one", "backup"),
   ```
4. Build and run: `dotnet build src/MyHi.Companion/MyHi.Companion.csproj -f net10.0-android`,
   then walk through the manual test table below on-device before calling the phase
   done.

---

## Tests

| Test | Expected |
|------|----------|
| Export with 50 workouts | ZIP created, opens, contains all five files |
| Export → wipe → import | Row counts match **and** SHA-256 of a canonically-ordered dump matches — see task 9.5 |
| Kill app mid-import | No data loss; pre-import backup recoverable |
| Import corrupt ZIP | Clear error, existing data untouched |
| Import archive with `../` entry | Rejected — task 9.5's zip-slip test |
| Import backup with newer format version | Clear "created by a newer version" message — task 9.5's version test |
| Export during active workout | Blocked with explanation |
| Auto-backup after 6 workouts | Exactly 5 files retained, oldest rotated out |

The first six rows are automated (`BackupExportImportTests`, task 9.5) except "Kill app
mid-import," which needs a real device — **`[HUMAN]`**: start an import with a large
backup, force-stop the app mid-transaction, relaunch, and confirm the database is
either fully the old data or fully the new data, never a mix (SQLite's transaction
guarantees this if task 9.3 is structured correctly — this test is there to catch a
mistake in the *code*, not in SQLite).

## Acceptance

- [ ] The export→wipe→import **checksum** test passes. "No data loss" is measured by
      checksum, not by eyeballing a list
- [ ] No test leaves the database in a partial state
- [ ] Zip-slip and format-version rejection tests pass
- [ ] Export is blocked while `IsWorkoutActive` is true; import is blocked the same way
- [ ] Zero warnings; all Phase 00–08 tests still pass
