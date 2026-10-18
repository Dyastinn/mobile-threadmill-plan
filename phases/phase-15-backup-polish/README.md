# Phase 15 — Backup Polish (optional)

> Only if Phases 00–14 are done **and the app is in daily use.** If it isn't in daily
> use, this phase is speculative work on a product nobody has proven they want yet.
> See `../README.md` for the collaboration model: you write the logic, the agent
> writes the UI. This phase has a small UI surface: a Merge/Replace choice on import,
> and a CSV export button next to Phase 09's existing ZIP export.

**Hardware:** none · **Size:** M · **Blocked by:** Phase 09 (Backup, minimal). Needs
`WorkoutId` and the single-ZIP export/import flow that phase already ships.

---

## Goal

Make backups more useful without weakening any of Phase 09's safety guarantees:
a human-readable CSV export for spreadsheets, a merge import mode that doesn't
require wiping the phone's existing history first, and the plumbing for backup
format migrations that don't have a real case to handle yet.

### Understanding what you're building (read this before the tasks)

**Merging two workout histories only works if every entry carries a global
identity, not just a local one.** A date alone can't tell you whether two
"Tuesday" journal entries describe the same event twice or two different
events that happened to land on the same day; you need something else to
tell them apart. That's exactly the problem Task B (merge import) solves for
this app: two phones can each accumulate their own workout history, and
merging them means answering "is this the same workout, or two different
ones?" A `WorkoutId` (a GUID, stamped once per workout back in Phase 06)
already answers that by construction. Unlike the database's own
auto-incrementing integer `Id` (which means "the Nth row on this phone" and
nothing more), a GUID means the same workout no matter which phone's database
it's sitting in.

**Why merge wasn't built in Phase 09, and why it's cheap now.** The simpler
alternative (the one Phase 09 actually shipped) is: import always means
Replace, wipe the phone's existing history, restore from the file. That wasn't
a shortcut that skipped real work; it was the correct call for a feature nobody
had asked to use yet ("Only if Phases 00–14 are done **and the app is in daily
use**," per the header above). What makes Merge affordable to add *now*,
instead of a multi-day project, is that the one piece of infrastructure it
actually needs, a way to tell "same workout" from "different workout" across
two independent databases, was already paid for in Phase 06, for reasons that
had nothing to do with backups. Task B's entire merge logic is one SQL
statement (`INSERT OR IGNORE INTO Workout (WorkoutId, ...) VALUES (...)`,
relying on `WorkoutId`'s uniqueness) plus a 4-case test matrix. Contrast that
with what it would have cost to retrofit global identity after the fact: if
Phase 06 had used only the auto-increment integer, adding merge later would
mean migrating every already-shipped phone's data onto some new globally
unique key, on databases already holding real history. That's a far bigger,
riskier job than the near-zero cost of choosing a GUID as the portable identity
column back when the table was still empty.

**The pattern, named plainly.** This is a case of paying a near-zero cost
today to keep a future option open: deliberately provisioning a seam (global
identity via GUID) before there's a demonstrated need for the feature that
seam enables (merge import). This maps directly onto Metz's rule
elsewhere in this project's teaching notes: "only justify a pattern when a
demonstrated, concrete problem calls for it, never 'in case it's needed
later.'" The GUID doesn't violate that rule, because its cost was genuinely
negligible: one extra column, minted once, with no extra machinery, no
speculative interface, no code written against a guessed future requirement.
Compare that to the alternative shape of "preparing for merge" that *would* have
been speculative overhead: building a full merge UI and conflict-resolution flow
back in Phase 06, before Phase 09's backup format even existed to merge. That would
have been exactly the kind of "in case it's needed later" complexity Metz warns
against, because it's expensive machinery built against a guess. The test for
whether a seam is worth placing early isn't "might this be useful someday," it's
"does placing it now cost close to nothing, and does retrofitting it later cost a
lot." An identity column is the former; a UI and merge algorithm is the latter. Task
C in this same phase makes the identical call in the opposite direction, worth
noting as the contrast: it ships the migration *mechanism* (an empty list, a runner
that walks it) but deliberately holds off on writing any actual migration, "because
`backupFormatVersion` has only ever had one value so far... a migration written
against a guessed future format is a migration written against the wrong format."
Same principle, applied consistently: cheap seam now, expensive guesswork deferred
until there's something real to build against.

## Learning goals

- **Locale-independent formatting**: why any number written to a file (as opposed
  to shown on screen) must never depend on the phone's regional settings. In
  JavaScript the default is already safe (`String(12.5)` and `toFixed` always write
  `12.5`); the locale-sensitive ones are `toLocaleString` and `Intl.NumberFormat`.
  The trap is *reaching for* those, not forgetting to avoid a default.
- **Byte order marks (BOM)**: what the three bytes `EF BB BF` at the start of a UTF-8
  file are for, why Excel specifically needs them to detect non-ASCII text correctly,
  and that in JavaScript you add one by putting the character `﻿` first in the
  string before encoding it.
- **SQL conflict resolution**: `INSERT OR IGNORE` as a way to merge two datasets that
  might overlap without hand-writing "does this row already exist" checks yourself.
- **Designing for a version that doesn't exist yet**: the backup format migration
  mechanism ships now, with zero migrations registered, because retrofitting a
  migration *mechanism* after the first breaking format change is much harder than
  building the mechanism before you need it.

## Reference docs

- **`Number.prototype.toFixed`**: https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Number/toFixed,
  and, for contrast, **`toLocaleString`**: https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Number/toLocaleString.
  Read both before writing the CSV exporter: the first is what file output wants, the
  second is what it must never use.
- **SQLite `ON CONFLICT` clause**: https://sqlite.org/lang_conflict.html, the
  official spec for `INSERT OR IGNORE` (and the other four conflict-resolution
  keywords). Read the IGNORE section specifically: it silently skips the *violating
  row only* and continues, which is exactly the semantics merge-by-`WorkoutId` wants.
- **`expo-sharing`** and **`expo-file-system`**: already used in Phase 09; the CSV
  export goes out exactly the same way as the ZIP.

---

## Task A — CSV export of workout summaries

**Export-only, not round-trippable: say so in the UI** (task D below). Two
correctness requirements that are easy to miss and annoying to discover later:

- Write a **UTF-8 BOM**, or Excel mangles non-ASCII (an accented name in `Notes`).
- Format numbers locale-independently, with a documented note: Excel parses CSV using
  the OS list separator, so in comma-decimal locales it expects `;` between columns
  and may split `12.5` oddly. That's Excel's import behaviour, not a bug in the file;
  the file stays standard RFC 4180 (comma-separated, `.` decimals) and the note in the
  UI says so.

### Concrete steps

1. Create `src/core/backup/workoutCsv.ts` (`core/`: pure data formatting with no React
   Native dependency, same reasoning as the protocol parsers).
2. Decide the column set from the `Workout` table
   (`../phase-06-recording-schema/README.md`). A reasonable summary row:
   `WorkoutId, StartedAtUtc, DurationSeconds, DistanceMeters, Calories, AvgSpeedKph,
   MaxSpeedKph, AvgHeartRate, MaxHeartRate, Notes`. **Never export the integer `Id`**,
   same rule as the ZIP backup in Phase 09: it means a different workout on every
   phone.
3. Implement against this shape, not the full thing, just enough that you're not
   staring at a blank file:

   ```ts
   /**
    * One row per workout. The caller supplies the already-loaded list: this function
    * does no database I/O, only formatting. Keeping it a pure function of
    * (workouts) -> bytes is what makes it testable without a database.
    */
   export function toWorkoutCsv(workouts: readonly WorkoutExport[]): Uint8Array {
     // TODO: start the text with '﻿' (the BOM). TextEncoder encodes it as the
     // three bytes EF BB BF, which is exactly what Excel looks for.

     // TODO: the header row: the column list decided above, comma-separated.

     // TODO: one row per workout. Numbers via String(n) or n.toFixed(2): both are
     // locale-independent in JavaScript. Never toLocaleString / Intl.NumberFormat:
     // those are the bug this whole task exists to prevent.

     // TODO: any text field that could contain a comma, quote, or newline (Notes is
     // the only one here) needs RFC 4180 quoting: wrap in double quotes and double
     // any quote inside. Decide the rule now, don't bolt it on after an export breaks
     // on someone's first multi-line note. null fields are empty, not "null".

     // TODO: lines end with \r\n (RFC 4180), then return new TextEncoder().encode(text).
   }
   ```
4. Write `src/core/backup/workoutCsv.test.ts`:
   - The first three bytes of the output are `0xEF 0xBB 0xBF`.
   - A workout with `distanceMeters = 12.5` produces the literal text `12.5` (or
     `12.50`, whichever format you chose) in the output **even when
     `Number.prototype.toLocaleString` is temporarily replaced** with a function that
     returns `'12,5'`. Save the original, replace it, run the export, assert, and
     restore it in a `finally`: a test that leaves a global patched will
     intermittently break unrelated tests that run after it. This is the test that
     proves the exporter never reaches for the locale-sensitive formatter.
   - A `notes` value containing a comma, a quote and a newline is quoted correctly and
     a proper CSV parse (write a ten-line one in the test, or split carefully) still
     yields the right column count.
5. Verify manually: export against real data, open the file in a plain text editor and
   confirm the header line looks correct (not prefixed with visible junk characters,
   which would mean the BOM landed wrong), then open it in Excel or Google Sheets and
   confirm values land in the right columns and a non-ASCII note reads correctly.

## Task B — Merge import mode

Now cheap, because `WorkoutId` exists: `INSERT OR IGNORE` on the GUID.

Still needs a **4-case test matrix**: empty / disjoint / partial overlap / full
overlap. And defined conflict semantics for same-GUID-different-data: decide and
document, do not leave it to whichever row SQLite happens to keep.

**Never de-duplicate on the integer `Id`.** The same integer means different workouts
on two phones.

### Concrete steps

1. Touches: `src/core/backup/backupImporter.ts` (Phase 09) plus a merge-specific SQL
   path alongside the replace one.
2. Add the mode as an explicit union, not a boolean. A `merge: boolean` parameter
   reads fine at the call site today and ambiguous in six months:

   ```ts
   export type ImportMode = 'replace' | 'merge';

   async import(open: () => Promise<ByteSource>, mode: ImportMode, onProgress?: ...): Promise<ImportResult> {
     // TODO: 'replace' is Phase 09's existing path, unchanged by this phase.
     // TODO: 'merge' follows the *same* safety rules Phase 09 established for
     // replace: validate first, pre-import safety backup, the whole import inside
     // one SQLite transaction, settings applied only after that transaction commits
     // (and for merge, consider not applying them at all: decide and say why).
     // "Merge feels less destructive than replace" is not a reason to relax any of
     // those rules: a merge that partially applies on a mid-import crash is exactly
     // as bad as a partial replace.
   }
   ```
3. The merge SQL shape: one statement per imported `Workout` row, inside the same
   transaction as everything else:

   ```sql
   -- Insert only if this WorkoutId isn't already present. IGNORE skips just the
   -- conflicting row and keeps going: see the ON CONFLICT doc link above for why
   -- that's the right primitive here instead of a manual SELECT-then-INSERT per row.
   INSERT OR IGNORE INTO Workout
       (WorkoutId, StartedAtUtc, EndedAtUtc, StartOffsetMinutes, DurationSeconds,
        DistanceMeters, Calories, AvgSpeedKph, MaxSpeedKph, AvgHeartRate,
        MaxHeartRate, Status, DeviceId, Notes, CreatedAtUtc, UpdatedAtUtc)
   VALUES
       ($workoutId, $startedAtUtc, $endedAtUtc, $startOffsetMinutes, $durationSeconds,
        $distanceMeters, $calories, $avgSpeedKph, $maxSpeedKph, $avgHeartRate,
        $maxHeartRate, $status, $deviceId, $notes, $createdAtUtc, $updatedAtUtc);

   -- TODO: decide what happens to WorkoutSample rows for a WorkoutId that already
   -- existed on this phone but carries *different* header data in the imported file
   -- (the "same-GUID-different-data" case). Pick one rule, e.g. "existing data wins;
   -- samples for an already-present WorkoutId are never imported", and write the
   -- decision as a comment right next to this query, not just in your head. Whatever
   -- you pick, it needs a test in the matrix below.
   ```
   The run result's `changes` tells you whether a row was inserted (`1`) or ignored
   (`0`), which is exactly what the samples step needs to know. When a row is ignored,
   `lastInsertRowId` is meaningless; look the existing row up by `WorkoutId` if your
   rule needs it.
4. Write the 4-case test matrix as real Jest tests against `nodeSqliteDb`, following
   `backup.test.ts`'s pattern (Phase 09):
   - **Empty**: importing into a database with zero existing workouts. Every imported
     row lands; count after equals count in the file.
   - **Disjoint**: existing workouts and imported workouts share no `WorkoutId`. Both
     sets survive; count after equals the sum.
   - **Partial overlap**: some `WorkoutId`s exist on both sides. Only the new ones get
     inserted; count after equals existing-count plus new-only-count, and your
     documented same-GUID-different-data rule holds for the overlapping ones.
   - **Full overlap**: every imported `WorkoutId` already exists. Zero rows inserted;
     count after equals count before; nothing on the existing rows changes (checksum
     equal, using Phase 09's checksum helper).
5. Statistics and personal records are **derived, not imported**. After either
   replace or merge, they're recomputed from the `Workout`/`WorkoutSample` tables now
   on the phone, which Phase 10's queries already do on every screen load. Never write
   imported statistics/PR values directly; that creates a second source of truth that
   can silently drift from the data it's supposed to summarize.

## Task C — Backup format migration

For older `backupFormatVersion` values, **once such versions actually exist.** The
mechanism shipped in Phase 09 (the manifest already carries an integer
`backupFormatVersion`, independent of the app version); this task is the migration
*runner* itself, with zero real migrations registered yet.

### Concrete steps

1. Creates: `src/core/backup/backupMigrations.ts`.
2. Shape it as a small ordered chain, mirroring how `migrationRunner.ts` already
   handles the *database* schema (same idea, applied to backup records instead of
   SQLite DDL). Because samples stream line by line, a migration transforms one
   record at a time rather than whole files:

   ```ts
   export interface BackupMigration {
     readonly fromVersion: number;
     readonly toVersion: number;
     // TODO: pick one style and be consistent: return transformed copies (simplest to
     // test) rather than mutating in place.
     migrateManifest(manifest: unknown): unknown;
     migrateWorkout(workout: unknown): unknown;
     migrateSample(sample: unknown): unknown;
   }

   // Empty for now: this phase ships the mechanism, not a migration, because
   // backupFormatVersion has only ever had one value so far. Add entries here the day
   // a real format change happens, not before: a migration written against a guessed
   // future format is a migration written against the wrong format.
   export const backupMigrations: readonly BackupMigration[] = [];

   /** The migrations needed to bring `fromVersion` up to BACKUP_FORMAT_VERSION, in order. */
   export function migrationsFrom(fromVersion: number): readonly BackupMigration[] {
     // TODO: walk backupMigrations from fromVersion to the current version. If
     // fromVersion is already current, this is an empty list. If fromVersion is
     // *higher* than current, that's Phase 09's existing "created by a newer version"
     // error path: don't duplicate that check here.
   }
   ```
3. Wire it into the importer: after reading the manifest's version, run every record
   through `migrationsFrom(version)` before validating its fields. With an empty list
   that's a no-op, which is the point.
4. No tests beyond a trivial "current version in, empty list out" case belong here
   yet. Writing tests against a migration for a format version that doesn't exist is
   testing a guess. Add real migration tests the day a real old version shows up.

## Task D — UI: Merge/Replace choice and CSV export button

Full TSX, using only the monochrome theme's tokens and components from
`../../docs/learning/04-Monochrome-Theme.md`. These sections replace the Export and
Import sections of Phase 09's `BackupScreen.tsx`; adjust names to whatever your
`useBackup` actually returns (it gains `exportCsv()`, `importMode` and
`setImportMode()` in this phase).

```tsx
{/* Export: CSV button added next to Phase 09's ZIP export */}
<Surface style={{ padding: spacing.lg, gap: spacing.md }}>
  <Text variant="subheadline">Export</Text>
  <Text variant="caption">
    CSV is a summary only: it can't be imported back into the app. Use ZIP export and import for a full, restorable backup. CSV uses commas and dots; in some regions Excel needs Data → From Text to read it.
  </Text>
  <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md }}>
    <Button label="Export ZIP" onPress={() => void b.exportAndShare()} disabled={locked} />
    <SecondaryButton label="Export CSV" onPress={() => void b.exportCsv()} disabled={locked} />
  </View>
</Surface>

{/* Import: Merge/Replace choice */}
<Surface style={{ padding: spacing.lg, gap: spacing.md, marginTop: spacing.md }}>
  <Text variant="subheadline">Import</Text>
  <Text variant="caption">
    Merge keeps everything already on this phone and adds any new workouts from the file. Replace deletes this phone's history first, then restores from the file.
  </Text>
  <SegmentedControl
    options={['merge', 'replace'] as const}
    value={b.importMode}
    onChange={(mode) => b.setImportMode(mode)}
    label={(mode) => (mode === 'merge' ? 'Merge (recommended)' : 'Replace')}
    accessibilityLabel="Import mode"
  />
  <Button label="Choose backup file..." onPress={() => void b.importFromFile()} disabled={locked} />
</Surface>
```

Notes on wiring this up:

- `importMode` is a `useState<ImportMode>('merge')` in `useBackup`, passed to the
  importer when `importFromFile` runs. The confirmation dialog's text should change
  with it: Replace keeps Phase 09's "replaces every workout" warning; Merge says what
  it does instead ("adds workouts that aren't already on this phone").
- `exportCsv` loads the workout headers (one `SELECT` through the `Db`, the same
  columns as Phase 09's `workouts.json`), calls `toWorkoutCsv`, writes the bytes to a
  file in the cache directory with Phase 09's `exportToFile`-style `.tmp` + rename,
  and shares it with `mimeType: 'text/csv'`.
- Everything above resolves through existing theme components. `Surface` already
  gets the surface color and border, `Button`/`SecondaryButton` the interactive
  colors, `SegmentedControl` the selected-state treatment. Nothing here is an inline
  color or a new token.
- `disabled={locked}` (workout active or busy) on every button prevents
  double-tapping an export or import that's already running; the theme's `Button`
  handles the dimmed appearance.

---

## Tests

| Test | Expected |
|------|----------|
| CSV export, then inspect raw bytes | First three bytes are `EF BB BF` |
| CSV export with `toLocaleString` patched to return comma decimals | Numeric fields still use `.` as the decimal separator |
| CSV export with a comma, quote and newline in `notes` | Field is quoted; column count after a proper parse is still correct |
| CSV opens in Excel or Sheets | Values in the right columns, non-ASCII text intact |
| Merge import — empty database | All imported workouts inserted |
| Merge import — disjoint sets | Both sets present, count is the sum |
| Merge import — partial overlap | Only new `WorkoutId`s inserted; documented same-GUID rule holds |
| Merge import — full overlap | Zero rows inserted, nothing changed |
| Statistics/PRs after any import (merge or replace) | Recomputed from `Workout`/`WorkoutSample`, never imported directly |

## Acceptance

- [ ] CSV opens correctly in a spreadsheet with non-ASCII text and decimal values
- [ ] All four merge cases pass
- [ ] Statistics and PRs recomputed after any import, never imported
- [ ] Merge/Replace choice and CSV export button render correctly in both light and
      dark theme, using only existing theme tokens and components
