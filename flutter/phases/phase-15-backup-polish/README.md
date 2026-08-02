# Phase 15 — Backup Polish (optional) (Flutter track)

> Only if Phases 00–14 are done **and the app is in daily use.** If it
> isn't, this phase is speculative work on a product nobody has proven they
> want yet.

**Hardware:** none · **Size:** M · **Blocked by:** Phase 09. Needs `workoutId`
and the single-ZIP export/import flow that phase already ships.

## Goal

Make backups more useful without weakening Phase 09's safety guarantees: a
human-readable CSV export, a merge import mode that doesn't require wiping
existing history first, and the plumbing for backup format migrations that
don't have a real case to handle yet.

## The concept

Merging two workout histories only works if every entry carries a global
identity, not just a local one — a `workoutId` GUID, stamped once per
workout back in Phase 06, already answers "is this the same workout on both
phones, or two different ones?" by construction. Merge wasn't built in
Phase 09 because nobody needed it yet (Replace was the correct minimal
answer to a feature nobody had asked to use); it's cheap now because the one
piece of infrastructure it needs — global identity — was already paid for,
for reasons that had nothing to do with backups. Same "cheap seam now,
expensive guesswork deferred" principle Task C below applies again, in the
opposite direction: ship the migration *mechanism*, write zero actual
migrations, because a migration written against a guessed future format is
a migration written against the wrong format.

## Task A — CSV export of workout summaries

**Export-only, not round-trippable — say so in the UI** (Task D).

**A genuine Dart simplification worth naming.** The original track's biggest
CSV pitfall was culture-sensitive number formatting: C#'s `double.ToString()`
without an explicit `CultureInfo.InvariantCulture` argument uses the
*current thread's* culture, so the same code silently emits `12,5` instead
of `12.5` on a comma-decimal-locale device. **Dart's `double.toString()` has
no such trap** — it's always culture-invariant; Dart's core number
formatting never consults the OS locale unless you explicitly reach for the
`intl` package's `NumberFormat`. This whole bug class the original track
spent a learning goal on doesn't apply here by default. What *does* still
apply, because it's an Excel behaviour, not a Dart/C# one: Excel itself
parses CSV using the OS's configured list separator, so a comma-decimal
locale still splits `12.5` into two columns unless you handle it (documented
in the UI, per Task D, same as the original).

The UTF-8 BOM requirement is unchanged — Excel needs it to detect non-ASCII
text correctly, and it's an Excel/file-format fact, not a language one.

### Concrete steps

1. Create `myhi_companion_core/lib/backup/workout_csv_exporter.dart` — pure
   data formatting, no Flutter dependency, same reasoning as everything
   else that lives in `myhi_companion_core`.
2. Column set, same as the original: `workoutId, startedAtUtc,
   durationSeconds, distanceMeters, calories, avgSpeedKph, maxSpeedKph,
   avgHeartRate, maxHeartRate, notes`. **Never export the integer `id`.**
3. Shape:
   ```dart
   class WorkoutCsvExporter {
     static Uint8List writeCsv(List<WorkoutSummary> workouts) {
       final buffer = StringBuffer();
       buffer.writeln('workoutId,startedAtUtc,durationSeconds,distanceMeters,'
           'calories,avgSpeedKph,maxSpeedKph,avgHeartRate,maxHeartRate,notes');
       for (final w in workouts) {
         // TODO: one row per workout. w.distanceMeters.toStringAsFixed(2) is
         // already culture-invariant — no CultureInfo argument needed,
         // unlike the original track's ToString("F2", CultureInfo.InvariantCulture).
         // TODO: CSV-quote `notes` per RFC 4180 if it contains a comma,
         // quote, or newline — decide the escaping rule now.
       }
       // UTF-8 BOM (EF BB BF) first, then the encoded text — Dart's plain
       // utf8.encode() does NOT add a BOM (same omit-by-default behavior as
       // .NET's own StreamWriter default, per the original track's own
       // caveat), so prepend the three bytes explicitly:
       return Uint8List.fromList([0xEF, 0xBB, 0xBF, ...utf8.encode(buffer.toString())]);
     }
   }
   ```
4. Tests: assert the first three bytes are `0xEF 0xBB 0xBF`; assert a
   workout with `distanceMeters = 12.5` produces the literal text `12.5`
   (no culture-mutation test needed here the way the original's C#-specific
   test was, precisely because Dart has nothing to mutate — worth a
   one-line comment noting *why* that test is absent, not just omitting it
   silently); assert a `notes` value containing a comma is quoted correctly.

## Task B — Merge import mode

Cheap because `workoutId` exists: `sqflite`'s `ConflictAlgorithm.ignore` is
this track's `INSERT OR IGNORE` — skip just the conflicting row, keep going.

**Never de-duplicate on the integer `id`.** The same integer means a
different workout on two phones.

### Concrete steps

1. Add the mode as an explicit enum, not a bool — a `bool merge` parameter
   reads fine today and ambiguous in six months:
   ```dart
   enum ImportMode { replace, merge }

   Future<ImportResult> importBackup(BackupArchive archive, ImportMode mode) async {
     // TODO: replace is Phase 09's existing path, unchanged.
     // TODO: merge follows the SAME safety rules Phase 09 established for
     // replace — pre-import safety backup first, the whole import inside
     // one sqflite transaction, settings applied only after that
     // transaction's callback returns. "Merge feels less destructive" is
     // not a reason to relax any of those three rules.
     throw UnimplementedError();
   }
   ```
2. The merge insert, one call per imported `Workout` row, inside the same
   transaction as everything else:
   ```dart
   await txn.insert('Workout', workoutMap, conflictAlgorithm: ConflictAlgorithm.ignore);
   ```
3. Decide and document what happens to `WorkoutSample` rows for a
   `workoutId` that already existed on this phone but carries *different*
   header data in the imported file. Pick one rule (e.g. "existing data
   wins; samples for an already-present workoutId are never imported") and
   write it as a comment next to the query, not just in your head.
4. Write the same 4-case test matrix as the original: empty, disjoint,
   partial overlap, full overlap — against a temp `sqflite` file, following
   the same fresh-temp-database-per-test pattern Phase 06's migration tests
   already established.
5. Statistics and personal records are **derived, not imported** — recompute
   from `Workout`/`WorkoutSample` after either mode, never write imported
   statistic values directly.

## Task C — Backup format migration

The mechanism, zero real migrations, same reasoning as the original — it
shipped in Phase 09 as an integer `backupFormatVersion` in the manifest.

```dart
// myhi_companion_core/lib/backup/backup_migrations.dart
abstract interface class BackupMigration {
  int get fromVersion;
  int get toVersion;
  void apply(Map<String, dynamic> manifest, List<dynamic> workouts, List<dynamic> samples);
}

class BackupMigrations {
  // Empty for now — backupFormatVersion has only ever had one value.
  // Add entries here the day a real format change happens, not before.
  static const List<BackupMigration> _migrations = [];

  static ({Map<String, dynamic> manifest, List<dynamic> workouts, List<dynamic> samples}) upgrade(
    int fromVersion,
    Map<String, dynamic> manifest,
    List<dynamic> workouts,
    List<dynamic> samples,
  ) {
    // TODO: walk _migrations in order from fromVersion to current, applying
    // each. No-op if fromVersion is already current. A fromVersion higher
    // than current is Phase 09's existing "created by a newer version"
    // error path — don't duplicate that check here.
    throw UnimplementedError();
  }
}
```

No tests beyond a trivial "current version in, current version out,
unchanged" case belong here yet — testing a migration for a format that
doesn't exist is testing a guess.

## Task D — UI: Merge/Replace choice, CSV export button

```dart
Column(children: [
  Text('Export', style: Theme.of(context).textTheme.titleMedium),
  Text(
    "CSV is a summary only — it can't be imported back into the app. "
    'Use ZIP export and import for a full, restorable backup.',
    style: Theme.of(context).textTheme.bodySmall,
  ),
  Row(children: [
    FilledButton(onPressed: exportZip, child: const Text('Export ZIP')),
    const SizedBox(width: 12),
    OutlinedButton(onPressed: exportCsv, child: const Text('Export CSV')),
  ]),
  const Divider(height: 32),
  Text('Import', style: Theme.of(context).textTheme.titleMedium),
  Text(
    'Merge keeps everything already on this phone and adds any new '
    'workouts from the file. Replace deletes this phone’s history '
    'first, then restores from the file.',
    style: Theme.of(context).textTheme.bodySmall,
  ),
  RadioListTile<ImportMode>(
    title: const Text('Merge (recommended)'),
    value: ImportMode.merge,
    groupValue: selectedMode,
    onChanged: onModeChanged,
  ),
  RadioListTile<ImportMode>(
    title: const Text('Replace — deletes existing data first'),
    value: ImportMode.replace,
    groupValue: selectedMode,
    onChanged: onModeChanged,
  ),
  FilledButton(onPressed: pickAndImport, child: const Text('Choose Backup File…')),
])
```

`RadioListTile` with a shared `groupValue` is this track's `RadioButton`
`GroupName` mutual-exclusivity mechanism.

## Tests

Same table as the original: CSV BOM check, CSV numeric-format check
(simplified, per Task A's note — no culture-mutation needed), CSV comma-in-notes
quoting, the 4-case merge matrix, and "statistics/PRs recomputed after any
import, never imported."

## Acceptance

- [ ] CSV opens correctly in Excel with non-ASCII text and decimal values
- [ ] All four merge cases pass
- [ ] Statistics and PRs recomputed after any import, never imported
- [ ] Merge/Replace choice and CSV export button render correctly in light and dark
