# Phase 09 — Backup

> Kept small on purpose. Merging, CSV and format upgrades are Phase 15. This phase only
> makes sure your data can never be lost.

**Treadmill needed:** no · **Size:** M · **Waits for:** Phase 08

---

## Goal

Get your workout history off this phone and onto another one, without ever leaving the
database worse off than before you tried.

### The idea

One promise, like packing for a move: nothing that goes in the box goes missing, even if
the move is interrupted halfway.

**Why not build merging, CSV and format upgrades now too?** Each has a real reason to
wait:
- **Merging** needs rules for "the same workout is on both phones but different". You
  can't write good rules without a real case to look at.
- **CSV** is a second format nobody needs yet. The ZIP already gets the data off the
  phone.
- **Format upgrades** can't be written until there's an old format to upgrade from.
  The backup already stores a format number, so that's possible later.

What's left is the minimum for "you can always get your data back": full export,
import that replaces everything, automatic backups, and a safety copy before any import.

**Check everything before trusting it.** Same idea as Phase 01's packet checks, at a
new boundary: a ZIP file you picked could be broken, edited or hostile. So the importer
checks the whole file first (which files are inside, how big it unpacks, which format
version) before touching the database. And the import happens in one transaction, with
a safety copy made first, so a failure at row 40,000 can't leave you with neither your
old data nor the new.

**About "zip slip"**, a well-known ZIP attack: a file inside named `../../something`
that writes outside its folder when unzipped. It only affects code that unzips files to
disk. This importer never does; it reads each file's bytes straight into the database.
The check for "only our five file names" still stays: it rejects anything that isn't
one of our backups before any work starts.

## What you'll learn

- **Splitting between `core/` and `features/` again.** Building and reading the ZIP,
  the SQL, and every safety check are plain logic in `core/`, with real tests. Only the
  files, the share sheet, the file picker and the confirmation dialog are in `features/`.
- **Streaming**: reading a 30 MB file 64 KB at a time, unpacking as you go, and saving
  each line, so memory stays flat however big the backup is. And why the samples are
  saved one JSON object per line (JSON Lines) instead of one big JSON list: a big list
  can't be read until it's all in memory.
- **Small interfaces for bytes** (`ByteSink`, `ByteSource`), so `core/` can write and read
  file contents without touching files.
- **Why settings are restored only after the database is done.** The settings store
  isn't part of the transaction, so it goes last.

## Scope

**In:** export everything to one ZIP · import that **replaces** everything · automatic
backup when a workout finishes (last 5 kept, private to the app) · a safety copy before
any import.

**Not in (Phase 15):** merging · CSV · format upgrades. Totals and records aren't
exported at all: they're calculated from the workouts, so exporting them would create
two versions of the truth.

## What's in a backup

```
MyHiBackup_2026-07-28_1930.zip
├── manifest.json     (format version, app version, date, counts)
├── devices.json      (a list)
├── workouts.json     (a list, each with its WorkoutId GUID)
├── samples.jsonl     (one sample per line, each with its WorkoutId GUID)
└── settings.json     (an object)
```

Always in that order. The importer depends on it: devices before workouts (a workout
points to a device), workouts before samples (a sample needs its workout's new row id).

One manifest file, not two: two files about the same thing drift apart.

The **format version** is a plain number, separate from the app version. A small app
update mustn't make old backups unreadable.

The backup uses the GUIDs (`WorkoutId`, `DeviceUid`). **The number `Id`s are never
exported**; new ones are made on import. (Phase 06, "Backups".)

---

## Moving the file

- **Export: the share sheet.** Build the ZIP in the app's cache folder, then
  `Sharing.shareAsync(file.uri, { mimeType: 'application/zip' })`. No storage
  permissions, one tap to Drive, Gmail or Nearby Share.
- **Also: "Save a copy to device"**, with `Directory.pickDirectoryAsync()`, which opens
  Android's folder picker. A second option, because some cloud folders behave oddly; the
  share sheet always works.
- **Import:** `File.pickFileAsync()`. Phones label ZIP files inconsistently, so accept
  any file and check its first four bytes are `50 4B 03 04` ("PK"), not its name.
- **Don't** hardcode a folder like `Documents/MyHi Companion/Backup/`. Android doesn't
  let apps write wherever they like.

---

## Technology decision: `fflate` for ZIP files

**Problem.** Backups can be tens of MB, and both directions must stream: build the ZIP
without holding every sample in memory, and read it without unpacking it all first.

**Why this.** Plain JavaScript (no native code, nothing to rebuild, works the same on
the phone and in tests), small, MIT licensed, and it really streams both ways: `Zip` +
`ZipDeflate` to build, `Unzip` + `UnzipInflate` to read. When reading, it tells you each
file's name before its contents, so the name check happens first. Being plain
JavaScript, it's allowed in `core/`.

**Alternatives.**

1. **`react-native-zip-archive`.** Good: native and fast. Bad: it only zips folders and
   unzips to folders, so import means unpacking to disk first (bringing zip slip back),
   and it can't run in tests. Better for apps that actually want files on disk.
2. **JSZip.** Good: the best-known, friendly. Bad: loads the whole ZIP into memory to read
   it. Better for small files.
3. **Copying the database file itself.** Good: very simple. Bad: tied to the table
   layout, carries number `Id`s that mean nothing on another phone, and can't be merged
   (Phase 15). Better as a same-phone "undo" copy.

**Why not those.** The first needs a disk round trip and can't be tested; the second
can't stream; the third gives up the portable format.

**Long term.** No native code, so no upgrade risk. Its streaming API is lower-level than
JSZip's, which is code to write once and test once.

**In the code:** `src/core/backup/backupExporter.ts` and `backupImporter.ts`.

---

## Safety rules (these are the point of the phase)

**A failed import must not destroy your data.** "Delete everything, then restore" fails
badly if it breaks halfway. So:

1. Check the whole file **before** touching the database (task 9.3).
2. Make a safety copy of your current data first.
3. Do the whole import in **one transaction**.
4. Restore settings **after** the transaction finishes.
5. Offer "Undo last import" for the rest of the session.

Also:

- **Safe export:** write to `.tmp`, then rename. An interrupted export never leaves a
  half-written file with a real-looking name.
- **Name check:** reject any file inside the ZIP that isn't one of the five names.
- **Size limit:** reject anything that unpacks to more than ~500 MB, counting the bytes
  as they come out (a file's stated size can be wrong, or missing).
- **Streaming:** never hold the whole unpacked backup in memory.
- **No export during a workout; no import while connected or during a workout.**
- **Times in UTC plus offset**, as ISO 8601.
- **A restored device won't connect by itself** on a new phone. Say so, so it doesn't
  look broken.

---

## Read first

- **`fflate`**: https://github.com/101arrowz/fflate. The "Streaming" and "ZIP files"
  sections: `Zip`/`ZipDeflate` (task 9.2), `Unzip`/`UnzipInflate` with `onfile` (9.3).
- **`TextDecoder`**: https://developer.mozilla.org/en-US/docs/Web/API/TextDecoder.
  `decoder.decode(chunk, { stream: true })` turns bytes into text without breaking a
  character that's split across two chunks.
- **`expo-file-system`**: https://docs.expo.dev/versions/latest/sdk/filesystem/. `File`,
  `Directory`, `Paths.cache`, `Paths.document`, `file.open()` and its `readBytes` /
  `writeBytes`, `File.pickFileAsync`, `Directory.pickDirectoryAsync`, `move`.
- **`expo-sharing`**: https://docs.expo.dev/versions/latest/sdk/sharing/.
- **`node:sqlite`** and **`node:crypto`**: tests only (9.5), for a real database and a
  checksum.

---

## Tasks

### 9.1 — Types and byte interfaces (write these as is)

Create `src/core/backup/backupTypes.ts`. Plain data shapes, so here they are in full.
Each field matches a Phase 06 column, with GUIDs instead of the number ids:

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
  readonly workoutId: string;          // the GUID; the number Id is never exported
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
  readonly deviceUid: string | null;   // the device's GUID, not its number Id
  readonly notes: string | null;
}

export interface WorkoutSampleExport {
  readonly workoutId: string;          // links to WorkoutExport.workoutId
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

/** Where backup bytes go. In the app: a file. In tests: an array. */
export interface ByteSink {
  write(chunk: Uint8Array): Promise<void>;
}

/** Where backup bytes come from. read() returns an empty array at the end. */
export interface ByteSource {
  read(maxBytes: number): Promise<Uint8Array>;
}

/** An error with a message the screen can show as is. */
export class BackupValidationError extends Error {}
```

Add two small methods to Phase 08's `AppSettings`:

```ts
toExport(): SettingsExport {
  // TODO: { key: savedString } for every key in KEYS that has a saved value
}

restoreFrom(exported: SettingsExport): void {
  // TODO: write each known key back with store.setString, then refresh().
  // Ignore unknown keys (a newer backup may have settings this version doesn't).
  // Junk values still fall back to defaults when read (Phase 08).
}
```

### 9.2 — `BackupExporter` (yours)

Create `src/core/backup/backupExporter.ts`:

```ts
import { Zip, ZipDeflate } from 'fflate';

export interface ExportInput {
  readonly appVersion: string;
  readonly settings: SettingsExport;
  readonly now: Date;
}

export class BackupExporter {
  constructor(private readonly db: Db) {}

  /** Streams a whole backup ZIP into `sink`. Doesn't know about files; task 9.6 does. */
  async export(sink: ByteSink, input: ExportInput): Promise<BackupManifest> {
    // TODO, in order:
    // 1. Count workouts, samples and devices first, so the manifest can go first.
    // 2. Collect fflate's output in an array as it's produced:
    //      const pending: Uint8Array[] = [];
    //      const zip = new Zip((err, chunk) => { if (err) throw err; pending.push(chunk); });
    //    and write a small drain() that sends everything in `pending` to the sink.
    //    Call drain() after each step, so output doesn't pile up in memory.
    // 3. For each file, in ENTRY_NAMES order:
    //      const entry = new ZipDeflate(name, { level: 6 }); zip.add(entry);
    //      entry.push(new TextEncoder().encode(text), isLast)
    //    The four small files: build the whole JSON text and push it once.
    // 4. samples.jsonl: read 2,000 samples at a time
    //      (SELECT w.WorkoutId AS workoutId, s.ElapsedSec AS elapsedSec, ...
    //       FROM WorkoutSample s JOIN Workout w ON w.Id = s.WorkoutRowId
    //       ORDER BY s.WorkoutRowId, s.ElapsedSec LIMIT 2000 OFFSET $offset),
    //    push them as lines (JSON.stringify(row) + '\n'), then drain().
    //    The JOIN swaps each sample's WorkoutRowId for the workout's GUID.
    //    For workouts, LEFT JOIN Device (DeviceId can be NULL) to get the device GUID.
    // 5. zip.end(), drain(), return the manifest.
  }
}
```

### 9.3 — `BackupImporter` (yours)

Create `src/core/backup/backupImporter.ts`.

The most important file in this phase. It reads the ZIP **twice**: once to check
everything without touching the database, once to import. Reading 30 MB twice takes a
second or two; trusting a file you haven't checked could cost your whole history.

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

const MAX_UNPACKED_BYTES = 500 * 1024 * 1024;
const CHUNK = 64 * 1024;

export class BackupImporter {
  constructor(private readonly db: Db) {}

  /** Pass 1: reads everything, writes nothing. Throws BackupValidationError on the first problem. */
  async validate(open: () => Promise<ByteSource>): Promise<BackupManifest> {
    // TODO, in this order (each check protects the next):
    // 1. HEADER: the first 4 bytes are 50 4B 03 04, or it isn't a ZIP.
    // 2. NAMES: in unzip.onfile, before file.start(), reject any name not in
    //    ENTRY_NAMES, and any name seen twice.
    // 3. SIZE: in each file's ondata, add up the bytes; over MAX_UNPACKED_BYTES -> reject.
    // 4. VERSION: backupFormatVersion > BACKUP_FORMAT_VERSION -> reject with
    //    "This backup was made by a newer version of the app."
    // 5. CONTENTS: every line and item parses and has the right fields; the counts
    //    match the manifest. Otherwise reject, naming the file and line.
    // How to read: register UnzipInflate once, then loop:
    //   chunk = await source.read(CHUNK); unzip.push(chunk, chunk.length === 0);
    //   stop when the chunk is empty.
    // onfile/ondata run during push(), so save any error in a variable and throw after.
  }

  /** Pass 2: only after validate() passed AND a safety copy was made. Replaces everything. */
  async importReplace(
    open: () => Promise<ByteSource>,
    onProgress?: (p: ImportProgress) => void,
  ): Promise<ImportResult> {
    // TODO:
    // 1. Do everything inside this.db.transaction(async (tx) => { ... }), using tx.
    //    If anything throws, all of it is undone.
    // 2. DELETE FROM WorkoutSample; DELETE FROM Workout; DELETE FROM Device;
    // 3. Read the ZIP again. Files arrive in ENTRY_NAMES order:
    //    - devices.json: insert each; keep a Map of deviceUid -> new Id
    //    - workouts.json: insert each (look up the device Id); keep a Map of
    //      workoutId -> new row Id, from each insert's lastInsertRowId
    //    - samples.jsonl: split the text into lines (keep a half line for the next
    //      chunk), insert each with ONE prepared statement and a plain INSERT
    //      (a duplicate means a broken file, and should fail loudly). Skip and count
    //      any sample whose workout isn't in the map. Report progress every few
    //      hundred rows.
    //    - settings.json: parse and keep it. Don't apply it here.
    // 4. Inserts are async but fflate's callbacks aren't, so: collect each chunk's
    //    complete lines during push(), then await their inserts before reading the
    //    next chunk. That keeps memory to one chunk's worth.
    // 5. Return the counts and settings. The caller restores settings only after
    //    this finishes, which is after the transaction is committed.
  }
}
```

### 9.4 — Automatic backups (yours)

Create `src/features/backup/autoBackup.ts`. It works with files, so it's in `features/`:

```ts
export const MAX_KEPT = 5;

export async function runAutoBackup(exporter: BackupExporter, settings: AppSettings): Promise<void> {
  // TODO:
  // 1. new Directory(Paths.document, 'auto-backups'); create it if it's not there
  // 2. export into it with exportToFile (task 9.6)
  // 3. list its MyHiBackup_*.zip files, sort by name (the date in the name sorts
  //    correctly as text), delete all but the newest MAX_KEPT
}
```

Call it from `WorkoutSession` (Phase 07) when the workout reaches `finished`, after the
final save. If it fails, log it; don't show it as a workout failure.

### 9.5 — Tests: export, wipe, import, compare

Create `src/core/backup/backup.test.ts` and `src/core/backup/testing/memoryBytes.ts` (a
`ByteSink` that collects chunks and a `ByteSource` that replays them, ~20 lines).

"Nothing is lost" has to mean something exact, not "the count looks right".

1. Same setup as `migrationRunner.test.ts`: a fresh test database with Phase 06's real
   migrations.
2. Add 10–50 workouts with samples and a couple of devices. Include: one with no end
   time, one with no heart rate, one with a negative time-zone offset, and a sample
   marked as a gap.
3. **Export, wipe, import, compare:** compute a checksum of all the data (SHA-256 from
   `node:crypto`, over every row in a fixed column order, sorted by GUID, never by
   number Id since those change). Export to memory, delete everything, validate and
   import the same bytes, compute the checksum again, expect it to match. Write a small
   checksum helper; it's test code.
4. **Unknown file inside:** build a ZIP with fflate's `zipSync({ 'manifest.json': ...,
   '../evil.txt': ... })`. `validate` throws `BackupValidationError`, and the database is
   untouched.
5. **Newer version:** a manifest with `BACKUP_FORMAT_VERSION + 1`. Same expectation.
6. **Failure halfway rolls everything back:** call `importReplace` directly (no
   `validate`) on a ZIP whose `samples.jsonl` has the same `(workoutId, elapsedSec)`
   twice near the end. The plain `INSERT` fails on the duplicate. Expect the error, and
   the checksum to equal the *original*: the deletes and every insert were undone.
7. `npm test`.

### 9.6 — File helpers (yours)

Create `src/features/backup/backupFiles.ts`:

```ts
/** Exports into `dir` safely: writes NAME.zip.tmp, then renames it to NAME.zip. */
export async function exportToFile(exporter: BackupExporter, dir: Directory, input: ExportInput): Promise<File> {
  // TODO:
  // 1. name = MyHiBackup_yyyy-MM-dd_HHmm.zip, from input.now in local time
  // 2. create `${name}.tmp` and open it
  // 3. the sink writes each chunk with handle.writeBytes(chunk)
  // 4. await exporter.export(sink, input); close the handle in a finally
  // 5. rename the .tmp to the real name (replace an old one). A crash before this
  //    leaves only a .tmp, never a half file with the real name.
}

/** Reads a file for the importer. */
export function fileSource(file: File): () => Promise<ByteSource> {
  // TODO: each call opens the file fresh (the importer reads it twice) and returns
  // { read: (n) => handle.readBytes(n) }, closing the handle when it reaches the end.
}
```

### 9.7 — `useBackup` (yours)

Create `src/features/backup/useBackup.ts`:

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
  // TODO: get exporter, importer, settings and treadmill from useServices(), and
  // isWorkoutActive from Phase 04's useWorkoutState.
  //
  // exportAndShare: refuse during a workout. Show "Building backup...". exportToFile
  //   into the cache folder, then Sharing.shareAsync(file.uri, { mimeType: 'application/zip' }).
  //   Show "Backup shared." On error, show the error's message. Always clear busy at the end.
  //
  // saveCopyToDevice: the same export, then Directory.pickDirectoryAsync() and copy
  //   the file there (the expo-file-system docs say how). If it fails, say "couldn't
  //   save a copy", not "backup failed": it's the extra option.
  //
  // importFromFile: refuse during a workout, while connected, or while busy.
  //   1. File.pickFileAsync(); cancelled -> stop
  //   2. importer.validate(...) BEFORE asking anything
  //   3. confirm: "Replace all workout history?" / "This replaces every workout on
  //      this phone with the backup. A safety copy of your current data is made first."
  //   4. safety copy: exportToFile into new Directory(Paths.document, 'safety-backups');
  //      remember it for undo
  //   5. importer.importReplace(..., p => show p.stage)
  //   6. settings.restoreFrom(result.settings), only now
  //   7. canUndoImport = true; show "Imported N workouts. Connect to your treadmill once."
  //   A BackupValidationError's message can be shown as is.
  //
  // undoLastImport: validate + importReplace from the safety copy, then restore its
  //   settings. Clears canUndoImport.
}
```

`Alert.alert` uses callbacks, not a promise. Write a tiny
`confirm(title, message, confirmLabel): Promise<boolean>` in
`src/features/shared/confirm.ts`, so the import reads top to bottom. About five lines.

### 9.8 — The backup screen (given)

Create `src/features/backup/BackupScreen.tsx` and `src/app/backup.tsx`.

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
        <Text variant="caption">One ZIP with your workouts, saved devices and settings.</Text>

        {b.isWorkoutActive && (
          <Surface style={{ padding: 14 }}>
            <Text variant="caption">Backup is off during a workout. Finish the workout first.</Text>
          </Surface>
        )}

        <Button label="Export backup" onPress={() => void b.exportAndShare()} disabled={locked} />
        <SecondaryButton label="Save a copy to device" onPress={() => void b.saveCopyToDevice()} disabled={locked} />

        <View style={{ height: 1, backgroundColor: colors.border, marginVertical: spacing.sm }} />

        <Text variant="subheadline">Import</Text>
        <Text variant="caption">
          Replaces all workouts on this phone with the ones in a backup. A safety copy of your current data is made first.
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

### 9.9 — Hook it up

1. `npm install fflate`.
2. Services file: `exporter: new BackupExporter(db)`, `importer: new BackupImporter(db)`.
3. A "Backup" link on the dashboard next to "Settings".
4. Run through the tests below on the phone before calling it done.

---

## Tests

| Test | Expected |
|------|----------|
| Export 50 workouts | The ZIP opens on a PC and has all five files |
| Export → wipe → import | Same counts **and** the same checksum (9.5) |
| Kill the app during an import | No data lost; the safety copy can be restored |
| Import a broken ZIP | Clear error, data untouched |
| A ZIP with an unexpected file | Rejected (9.5) |
| A backup from a newer version | Clear "made by a newer version" message (9.5) |
| Failure halfway | Everything undone (9.5) |
| Export during a workout | Refused, with an explanation |
| 6 workouts with auto-backup | Exactly 5 files kept, oldest removed |

On the phone: start a large import, force-stop the app in the middle
(`adb shell am force-stop com.myhi.companion`), reopen, and check the data is either all
old or all new, never a mix. SQLite guarantees this if task 9.3 is right; the test is
there to catch a mistake in the code.

## Done when

- [ ] The export → wipe → import **checksum** test passes
- [ ] No test leaves the database half-changed
- [ ] The unknown-file and newer-version tests pass
- [ ] Export is blocked during a workout; import is blocked during a workout and while
      connected
- [ ] `tsc` and lint are clean; all earlier tests pass
