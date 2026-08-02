# Phase 09 — Backup (minimal) (Flutter track)

> Deliberately scoped down — merge mode, CSV, and format migration are
> Phase 15. This phase exists to make data loss survivable, nothing more.

**Hardware:** none · **Size:** M · **Blocked by:** Phase 08

## Goal

Get workout history off this phone and onto another one, without ever
leaving the database in a state worse than before the attempt — identical
promise to the original track.

## The concept

The zip-slip guard, decompression cap, and format-version check are the same
fail-fast-at-the-trust-boundary idea Phase 01's parsers apply to BLE
packets, just at a different boundary: a ZIP file the user picked off their
filesystem, not bytes off a radio. Check everything before trusting any of
it. Merge mode, CSV, and format migrations are deferred for the same reason
as the original: each needs a real case to design against (a real second
format version, a real conflicting-record scenario) that doesn't exist yet.

## Scope

**In:** full export to a single ZIP · import **Replace mode only** ·
automatic local backup on workout finish (last 5 kept) · pre-import safety
backup.
**Out (Phase 15):** merge mode · CSV export · format migrations (the
mechanism ships here, migrations do not).

## Backup contents and a Dart-idiom adaptation

```
MyHiBackup_2026-07-28_1930.zip
├── manifest.json
├── workouts.jsonl
├── samples.jsonl
├── devices.json
└── settings.json
```

**One deliberate difference from the original track: `samples.jsonl`, not
`samples.json`.** The original streams a JSON *array* one element at a time
via `JsonSerializer.DeserializeAsyncEnumerable`. Dart's `dart:convert` has
no equivalent streaming array parser built in, and reaching for a
third-party one is more dependency than this needs. **JSONL (one JSON object
per line)** sidesteps the problem entirely: read the file as a line stream
(`file.openRead().transform(utf8.decoder).transform(const LineSplitter())`),
`jsonDecode()` each line independently. This project already uses JSONL for
capture files (Phase 00) for the same reason — line-at-a-time is Dart's
natural streaming shape for JSON, so `samples.jsonl` reuses a format the
codebase already trusts rather than importing new machinery. `workouts.jsonl`
gets the same treatment for consistency, even though it's small enough that
streaming barely matters there — one convention, not two.

`manifest.json`/`devices.json`/`settings.json` stay plain JSON: small enough
to fully deserialize in one call.

`backupFormatVersion` is an integer independent of the app version, same
rule as the original. Identities in the file are `workoutId`/`deviceUid`;
integer `id` values are never exported.

## Technology: the `archive` package

`archive` (pub.dev) is this track's `System.IO.Compression.ZipFile`
equivalent — `ZipEncoder`/`ZipDecoder`, working over `dart:io` file streams.
No real alternative was seriously considered: it's the standard, actively
maintained option, and this project's ZIP needs (a handful of named entries,
no encryption, no split archives) are exactly its core use case.

## Safety requirements — the point of the phase

Identical to the original track, because none of them are framework-specific:

1. **Pre-import safety backup first**, to app-private storage
   (`getApplicationSupportDirectory()` from `path_provider`, not the cache
   directory export uses).
2. **The entire import inside one `sqflite` transaction** (`db.transaction(...)`).
3. **Settings applied only after the transaction commits.** `shared_preferences`
   has no transaction of its own; losing six toggles is recoverable, losing
   years of workouts is not.
4. **Atomic export**: write to `.tmp`, then rename — an interrupted export
   must never leave a truncated file under the real name.
5. **Zip-slip guard**: reject any archive entry whose name isn't in a fixed
   allowlist, before extracting anything.
6. **Decompression cap**: reject archives whose uncompressed size exceeds
   ~500 MB.
7. **Block export during an active workout; block import while connected or
   mid-workout.**

## `BackupExporter`

```dart
// myhi_companion_core/lib/backup/backup_exporter.dart
class BackupExporter {
  final Database _db;
  static const currentFormatVersion = 1;
  static const entryNames = ['manifest.json', 'workouts.jsonl', 'samples.jsonl', 'devices.json', 'settings.json'];

  BackupExporter(this._db);

  Future<String> export(String workingDirectory, String appVersion, Map<String, String> settingsSnapshot) async {
    // TODO, in order:
    // 1. Query Workout LEFT JOIN Device, map each row to a workout-export
    //    map keyed by workoutId (the GUID), never the integer id.
    // 2. Write workouts.jsonl: one jsonEncode(...) call per row, each on its
    //    own line, via an IOSink from File(...).openWrite().
    // 3. Same shape for samples.jsonl, joining WorkoutSample back to its
    //    workoutId.
    // 4. Write devices.json and settings.json as plain JSON (small enough).
    // 5. Write manifest.json last, with the row counts from steps 1-4.
    // 6. Build the archive: read each written file into an ArchiveFile,
    //    add to an Archive, encode with ZipEncoder(), write to
    //    '$workingDirectory/$fileName.tmp', then File(...).rename() to the
    //    real name — the atomic-export requirement.
    // 7. Delete the loose files, return the final path.
    throw UnimplementedError();
  }
}
```

## `BackupImporter`

```dart
// myhi_companion_core/lib/backup/backup_importer.dart
class BackupValidationException implements Exception {
  final String message;
  BackupValidationException(this.message);
}

class BackupImporter {
  final Database _db;
  final BackupExporter _preImportBackupExporter;
  final String _preImportBackupDirectory;
  static const _maxUncompressedBytes = 500 * 1024 * 1024;

  BackupImporter(this._db, this._preImportBackupExporter, this._preImportBackupDirectory);

  Future<BackupImportResult> import(String zipPath, {void Function(String stage, double fraction)? onProgress}) async {
    // TODO, in order — each guards the next:
    // 1. Decode the archive (ZipDecoder().decodeBytes(...)). ZIP-SLIP GUARD:
    //    reject if any entry.name isn't exactly one of BackupExporter.entryNames.
    // 2. DECOMPRESSION CAP: sum entry.size across all entries; reject over
    //    _maxUncompressedBytes.
    // 3. Read manifest.json's entry, check backupFormatVersion. Reject if
    //    greater than BackupExporter.currentFormatVersion.
    // 4. PRE-IMPORT SAFETY BACKUP, before touching the current database:
    //    await _preImportBackupExporter.export(_preImportBackupDirectory, ...).
    // 5. await _db.transaction((txn) async { ... }) wrapping steps 6-9.
    //    Inside: DELETE FROM WorkoutSample; DELETE FROM Workout; DELETE FROM Device;
    // 6. Parse workouts.jsonl line by line (LineSplitter over the decoded
    //    bytes), jsonDecode each line, INSERT via txn.insert, building a
    //    Map<String, int> from workoutId -> new rowid as you go.
    // 7. Parse samples.jsonl the same way, looking up workoutRowId from the
    //    map built in step 6. Report progress every few hundred rows.
    // 8. Parse devices.json (small, non-streaming), INSERT.
    // 9. Transaction commits when the callback returns normally — sqflite
    //    handles this; there is no separate explicit .commit() call to
    //    forget, unlike raw Microsoft.Data.Sqlite's BeginTransaction/Commit
    //    pair.
    // Return the row counts and the settings.json map for the caller to
    // apply through AppSettingsService AFTER this method returns.
    throw UnimplementedError();
  }
}
```

`db.transaction((txn) async {...})` is a real structural difference from the
original worth naming: `sqflite` commits automatically when the callback
completes and rolls back automatically if it throws, so there's no separate
`tx.Commit()` call to remember — the transaction boundary is the callback's
extent, not an explicit method call.

## Transfer mechanism

- **Export**: build the ZIP in a temp/cache directory (`path_provider`'s
  `getTemporaryDirectory()`), then `Share.shareXFiles([XFile(zipPath)])` via
  the `share_plus` package — this track's `Share.Default.RequestAsync`.
- **Import**: `file_picker`'s `FilePicker.platform.pickFiles()`. Accept
  broadly (MIME filtering for ZIP is as inconsistent on Android as the
  original track found) and validate by reading the archive header, not the
  extension.

## Tests

Same shape as the original: export→wipe→import reproduces the original data
(compare a canonical serialization of every row, not just row counts);
import rejects an archive entry outside the allowlist; import rejects a
newer format version; `[HUMAN]` kill the app mid-import and confirm the
database is fully old or fully new, never a mix.

## Acceptance

- [ ] The export→wipe→import equality test passes — checksum-equivalent, not eyeballed
- [ ] No test leaves the database in a partial state
- [ ] Zip-slip and format-version rejection tests pass
- [ ] Export blocked during an active workout; import blocked the same way
