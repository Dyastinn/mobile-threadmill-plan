# Phase 15 — Backup Extras (optional)

> Only once Phases 00–14 are done **and you're using the app every day.** Otherwise this
> is work on something nobody's proven they need yet. You write the logic; the agent
> writes the UI. The UI here is small: a Merge/Replace choice on import, and a CSV button.

**Treadmill needed:** no · **Size:** M · **Waits for:** Phase 09

---

## Goal

More useful backups, without weakening any of Phase 09's safety rules:

- a **CSV export** you can open in a spreadsheet,
- a **merge import** that adds workouts instead of replacing everything,
- the **mechanism for format upgrades**, for the day the backup format changes.

### The idea

**Merging needs a way to tell "same workout" from "different workout" across two
phones.** A date isn't enough: two workouts on the same Tuesday could be one workout or
two. The `WorkoutId` GUID from Phase 06 answers it: it's the same workout on every phone.
The number `Id` only means "the Nth row on this phone".

**Why it's cheap now.** Phase 09 only replaces, which was the right call while nobody
used the app daily. What makes merging easy now is that GUID, added in Phase 06 for other
reasons. The merge itself is one SQL statement (`INSERT OR IGNORE`, which skips a workout
whose GUID already exists) plus four tests. Without the GUID, adding merge would mean
changing every existing database first.

**The name for this:** keeping an option open when it costs almost nothing. The GUID was
one column. Building the merge feature back in Phase 06 would have been expensive work
against a guess. The rule: add something early only if it's nearly free now and expensive
later. Task C follows the same rule the other way round: it adds the *mechanism* for
format upgrades (cheap), but no actual upgrade (there's no old format to upgrade from yet).

## What you'll learn

- **Numbers in files must not depend on the phone's language.** In JavaScript the normal
  ways (`String(12.5)`, `toFixed`) are already safe: they always write `12.5`. The
  language-dependent ones are `toLocaleString` and `Intl.NumberFormat`. The trap is using
  those for files.
- **The BOM** (three bytes, `EF BB BF`, at the start of a file): Excel needs it to read
  accented letters correctly. In JavaScript you add it by starting the text with `﻿`.
- **`INSERT OR IGNORE`**: merge two sets that might overlap without checking each row
  yourself.
- **Building for a version that doesn't exist yet**: the upgrade mechanism ships with an
  empty list.

## Read first

- **`toFixed`**: https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Number/toFixed,
  and for contrast **`toLocaleString`**: https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Number/toLocaleString.
  The first is for files; the second must never be used for them.
- **SQLite `ON CONFLICT`**: https://sqlite.org/lang_conflict.html. The IGNORE section: it
  skips only the clashing row and carries on.
- **`expo-sharing`** and **`expo-file-system`**: as in Phase 09.

---

## Task A — CSV export of workout summaries

**CSV is export only; it can't be imported back. The screen says so** (task D). Two things
that are easy to miss:

- Start the file with a **BOM**, or Excel garbles accented letters (e.g. in notes).
- Numbers use `.` for decimals, always. Note for the UI: in countries that use `,` for
  decimals, Excel expects `;` between columns, so it may open the file oddly. That's
  Excel, not the file; the file follows the CSV standard (RFC 4180).

### Steps

1. Create `src/core/backup/workoutCsv.ts` (plain formatting, so `core/`).
2. Pick the columns from the `Workout` table. A good set: `WorkoutId, StartedAtUtc,
   DurationSeconds, DistanceMeters, Calories, AvgSpeedKph, MaxSpeedKph, AvgHeartRate,
   MaxHeartRate, Notes`. **Never the number `Id`**: it means a different workout on every
   phone.
3. Write it:

   ```ts
   /** One row per workout. Only formats; no database here, so it's easy to test. */
   export function toWorkoutCsv(workouts: readonly WorkoutExport[]): Uint8Array {
     // TODO: start the text with '﻿' (the BOM)
     // TODO: the header row
     // TODO: one row per workout. Numbers with String(n) or n.toFixed(2).
     //       Never toLocaleString or Intl.NumberFormat.
     // TODO: notes can contain commas, quotes or line breaks: wrap the field in "..."
     //       and double any " inside (the CSV standard). Empty for null, never "null".
     // TODO: end each line with \r\n, then return new TextEncoder().encode(text)
   }
   ```
4. Tests in `src/core/backup/workoutCsv.test.ts`:
   - The first three bytes are `0xEF 0xBB 0xBF`.
   - `distanceMeters = 12.5` appears as `12.5` (or `12.50`) **even when
     `Number.prototype.toLocaleString` is temporarily replaced** with one that returns
     `'12,5'`. Save the original, replace it, check, and put it back in a `finally`
     (otherwise later tests break). This proves the CSV never uses it.
   - A note with a comma, a quote and a line break is quoted correctly, and a proper CSV
     parse still finds the right number of columns.
5. By hand: export real data, open it in a text editor (the first line should start with
   the header, no strange characters), then in Excel or Google Sheets (values in the right
   columns, accents correct).

## Task B — Merge import

Easy now, thanks to `WorkoutId`: `INSERT OR IGNORE` on the GUID.

Still needs **four tests**: empty / no overlap / some overlap / full overlap. And a clear
rule for "same GUID, different data". Decide it and write it down; don't leave it to
chance.

**Never match workouts by the number `Id`.**

### Steps

1. Change Phase 09's `src/core/backup/backupImporter.ts`.
2. Make the mode a named choice, not a true/false. `merge: true` reads fine today and is
   unclear in six months:

   ```ts
   export type ImportMode = 'replace' | 'merge';

   async import(open: () => Promise<ByteSource>, mode: ImportMode, onProgress?: ...): Promise<ImportResult> {
     // TODO: 'replace' is Phase 09's code, unchanged.
     // TODO: 'merge' follows the SAME safety rules: check first, safety copy, one
     //       transaction, settings after (or not at all for a merge: decide and say why).
     //       "Merge feels safer" is no reason to skip any of them.
   }
   ```
3. The merge SQL, one statement per workout, in the same transaction:

   ```sql
   -- Adds the workout only if its WorkoutId isn't already here.
   INSERT OR IGNORE INTO Workout
       (WorkoutId, StartedAtUtc, EndedAtUtc, StartOffsetMinutes, DurationSeconds,
        DistanceMeters, Calories, AvgSpeedKph, MaxSpeedKph, AvgHeartRate,
        MaxHeartRate, Status, DeviceId, Notes, CreatedAtUtc, UpdatedAtUtc)
   VALUES
       ($workoutId, $startedAtUtc, $endedAtUtc, $startOffsetMinutes, $durationSeconds,
        $distanceMeters, $calories, $avgSpeedKph, $maxSpeedKph, $avgHeartRate,
        $maxHeartRate, $status, $deviceId, $notes, $createdAtUtc, $updatedAtUtc);
   ```

   **Your decision:** if a workout's GUID is already on this phone but the backup has
   different data for it, what happens to its samples? For example: "the phone's copy
   wins; samples for a workout already here are never imported". Write the rule as a
   comment next to this query, and test it.

   The insert's `changes` tells you whether it was added (`1`) or skipped (`0`), which is
   what the samples step needs to know. When it was skipped, `lastInsertRowId` means
   nothing; look the existing row up by `WorkoutId` if your rule needs it.
4. The four tests, with the test database, like Phase 09's `backup.test.ts`:
   - **Empty:** nothing on the phone. Everything is added; the count equals the file's.
   - **No overlap:** no shared GUIDs. Both sets kept; the count is the sum.
   - **Some overlap:** only new GUIDs added; your same-GUID rule holds.
   - **Full overlap:** every GUID already here. Nothing added, nothing changed (same
     checksum, using Phase 09's helper).
5. Totals and records are **never imported**. After any import they're simply calculated
   again by Phase 10's queries.

## Task C — Format upgrades

For backups made in an older format, **once an older format exists.** The manifest already
has a format number (Phase 09); this task adds the upgrade runner, with no upgrades in it.

### Steps

1. Create `src/core/backup/backupMigrations.ts`.
2. A small ordered list, like the database migrations. Samples are read one line at a
   time, so an upgrade changes one record at a time:

   ```ts
   export interface BackupMigration {
     readonly fromVersion: number;
     readonly toVersion: number;
     // Return a changed copy (simplest to test), don't edit in place.
     migrateManifest(manifest: unknown): unknown;
     migrateWorkout(workout: unknown): unknown;
     migrateSample(sample: unknown): unknown;
   }

   // Empty on purpose: the format has only ever had one version.
   // Add an entry the day the format really changes, not before.
   export const backupMigrations: readonly BackupMigration[] = [];

   /** The upgrades needed to go from `fromVersion` to the current version, in order. */
   export function migrationsFrom(fromVersion: number): readonly BackupMigration[] {
     // TODO: current version -> empty list. A newer version is already rejected by
     // Phase 09's check; don't check it again here.
   }
   ```
3. In the importer, after reading the manifest's version, pass every record through
   `migrationsFrom(version)` before checking its fields. With an empty list, that changes
   nothing, which is the point.
4. Only one test for now: current version in, empty list out. Testing an upgrade for a
   format that doesn't exist would be testing a guess.

## Task D — Merge/Replace choice and CSV button (given)

These replace the Export and Import sections of Phase 09's `BackupScreen.tsx`. Adjust the
names to what your `useBackup` returns (it gains `exportCsv()`, `importMode` and
`setImportMode()`).

```tsx
{/* Export, with the new CSV button */}
<Surface style={{ padding: spacing.lg, gap: spacing.md }}>
  <Text variant="subheadline">Export</Text>
  <Text variant="caption">
    CSV is a summary only: it can't be imported back. Use ZIP for a full backup you can restore. CSV uses commas and dots; in some regions Excel needs Data → From Text to open it.
  </Text>
  <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md }}>
    <Button label="Export ZIP" onPress={() => void b.exportAndShare()} disabled={locked} />
    <SecondaryButton label="Export CSV" onPress={() => void b.exportCsv()} disabled={locked} />
  </View>
</Surface>

{/* Import, with the Merge/Replace choice */}
<Surface style={{ padding: spacing.lg, gap: spacing.md, marginTop: spacing.md }}>
  <Text variant="subheadline">Import</Text>
  <Text variant="caption">
    Merge keeps everything on this phone and adds any new workouts from the file. Replace deletes this phone's history first, then restores the file.
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

Notes:

- `importMode` is `useState<ImportMode>('merge')` in `useBackup`, passed to the importer.
  The confirmation should match: Replace keeps Phase 09's "replaces every workout"
  warning; Merge says "adds workouts that aren't already on this phone".
- `exportCsv` loads the workout rows (one query, the same columns as Phase 09's
  `workouts.json`), calls `toWorkoutCsv`, writes the file safely (`.tmp`, then rename, as
  in Phase 09), and shares it with `mimeType: 'text/csv'`.
- Everything uses existing theme components; no new colors.
- `disabled={locked}` stops double taps while something is running.

---

## Tests

| Test | Expected |
|------|----------|
| CSV, raw bytes | Starts with `EF BB BF` |
| CSV with `toLocaleString` patched to use commas | Numbers still use `.` |
| CSV with a comma, quote and line break in notes | Quoted; columns still line up |
| CSV in Excel or Sheets | Right columns, accents correct |
| Merge, empty database | Everything added |
| Merge, no overlap | Both sets there; count is the sum |
| Merge, some overlap | Only new GUIDs added; your rule holds |
| Merge, full overlap | Nothing added, nothing changed |
| Totals and records after any import | Calculated again, never imported |

## Done when

- [ ] The CSV opens correctly in a spreadsheet, with accents and decimals right
- [ ] All four merge cases pass
- [ ] Totals and records are calculated after any import, never imported
- [ ] The Merge/Replace choice and CSV button look right in light and dark mode, using
      only existing theme pieces
