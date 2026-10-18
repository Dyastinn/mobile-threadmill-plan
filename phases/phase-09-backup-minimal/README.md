# Phase 09 — Backup (minimal)

> Deliberately scoped down. Merge mode, CSV, and version migration are Phase 15. This
> phase exists to make data loss survivable, nothing more.

**Hardware:** none · **Size:** M · **Blocked by:** Phase 08

---

## Goal

Get workout history off this phone and onto another one, without ever leaving the
database in a state worse than before the attempt.

### Understanding what you're building (read this before the tasks)

**The everyday problem.** This phase makes one promise, the same one you'd make to
yourself packing boxes for a move: nothing that goes in disappears before it comes
out the other end. Get every workout off this device and onto another one, intact,
even if the process is interrupted halfway through. The line at the top of this
phase's doc says exactly that: "without ever leaving the database in a state worse
than before the attempt." Everything else (merging two phones' histories, a CSV
export for spreadsheets, an older backup reading correctly in a newer app version)
is explicitly Phase 15's problem, not this one's.

**Why "minimal" isn't corner-cutting.** The tempting alternative, once you're deep
in the exporter and importer with the ZIP format and database queries fresh in your
head, is to build the complete version now: add merge mode, add CSV, add version
migration while you're already in the file. Each of the three is deferred for a
specific, demonstrable reason, not laziness. **Merge mode** needs conflict-resolution
rules for what happens when the same `WorkoutId` appears on both phones with a
different `Notes` field or a different `EndedAtUtc`. Nobody can write those rules
correctly today, because there's no real case yet to write them against; guessing now
means designing against an imagined scenario instead of an observed one. **CSV** is a
second output format with its own schema-mapping problem, and nothing in this phase's
scope actually needs it. The single ZIP already satisfies "get the data off the
phone." **Version migration** is, almost by definition, something you cannot build
correctly before a second format version exists to migrate *from*.
`backupFormatVersion` is in the manifest today specifically so a real migration can be
written later against a real old format, not an invented one now. So the "Out" column
in the Scope section isn't three shortcuts. It's three things that are genuinely
impossible to build well yet, deferred to the point where they become buildable.
What's left in scope (full export, replace-only import, automatic local backup,
pre-import safety backup) is the complete list of what's needed to make "you can
always get your data back" true today, and no smaller.

**The pattern, named plainly.** The entry allowlist, the decompression cap, and the
format-version check in the importer (task 9.3's validation pass) are the same
**fail-fast-at-the-trust-boundary** idea Phase 01a applied to BLE packets: check
everything *before* trusting any of it, reject loudly and immediately, never partway
through real work. The boundary just moved. There, the untrusted input was bytes off a
Bluetooth radio; here, it's a ZIP file that might be corrupted, hand-edited, or
actively hostile. The cost is the same shape too: a handful of checks before any real
import work starts. The payoff is what makes it worth it here specifically. Without the
pre-import safety backup and the single SQLite transaction (task 9.3), an import that
fails at row 40,000 of the samples leaves the user with neither their old data nor
their new data. For a feature whose entire purpose is "never lose the user's workout
history," a validation gap here would defeat the phase's own point. This codebase
doesn't apply fail-fast reflexively everywhere; it applies it precisely at the
boundary where control passes from something you don't trust (a file the user picked
off their filesystem) to something you do (your own database).

One honest note on **zip-slip**, the classic ZIP attack where an entry named
`../../something` writes a file outside the target folder. It only bites code that
*extracts entries to disk* using their names. This importer never does: it streams
each entry's bytes straight into SQLite. So that bug class can't happen here by
construction. The allowlist check still earns its three lines: it rejects an archive
that isn't one of ours (or has been tampered with) before any work starts, and it
keeps the importer from silently ignoring content it doesn't understand.

## Learning goals

- **Splitting a feature across the `core/`/`features/` seam a third time.** The ZIP
  building, the SQL, the JSON streaming, and every safety check (allowlist,
  decompression cap, version check, single-transaction replace) are pure logic with
  zero React Native dependency. They belong in `core/`, get real Jest tests against
  `nodeSqliteDb`, and are built the same "spec, not code" way as
  `FakeTreadmillService` (Phase 01b) and `AppSettings` (Phase 08). Only the
  *transfer* (files, the share sheet, the file picker, the confirmation dialog)
  touches an Expo API and lives in `features/`.
- **Streaming**, concretely: reading a 30 MB file 64 KB at a time, decompressing as
  you go, splitting the result into lines, and inserting each line, so memory use
  stays flat no matter how big the backup is. And why the samples go in a JSON *Lines*
  file (one JSON object per line), which can be read line by line, rather than one big
  JSON array, which can't be parsed until the whole thing is in memory.
- **Small interfaces for I/O** (`ByteSink`, `ByteSource`): the same seam idea, used so
  `core/` can produce and consume file contents without importing a file-system API.
- **Why settings are applied *after* the database transaction commits, never
  during it.** The kv-store has no transaction of its own. See "Safety requirements"
  below for the exact reasoning; it is not optional.

## Scope

**In:** full export to a single ZIP · import **Replace mode only** · automatic local
backup on workout finish (last 5 kept, app-private storage) · pre-import safety backup.

**Out (Phase 15):** merge mode · CSV export · backup format *migrations* (the
mechanism ships here; migrations do not) · statistics/PR export. Those are **derived
from workouts and must be recomputed on import**, never exported. Exporting derived
data creates two sources of truth.

## Backup contents

```
MyHiBackup_2026-07-28_1930.zip
├── manifest.json     (backupFormatVersion, app version, export date, counts)
├── devices.json      (array)
├── workouts.json     (array: headers, keyed by WorkoutId GUID)
├── samples.jsonl     (JSON Lines: one telemetry row per line, keyed by WorkoutId GUID)
└── settings.json     (object)
```

Entries are written in exactly that order. The importer relies on it: devices before
workouts (a workout references a device), workouts before samples (a sample needs its
workout's new row id).

One manifest, not a `metadata.json` + `version.json` pair. Two files with overlapping
responsibility drift apart.

`backupFormatVersion` is an **integer independent of the app version**. A UI bugfix
bumping the app version must not invalidate backups.

Identities in the file are `WorkoutId` and `DeviceUid`. **Integer `Id` values are not
exported** and are reassigned on import. See `../phase-06-recording-schema/README.md`'s
"Backup mapping" section, which this phase implements field-for-field (task 9.1 below
mirrors its `Workout`/`WorkoutSample`/`Device` columns exactly).

---

## Transfer mechanism

- **Export: share sheet.** Build the ZIP in the app's cache directory, then
  `Sharing.shareAsync(file.uri, { mimeType: 'application/zip' })` (`expo-sharing`).
  Zero storage permissions, one tap to Drive/Gmail/Nearby Share, sidesteps scoped
  storage entirely.
- **Secondary: "Save a copy to device"** via `Directory.pickDirectoryAsync()`
  (`expo-file-system`), which opens Android's own folder picker (the Storage Access
  Framework) and returns a directory the app may write into. Secondary, because a
  folder on a cloud provider can return a location that behaves oddly; the share sheet
  is the path that always works.
- **Import:** `File.pickFileAsync()` (`expo-file-system`). **MIME filtering for ZIP is
  inconsistent** across Android file providers (`application/zip`,
  `application/octet-stream`, `*/*`). Accept broadly and validate by reading the
  archive's first four bytes (`50 4B 03 04`, "PK"), not the extension.
- **Do not** hardcode a path like `Documents/MyHi Companion/Backup/`. Since Android 10
  scoped storage an app cannot freely create and write such a path.

---

## Technology decision: `fflate` for ZIP

**What problem does it solve?** The backup is a ZIP that can reach tens of megabytes,
and both directions must stream: building it without holding every sample in memory,
and reading it back without decompressing the whole thing first.

**Why are we using it?** Pure JavaScript (no native module, nothing to rebuild, runs
identically on the phone and in Jest), small, MIT, and it has true streaming classes
in both directions: `Zip` + `ZipDeflate` to produce an archive chunk by chunk, and
`Unzip` + `UnzipInflate` to consume one chunk by chunk, handing you each entry's name
before its data so the allowlist check happens before any content is read. Because
it's plain JavaScript, it's allowed in `core/`.

**Alternatives considered:**

1. **`react-native-zip-archive`.** Pros: native, fast, simple API (`zip(folder)`,
   `unzip(file, folder)`). Cons: it works folder-to-file and file-to-folder, so import
   means extracting to disk first (bringing zip-slip back as a real concern) and
   export means writing every JSON file to disk first; a native module to keep
   compatible; can't run in Jest. Learning curve: low. The better choice for apps
   that genuinely want files on disk, like downloading and unpacking assets.
2. **JSZip.** Pros: the best-known JavaScript ZIP library, friendly API. Cons: it
   loads the whole archive into memory to read it, which is exactly what "streaming
   import" forbids at tens of megabytes. Learning curve: low. The better choice for
   small archives.
3. **No ZIP: share a copy of the SQLite file itself** (`expo-sqlite` can back up a
   database). Pros: trivially simple, perfectly faithful. Cons: ties the backup format
   to the schema version (an older backup can't be read by a newer schema without
   running migrations on the user's file), exports integer ids that mean nothing on
   another phone, and can't be partially validated or merged (Phase 15). Learning
   curve: lowest. The better choice for a pure same-device "undo" snapshot, which is
   actually a reasonable shape for the pre-import safety backup if this phase's
   exporter ever proves too slow for it.

**Why not the alternatives?** `react-native-zip-archive` forces a disk round trip and
can't be tested in Node. JSZip can't stream reads. A raw database copy gives up the
portable, versioned format that Phase 15's merge depends on.

**Long-term considerations.** No native code means no SDK-upgrade risk at all. The
streaming API is lower-level than JSZip's, which is real code to write once (and
test once). Replacing it would touch two files in `src/core/backup/`.

**Practical example:** `src/core/backup/backupExporter.ts` and `backupImporter.ts`,
this phase.

---

## Safety requirements — these are the point of the phase

**Replace must not be destructive on failure.** "Delete current data, then restore":
if that throws at file 3 of 5 the user has neither. Required:

1. Validate the whole archive **before** touching the database (task 9.3's first pass)
2. Write an automatic pre-import backup to app-private storage
3. Perform the entire data import inside **one SQLite transaction**
4. Apply settings **after** the transaction commits. The kv-store sits outside the
   transaction, and losing six toggles is recoverable while losing years of workouts
   is not
5. Offer "undo last import" for one session

Also:

- **Atomic export:** write to `.tmp`, then rename. An interrupted export must not leave
  a truncated file with a valid-looking name.
- **Entry allowlist:** reject any entry whose name is not one of the five above.
- **Decompression cap:** reject archives whose decompressed size exceeds ~500 MB,
  counted as bytes actually come out of the decompressor (an entry's declared size can
  lie).
- **Streaming import.** With telemetry included, backups reach tens of MB. Never hold
  the whole decompressed archive in memory.
- **Block export during an active workout**; **block import while connected or
  mid-workout.**
- **Timestamps are UTC plus stored offset**, serialised as ISO 8601.
- **Restored saved devices will not auto-connect** on a new phone: the MAC restores
  but the phone still has to find the treadmill again. Tell the user they need to
  reconnect once, rather than letting it look broken.

---

## Reference docs

- **`fflate`**: https://github.com/101arrowz/fflate. Read the "Streaming" and
  "ZIP files" sections of the README, specifically `Zip`/`ZipDeflate` (task 9.2) and
  `Unzip`/`UnzipInflate` with `onfile` (task 9.3).
- **`TextDecoder`**: https://developer.mozilla.org/en-US/docs/Web/API/TextDecoder.
  `decoder.decode(chunk, { stream: true })` is how bytes become text without breaking
  a multi-byte character that happens to straddle two chunks.
- **`expo-file-system`**: https://docs.expo.dev/versions/latest/sdk/filesystem/.
  `File`, `Directory`, `Paths.cache`, `Paths.document`, `file.open()` and the
  `FileHandle` (`readBytes`, `writeBytes`), `File.pickFileAsync`,
  `Directory.pickDirectoryAsync`, `move`.
- **`expo-sharing`**: https://docs.expo.dev/versions/latest/sdk/sharing/. Phase 00's
  capture screen already calls it for capture files; task 9.7 does the same thing
  with the backup ZIP.
- **`node:sqlite`** and **`node:crypto`**: only in tests (task 9.5), for a real
  database and a SHA-256 checksum. Neither is ever imported by app code.

---

## Walkthrough

### 9.1 — Export types and the I/O seams (data shapes — write these directly)

Creates: `src/core/backup/backupTypes.ts`.

These are plain data shapes, not logic, safe to write in full rather than from a
skeleton. Every field maps 1:1 to a column in `../phase-06-recording-schema/README.md`,
with the portable GUID identities swapped in for the integer
`Id`/`WorkoutRowId`/`DeviceId` columns that never leave the phone:

```ts
export const BACKUP_FORMAT_VERSION = 1;

export const ENTRY_NAMES = [
  'manifest.json',
  'devices.json',
  'workouts.json',
  'samples.jsonl',
  'settings.json',
] as const;
export type EntryName = (typeof ENTRY_NAMES)[number];

export interface BackupManifest {
  readonly backupFormatVersion: number;
  readonly appVersion: string;
  readonly exportedAtUtc: string;
  readonly workoutCount: number;
  readonly sampleCount: number;
  readonly deviceCount: number;
}

export interface WorkoutExport {
  readonly workoutId: string;          // GUID; Workout.Id (integer) is never exported
  readonly startedAtUtc: string;
  readonly endedAtUtc: string | null;
  readonly startOffsetMinutes: number;
  readonly durationSeconds: number;
  readonly distanceMeters: number;
  readonly calories: number;
  readonly avgSpeedKph: number;
  readonly maxSpeedKph: number;
  readonly avgHeartRate: number | null;
  readonly maxHeartRate: number | null;
  readonly status: number;
  readonly deviceUid: string | null;   // Device.DeviceUid, not Workout.DeviceId
  readonly notes: string | null;
}

export interface WorkoutSampleExport {
  readonly workoutId: string;          // joins back to WorkoutExport.workoutId,
                                       // not the integer WorkoutRowId
  readonly elapsedSec: number;
  readonly speedKph: number | null;
  readonly distanceM: number | null;
  readonly calories: number | null;
  readonly heartRate: number | null;
  readonly flags: number;
}

export interface DeviceExport {
  readonly deviceUid: string;
  readonly macAddress: string;
  readonly name: string;
  readonly lastSeenUtc: string | null;
  readonly isPreferred: boolean;
}

export type SettingsExport = Readonly<Record<string, string>>;

/** Where backup bytes go. In the app: a FileHandle. In tests: an array. */
export interface ByteSink {
  write(chunk: Uint8Array): Promise<void>;
}

/** Where backup bytes come from. read() returns an empty array at the end. */
export interface ByteSource {
  read(maxBytes: number): Promise<Uint8Array>;
}

/** A reason-carrying error the UI can show as-is. */
export class BackupValidationError extends Error {}
```

`settings.json` is just the key/value pairs `AppSettings` (Phase 08) already stores.
Add these two methods to `AppSettings` now (small enough to write directly; it's
straight delegation to the store you already wrote):

```ts
// In AppSettings (src/core/settings/appSettings.ts)
toExport(): SettingsExport {
  // TODO: { [key]: storedString } for every key in KEYS that has a stored value
}

restoreFrom(exported: SettingsExport): void {
  // TODO: for each known key present, write it back through store.setString, then
  // refresh(). Unknown keys are ignored, not an error: a newer backup may carry a
  // setting this version doesn't have yet. Values go through the normal read path
  // afterwards, so a garbage value still falls back to its default (Phase 08).
}
```

### 9.2 — `BackupExporter` (the logic — you write this)

Creates: `src/core/backup/backupExporter.ts`.

```ts
import { Zip, ZipDeflate } from 'fflate';

export interface ExportInput {
  readonly appVersion: string;
  readonly settings: SettingsExport;
  readonly now: Date;
}

export class BackupExporter {
  constructor(private readonly db: Db) {}

  /**
   * Streams a complete backup ZIP into `sink`. Knows nothing about files: the caller
   * (task 9.6) decides where the bytes land, and does the .tmp + rename.
   */
  async export(sink: ByteSink, input: ExportInput): Promise<BackupManifest> {
    // TODO, in order:
    // 1. Counts first (three SELECT COUNT(*)), so the manifest can be written first.
    //
    // 2. Collect output chunks as fflate produces them:
    //      const pending: Uint8Array[] = [];
    //      const zip = new Zip((err, chunk) => { if (err) throw err; pending.push(chunk); });
    //    and a small `drain()` that awaits sink.write(...) for everything in pending
    //    and empties it. fflate calls ondata synchronously; drain() after each step
    //    is how output reaches the sink without piling up in memory.
    //
    // 3. For each entry, in ENTRY_NAMES order:
    //      const entry = new ZipDeflate(name, { level: 6 }); zip.add(entry);
    //      entry.push(bytes, isLast)  // bytes = new TextEncoder().encode(text)
    //    manifest.json, devices.json, workouts.json, settings.json: small, build the
    //    whole JSON text and push it once.
    //
    // 4. samples.jsonl: page through
    //      SELECT w.WorkoutId AS workoutId, s.ElapsedSec AS elapsedSec, ...
    //      FROM WorkoutSample s JOIN Workout w ON w.Id = s.WorkoutRowId
    //      ORDER BY s.WorkoutRowId, s.ElapsedSec LIMIT 2000 OFFSET $offset
    //    and for each page push one chunk of `JSON.stringify(row) + '\n'` lines, then
    //    drain(). The JOIN is what translates WorkoutRowId -> the GUID. The last page
    //    is pushed with final = true (an empty final push is fine).
    //    Devices: SELECT ... FROM Device; workouts: Workout LEFT JOIN Device (LEFT,
    //    because DeviceId can be NULL), selecting Device.DeviceUid, not DeviceId.
    //
    // 5. zip.end(); drain(); return the manifest.
  }
}
```

### 9.3 — `BackupImporter` (the logic — you write this)

Creates: `src/core/backup/backupImporter.ts`.

This is the phase's highest-stakes file. Every step below is one of the "Safety
requirements" above turned into code, not extra caution. It reads the archive
**twice**: once to validate everything without touching the database, once to import.
Reading a 30 MB file twice costs a second or two; trusting a file you haven't fully
checked costs the user's history.

```ts
import { Unzip, UnzipInflate } from 'fflate';

export interface ImportResult {
  readonly workoutCount: number;
  readonly sampleCount: number;
  readonly deviceCount: number;
  readonly settings: SettingsExport;
}

export interface ImportProgress {
  readonly stage: string;
  readonly fractionComplete: number;
}

const MAX_DECOMPRESSED_BYTES = 500 * 1024 * 1024;
const CHUNK = 64 * 1024;

export class BackupImporter {
  constructor(private readonly db: Db) {}

  /**
   * Pass 1. Reads the whole archive, writes nothing. Throws BackupValidationError on
   * the first problem. Returns the manifest.
   */
  async validate(open: () => Promise<ByteSource>): Promise<BackupManifest> {
    // TODO, in order. Do not reorder: each one guards the next.
    // 1. HEADER: the first 4 bytes are 50 4B 03 04, or it isn't a ZIP at all.
    // 2. ALLOWLIST: in unzip.onfile, before calling file.start(), reject any
    //    file.name that isn't in ENTRY_NAMES. Also reject a duplicate name.
    // 3. DECOMPRESSION CAP: in each file's ondata, add chunk.length to a running
    //    total; over MAX_DECOMPRESSED_BYTES -> reject.
    // 4. MANIFEST: parse manifest.json; backupFormatVersion > BACKUP_FORMAT_VERSION ->
    //    reject with "This backup was created by a newer version of the app."
    // 5. CONTENT: every line of samples.jsonl and every element of the other files
    //    parses as JSON with the expected fields; counts match the manifest. Anything
    //    else -> reject, naming the entry and line number.
    // Reading pattern: loop { chunk = await source.read(CHUNK); unzip.push(chunk,
    // chunk.length === 0); if (chunk.length === 0) break; } with
    // unzip.register(UnzipInflate) once up front. onfile/ondata run synchronously
    // inside push(), so record errors in a variable and throw after push returns.
  }

  /**
   * Pass 2. Only after validate() succeeded AND the caller wrote the pre-import
   * safety backup. Replaces everything inside one transaction.
   */
  async importReplace(
    open: () => Promise<ByteSource>,
    onProgress?: (p: ImportProgress) => void,
  ): Promise<ImportResult> {
    // TODO:
    // 1. return this.db.transaction(async (tx) => { ... }): everything below happens
    //    on tx, and the whole thing rolls back if anything throws.
    // 2. DELETE FROM WorkoutSample; DELETE FROM Workout; DELETE FROM Device;
    //    (Replace mode, the whole scope of this phase.)
    // 3. Stream the archive again. Entries arrive in ENTRY_NAMES order (task 9.2):
    //    - devices.json: INSERT each; build Map<deviceUid, newId>.
    //    - workouts.json: INSERT each with a prepared statement, looking up DeviceId
    //      from the map; build Map<workoutId, newRowId> from each run's
    //      lastInsertRowId.
    //    - samples.jsonl: split decoded text into lines (keep the trailing partial
    //      line for the next chunk), plain INSERT (not OR REPLACE: a duplicate in a
    //      backup is corruption, and should fail loudly) through ONE prepared statement,
    //      looking up WorkoutRowId from the map. A sample whose workoutId isn't in
    //      the map is skipped and counted (validate() should make this impossible;
    //      count it anyway). Call onProgress every few hundred rows.
    //    - settings.json: parse and keep for the result. Do NOT apply it here.
    // 4. Because inserting is async but onfile/ondata are synchronous, collect each
    //    chunk's complete lines into an array during push(), then await the inserts
    //    for that array before reading the next chunk. That keeps memory bounded to
    //    one chunk's worth of lines.
    // 5. Return the counts and the settings. The caller applies settings only after
    //    this promise resolves, which only happens after COMMIT.
  }
}
```

### 9.4 — `AutoBackup` (the logic — you write this)

Creates: `src/features/backup/autoBackup.ts`.

This one manages files (create a directory, list it, delete old ones), so it lives in
`features/`:

```ts
export const MAX_RETAINED = 5;

export async function runAutoBackup(exporter: BackupExporter, settings: AppSettings): Promise<void> {
  // TODO:
  // 1. const dir = new Directory(Paths.document, 'auto-backups'); create it if missing.
  // 2. Export through exportToFile (task 9.6), into dir.
  // 3. List dir's MyHiBackup_*.zip files, sort ascending by name (the
  //    yyyy-MM-dd_HHmm timestamp in the name sorts correctly as a plain string, no
  //    need to parse it), and delete every file except the newest MAX_RETAINED.
}
```

Call it from `WorkoutSession` (Phase 07) when the engine reaches `'finished'`, after
the recorder's final flush. A failed auto-backup is logged, never shown as a workout
failure.

### 9.5 — Tests: the export → wipe → import checksum test

Creates: `src/core/backup/backup.test.ts`, plus `src/core/backup/testing/memoryBytes.ts`
(an in-memory `ByteSink` that collects chunks and a `ByteSource` that replays them;
test infrastructure, about twenty lines).

This is the test the phase's acceptance criteria are built around. "No data loss" has
to mean something more precise than eyeballing a row count.

Concrete steps:
1. Same setup pattern as `migrationRunner.test.ts` (Phase 00): a fresh `nodeSqliteDb`
   per test, PRAGMAs on, the real `migrations` from Phase 06 applied, so the test runs
   against the actual schema, not a hand-rolled one.
2. Seed 10–50 workouts with samples and a couple of devices directly via SQL (or via
   Phase 06's `WorkoutRepository`; either is fine, the point is realistic data),
   including at least one workout with `EndedAtUtc` null, one with `AvgHeartRate`
   null, one with a negative `StartOffsetMinutes`, and a sample with `Flags` bit 0 set.
3. `test('export, wipe, import reproduces the original data', ...)`: compute a
   canonical checksum of the seeded data (SHA-256 from `node:crypto` over every row
   serialised in a fixed column order, ordered by the GUIDs, never by integer ids,
   since those change on import; write a small helper for this, it's test
   infrastructure, not phase logic), export to a memory sink, wipe the three tables,
   validate and import from the same bytes, compute the checksum again, expect
   equality. This is the literal "SHA-256 of a canonically-ordered dump matches" row
   in the table below.
4. `test('rejects an entry outside the allowlist', ...)`: build a ZIP with fflate's
   `zipSync({ 'manifest.json': ..., '../evil.txt': ... })`, expect `validate` to throw
   `BackupValidationError`, and the three tables untouched (row counts unchanged).
5. `test('rejects a newer format version', ...)`: a manifest with
   `backupFormatVersion: BACKUP_FORMAT_VERSION + 1`, same expectation.
6. `test('a failure mid-import rolls everything back', ...)`: call `importReplace`
   directly (skipping `validate`) on an archive whose `samples.jsonl` contains the
   same `(workoutId, elapsedSec)` twice, near the end. The import uses plain `INSERT`
   (after the `DELETE`s there's nothing to replace, so `OR REPLACE` would only hide a
   corrupt file), so the duplicate hits the primary key and throws. Expect the throw,
   and the database to equal the pre-import checksum: the earlier `DELETE`s and
   thousands of inserts all rolled back.
7. `npm test`: all green, including every prior phase.

### 9.6 — File plumbing: `exportToFile`, atomic rename, file sources

Creates: `src/features/backup/backupFiles.ts` (logic, yours).

```ts
/** Exports into `dir` atomically: writes NAME.zip.tmp, then renames to NAME.zip. */
export async function exportToFile(exporter: BackupExporter, dir: Directory, input: ExportInput): Promise<File> {
  // TODO:
  // 1. name = `MyHiBackup_${yyyy-MM-dd_HHmm}.zip` from input.now (local time)
  // 2. const tmp = new File(dir, `${name}.tmp`); tmp.create(); const handle = tmp.open();
  // 3. sink = { write: (chunk) => handle.writeBytes(chunk) }
  // 4. await exporter.export(sink, input); handle.close() (in a finally)
  // 5. rename tmp to the final name (File.move to the destination File), replacing
  //    an existing file of that name. A crash before this line leaves only a .tmp,
  //    never a truncated file under the real name.
}

/** A ByteSource over a file, for the importer. */
export function fileSource(file: File): () => Promise<ByteSource> {
  // TODO: each call opens a fresh handle (the importer reads twice) and returns
  // { read: (n) => handle.readBytes(n) }, closing the handle when read returns empty.
}
```

### 9.7 — `useBackup` (the logic — you write this)

Creates: `src/features/backup/useBackup.ts`.

```ts
export interface BackupState {
  readonly isWorkoutActive: boolean;
  readonly busy: boolean;
  readonly progressStage: string | null;
  readonly statusMessage: string | null;
  readonly canUndoImport: boolean;
}

export function useBackup(): BackupState & {
  exportAndShare(): Promise<void>;
  saveCopyToDevice(): Promise<void>;
  importFromFile(): Promise<void>;
  undoLastImport(): Promise<void>;
} {
  // TODO: read exporter, importer, settings, workout (engine) and treadmill from
  // useServices(); isWorkoutActive from Phase 04's useWorkoutState.
  //
  // exportAndShare:
  //   guard isWorkoutActive -> statusMessage, return
  //   busy = true; progressStage = 'Building backup...'
  //   const file = await exportToFile(exporter, new Directory(Paths.cache), {...})
  //   await Sharing.shareAsync(file.uri, { mimeType: 'application/zip' })
  //   statusMessage = 'Backup shared.'; in a finally: busy = false, progressStage = null
  //   catch: statusMessage = the error's message
  //
  // saveCopyToDevice: same export, then Directory.pickDirectoryAsync() and copy the
  //   file into the picked directory (see the expo-file-system docs for whether
  //   file.copy accepts it directly, or whether you create a file in it and stream
  //   the bytes). A failure here reads "couldn't save a copy", never "backup failed":
  //   it's the secondary path.
  //
  // importFromFile:
  //   guard isWorkoutActive or treadmill connected or busy -> statusMessage, return
  //   pick with File.pickFileAsync(); cancelled -> return
  //   await importer.validate(fileSource(picked))   // BEFORE asking the user anything
  //   confirm with Alert.alert('Replace all workout history?', 'This replaces every
  //     workout on this phone with the contents of the backup. A safety copy of the
  //     current data is made first.', Cancel / Replace); cancelled -> return
  //   safety backup: exportToFile into new Directory(Paths.document, 'safety-backups'),
  //     remember that file for undo
  //   const result = await importer.importReplace(fileSource(picked), p => progressStage = ...)
  //   settings.restoreFrom(result.settings)   // only now, after the commit
  //   canUndoImport = true; statusMessage = `Imported ${result.workoutCount} workouts.
  //     Reconnect to your treadmill once.`
  //   BackupValidationError: show its message directly; it's written to be user-facing
  //
  // undoLastImport: validate + importReplace from the remembered safety backup, then
  //   restoreFrom its settings. Clears canUndoImport.
}
```

`Alert.alert` takes callbacks rather than returning a promise. Wrap it in a tiny
`confirm(title, message, confirmLabel): Promise<boolean>` helper in
`src/features/shared/confirm.ts` so the import reads top to bottom; write that helper
yourself, it's five lines.

### 9.8 — UI: the backup screen (agent-authored)

Per the collaboration model in `../README.md`, this is UI. Full TSX below, using only
tokens and components from `docs/learning/04-Monochrome-Theme.md`.

Creates: `src/features/backup/BackupScreen.tsx` and `src/app/backup.tsx`.

```tsx
import { ActivityIndicator, ScrollView, View } from 'react-native';

import { useBackup } from './useBackup';
import { Button } from '@/features/shared/theme/Button';
import { Screen } from '@/features/shared/theme/Screen';
import { SecondaryButton } from '@/features/shared/theme/SecondaryButton';
import { Surface } from '@/features/shared/theme/Surface';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';
import { useTheme } from '@/features/shared/theme/useTheme';

export function BackupScreen() {
  const b = useBackup();
  const { colors } = useTheme();
  const locked = b.isWorkoutActive || b.busy;

  return (
    <Screen>
      <ScrollView contentContainerStyle={{ padding: spacing.lg, gap: spacing.md }}>
        <Text variant="subheadline">Export</Text>
        <Text variant="caption">
          Builds a single ZIP with your full workout history, saved devices, and settings.
        </Text>

        {b.isWorkoutActive && (
          <Surface style={{ padding: 14 }}>
            <Text variant="caption">Backup is disabled while a workout is active. Finish the workout first.</Text>
          </Surface>
        )}

        <Button label="Export backup" onPress={() => void b.exportAndShare()} disabled={locked} />
        <SecondaryButton label="Save a copy to device" onPress={() => void b.saveCopyToDevice()} disabled={locked} />

        <View style={{ height: 1, backgroundColor: colors.border, marginVertical: spacing.sm }} />

        <Text variant="subheadline">Import</Text>
        <Text variant="caption">
          Replaces all workout history on this phone with the contents of a backup ZIP. A safety copy of the current data is made automatically first.
        </Text>

        <Button label="Choose backup file..." onPress={() => void b.importFromFile()} disabled={locked} />
        {b.canUndoImport && (
          <SecondaryButton label="Undo last import" onPress={() => void b.undoLastImport()} disabled={b.busy} />
        )}

        {b.busy && (
          <View style={{ alignItems: 'center', gap: spacing.sm }}>
            <ActivityIndicator color={colors.textPrimary} accessibilityLabel="Working" />
            {b.progressStage !== null && <Text variant="caption">{b.progressStage}</Text>}
          </View>
        )}

        {b.statusMessage !== null && (
          <Surface style={{ padding: 14 }}>
            <Text>{b.statusMessage}</Text>
          </Surface>
        )}
      </ScrollView>
    </Screen>
  );
}
```

`src/app/backup.tsx`:

```tsx
import { BackupScreen } from '@/features/backup/BackupScreen';

export default BackupScreen;
```

### 9.9 — Wire it up

Touches: `src/features/services.tsx`, `DashboardScreen.tsx`, `package.json`.

Concrete steps:
1. `npm install fflate`.
2. `services.tsx`: `exporter: new BackupExporter(db)`, `importer: new BackupImporter(db)`.
3. Add a "Backup" link next to "Settings" on the dashboard.
4. Run it and walk through the manual test table below on-device before calling the
   phase done.

---

## Tests

| Test | Expected |
|------|----------|
| Export with 50 workouts | ZIP created, opens on a PC, contains all five files |
| Export → wipe → import | Row counts match **and** SHA-256 of a canonically-ordered dump matches (task 9.5) |
| Kill app mid-import | No data loss; pre-import backup recoverable |
| Import corrupt ZIP | Clear error, existing data untouched |
| Import archive with an unexpected entry | Rejected (task 9.5) |
| Import backup with newer format version | Clear "created by a newer version" message (task 9.5) |
| Failure mid-import | Everything rolled back (task 9.5) |
| Export during active workout | Blocked with explanation |
| Auto-backup after 6 workouts | Exactly 5 files retained, oldest rotated out |

The automated rows run in `backup.test.ts` (task 9.5). "Kill app mid-import" needs a
real device: **`[HUMAN]`**: start an import with a large backup, force-stop the app
mid-import (`adb shell am force-stop com.myhi.companion`), relaunch, and confirm the
database is either fully the old data or fully the new data, never a mix (SQLite's
transaction guarantees this if task 9.3 is structured correctly; this test is there to
catch a mistake in the *code*, not in SQLite).

## Acceptance

- [ ] The export→wipe→import **checksum** test passes. "No data loss" is measured by
      checksum, not by eyeballing a list
- [ ] No test leaves the database in a partial state
- [ ] Allowlist and format-version rejection tests pass
- [ ] Export is blocked while a workout is active; import is blocked the same way and
      while connected
- [ ] `tsc` and lint clean; all Phase 00–08 tests still pass
