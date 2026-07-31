# Phase 15 — Backup Polish (optional)

> Only if Phases 00–14 are done **and the app is in daily use.** If it isn't in daily
> use, this phase is speculative work on a product nobody has proven they want yet.
> See `../README.md` for the collaboration model — you write the logic, the agent
> writes the UI. This phase has a small UI surface: a Merge/Replace choice on import,
> and a CSV export button next to Phase 09's existing ZIP export.

**Hardware:** none · **Size:** M · **Blocked by:** Phase 09 (Backup — minimal). Needs
`WorkoutId` and the single-ZIP export/import flow that phase already ships.

---

## Goal

Make backups more useful without weakening any of Phase 09's safety guarantees:
a human-readable CSV export for spreadsheets, a merge import mode that doesn't
require wiping the phone's existing history first, and the plumbing for backup
format migrations that don't have a real case to handle yet.

## Learning goals

- **Culture-invariant formatting** — why any number that gets written to a file (as
  opposed to shown on screen) must never depend on the phone's regional settings, and
  what `CultureInfo.InvariantCulture` actually changes about `ToString()`.
- **Byte order marks (BOM)** — what the three bytes `EF BB BF` at the start of a UTF-8
  file are for, why Excel specifically needs them to detect non-ASCII text correctly,
  and how `Encoding.UTF8` in .NET differs from `new UTF8Encoding()` on this exact
  point.
- **SQL conflict resolution** — `INSERT OR IGNORE` as a way to merge two datasets that
  might overlap without hand-writing "does this row already exist" checks yourself.
- **Designing for a version that doesn't exist yet** — the backup format migration
  mechanism ships now, with zero migrations registered, because retrofitting a
  migration *mechanism* after the first breaking format change is much harder than
  building the mechanism before you need it.

## Reference docs

- **`CultureInfo.InvariantCulture`** — https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.invariantculture —
  read this before writing a single `ToString()` call in the CSV exporter. The
  invariant culture is culture-*independent*, not culture-*neutral*-English — it's
  what "just give me the bytes, no locale opinions" means in .NET.
- **`Encoding.UTF8` property** — https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.utf8 —
  the Remarks section spells out the exact trap: `Encoding.UTF8` **does** include a
  BOM in its preamble; a `new UTF8Encoding()` constructed with the default (parameterless)
  constructor does not. `StreamWriter`'s own default encoding also omits the BOM. This
  is the one-line difference behind "why did the BOM not show up" bugs.
- **SQLite `ON CONFLICT` clause** — https://sqlite.org/lang_conflict.html — the
  official spec for `INSERT OR IGNORE` (and the other four conflict-resolution
  keywords). Read the IGNORE section specifically: it silently skips the *violating
  row only* and continues, which is exactly the semantics merge-by-`WorkoutId` wants.
- **`Microsoft.Data.Sqlite` overview** and **data types** — already in
  `../../docs/learning/03-Doc-Links.md` under "Data / SQLite" — reuse those links for
  the parameterized-command mechanics; nothing new there for this phase beyond the
  `ON CONFLICT` syntax above.
- **Dependency injection in .NET MAUI** — already in `03-Doc-Links.md` — if the CSV
  exporter or migration runner need registering as services, same pattern as every
  prior phase's DI registrations.

---

## Task A — CSV export of workout summaries

**Export-only, not round-trippable — say so in the UI** (task D below). Two
correctness requirements that are easy to miss and annoying to discover later:

- Write a **UTF-8 BOM**, or Excel mangles non-ASCII.
- Use invariant culture for numbers, with a documented note: Excel parses CSV using
  the OS list separator, so comma-decimal locales will split `12.5` into two columns.

### Concrete steps

1. Create `src/MyHi.Companion.Core/Backup/WorkoutCsvExporter.cs` — `Core` project, not
   the app project: this is pure data formatting with no MAUI dependency, same
   reasoning as why `FtmsCommands`/the protocol parsers live in `Core` (see Phase 01b).
2. Decide the column set from the `Workout` table (`../../14-Database.md`). A
   reasonable summary row: `WorkoutId, StartedAtUtc, DurationSeconds, DistanceMeters,
   Calories, AvgSpeedKph, MaxSpeedKph, AvgHeartRate, MaxHeartRate, Notes`. **Never
   export the integer `Id`** — same rule as the ZIP backup in Phase 09, it means a
   different workout on every phone.
3. Implement against this shape — not the full thing, just enough that you're not
   staring at a blank file:

   ```csharp
   namespace MyHi.Companion.Core.Backup;

   public static class WorkoutCsvExporter
   {
       // Writes one row per workout. Caller supplies the already-loaded list —
       // this class does no database I/O, only formatting. Keeping it a pure
       // function of (workouts, stream) is what makes it testable without a
       // real database in the test project.
       public static void WriteCsv(Stream destination, IReadOnlyList<WorkoutSummary> workouts)
       {
           // TODO: write the UTF-8 BOM first. Encoding.UTF8 (the static
           // property, not `new UTF8Encoding()`) already carries the BOM in
           // its preamble — see the doc link above for exactly why those two
           // are different.

           // TODO: write the header row — the column list decided above,
           // comma-separated.

           // TODO: one row per workout. Every numeric field must go through
           // CultureInfo.InvariantCulture, e.g.
           //     workout.DistanceMeters.ToString("F2", CultureInfo.InvariantCulture)
           // never the culture-sensitive ToString() overload — that's the bug
           // this whole task exists to prevent.

           // TODO: any text field that could contain a comma, quote, or
           // newline (Notes is the only one here) needs CSV quoting per
           // RFC 4180 — decide the escaping rule now, don't bolt it on after
           // an export breaks on someone's first multi-line note.
       }
   }
   ```
4. Write `src/MyHi.Companion.Tests/Backup/WorkoutCsvExporterTests.cs`:
   - Assert the first three bytes of the output are `0xEF 0xBB 0xBF`.
   - Assert a workout with `DistanceMeters = 12.5` produces the literal text `12.5`
     in the output **even when the test temporarily sets
     `CultureInfo.CurrentCulture` to `de-DE`** (save the original culture, set it,
     run the export, assert, then restore it — a test that leaves global culture
     state mutated will intermittently break unrelated tests that run after it).
   - Assert a `Notes` value containing a comma is quoted correctly and doesn't shift
     later columns when the output is split naively on `,`.
5. Verify manually: run the export against real data, open the file in a plain text
   editor and confirm the header line looks correct (not prefixed with visible junk
   characters — that would mean the BOM landed wrong), then, if you have access to a
   machine set to a comma-decimal locale (or can temporarily change Windows' regional
   format), open it in Excel and confirm decimal values land in one column, not two.

## Task B — Merge import mode

Now cheap, because `WorkoutId` exists — `INSERT OR IGNORE` on the GUID.

Still needs a **4-case test matrix**: empty / disjoint / partial overlap / full
overlap. And defined conflict semantics for same-GUID-different-data — decide and
document, do not leave it to whichever row SQLite happens to keep.

**Never de-duplicate on the integer `Id`.** The same integer means different workouts
on two phones.

### Concrete steps

1. Touches: wherever Phase 09's importer lives (e.g.
   `src/MyHi.Companion.Core/Backup/BackupImporter.cs`) plus a merge-specific SQL path
   alongside it.
2. Add the mode as an explicit enum, not a bool — a `bool merge` parameter reads fine
   at the call site today and ambiguous in six months:

   ```csharp
   public enum ImportMode
   {
       Replace,
       Merge
   }

   public async Task<ImportResult> ImportAsync(
       BackupArchive archive,
       ImportMode mode,
       CancellationToken ct = default)
   {
       // TODO: Replace is Phase 09's existing path — unchanged by this phase.
       // TODO: Merge follows the *same* safety rules Phase 09 established for
       // Replace: pre-import safety backup first, the whole import inside one
       // SQLite transaction, settings applied only after that transaction
       // commits. "Merge feels less destructive than Replace" is not a reason
       // to relax any of those three rules — a merge that partially applies
       // on a mid-import crash is exactly as bad as a partial replace.
   }
   ```
3. The merge SQL shape — one statement per imported `Workout` row, inside the same
   transaction as everything else:

   ```sql
   -- TODO: insert only if this WorkoutId isn't already present. IGNORE skips
   -- just the conflicting row and keeps going — see the ON CONFLICT doc link
   -- above for why that's the right primitive here instead of a manual
   -- SELECT-then-INSERT per row.
   INSERT OR IGNORE INTO Workout
       (WorkoutId, StartedAtUtc, EndedAtUtc, StartOffsetMinutes, DurationSeconds,
        DistanceMeters, Calories, AvgSpeedKph, MaxSpeedKph, AvgHeartRate,
        MaxHeartRate, Status, DeviceId, Notes, CreatedAtUtc, UpdatedAtUtc)
   VALUES
       ($workoutId, $startedAtUtc, $endedAtUtc, $startOffsetMinutes, $durationSeconds,
        $distanceMeters, $calories, $avgSpeedKph, $maxSpeedKph, $avgHeartRate,
        $maxHeartRate, $status, $deviceId, $notes, $createdAtUtc, $updatedAtUtc);

   -- TODO: decide what happens to WorkoutSample rows for a WorkoutId that
   -- already existed on this phone but carries *different* header data in
   -- the imported file (the "same-GUID-different-data" case). Pick one rule
   -- — e.g. "existing data wins; samples for an already-present WorkoutId
   -- are never imported" — and write the decision as a comment right next
   -- to this query, not just in your head. Whatever you pick, it needs a
   -- test in the matrix below.
   ```
4. Write the 4-case test matrix as real xUnit tests against a temp SQLite file —
   follow `src/MyHi.Companion.Tests/Data/MigrationRunnerTests.cs`'s pattern (a fresh
   temp-file database per test) rather than inventing a new fixture style:
   - **Empty**: importing into a database with zero existing workouts — every
     imported row lands, count after equals count in the file.
   - **Disjoint**: existing workouts and imported workouts share no `WorkoutId` —
     both sets survive, count after equals the sum.
   - **Partial overlap**: some `WorkoutId`s exist on both sides — only the new ones
     get inserted, count after equals existing-count plus new-only-count, and your
     documented same-GUID-different-data rule holds for the overlapping ones.
   - **Full overlap**: every imported `WorkoutId` already exists — zero rows
     inserted, count after equals count before, nothing on the existing rows changes.
5. Statistics and personal records are **derived, not imported** — after either
   Replace or Merge, recompute them from the `Workout`/`WorkoutSample` tables now on
   the phone. Never write imported statistics/PR values directly; that creates a
   second source of truth that can silently drift from the data it's supposed to
   summarize.

## Task C — Backup format migration

For older `backupFormatVersion` values, **once such versions actually exist.** The
mechanism shipped in Phase 09 (the manifest already carries an integer
`backupFormatVersion`, independent of the app version); this task is the migration
*runner* itself, with zero real migrations registered yet.

### Concrete steps

1. Touches: wherever Phase 09's manifest reader lives (e.g.
   `src/MyHi.Companion.Core/Backup/BackupManifest.cs`), plus a new
   `src/MyHi.Companion.Core/Backup/BackupMigrations.cs`.
2. Shape it as a small ordered chain, mirroring how `MigrationRunner.cs` already
   handles the *database* schema — same idea, applied to backup JSON instead of
   SQLite DDL:

   ```csharp
   public interface IBackupMigration
   {
       int FromVersion { get; }
       int ToVersion { get; }

       // TODO: mutate the parsed manifest/workouts/samples JSON in place (or
       // return a transformed copy — pick one and be consistent) so the
       // result matches ToVersion's shape.
       void Apply(JsonNode manifest, JsonNode workouts, JsonNode samples);
   }

   public static class BackupMigrations
   {
       // TODO: empty for now — this phase ships the mechanism, not a
       // migration, because backupFormatVersion has only ever had one value
       // so far. Add entries here the day a real format change happens, not
       // before — a migration written against a guessed future format is a
       // migration written against the wrong format.
       private static readonly IReadOnlyList<IBackupMigration> _migrations = [];

       public static (JsonNode manifest, JsonNode workouts, JsonNode samples) Upgrade(
           int fromVersion, JsonNode manifest, JsonNode workouts, JsonNode samples)
       {
           // TODO: walk _migrations in order from fromVersion to the current
           // version, applying each one. If fromVersion is already current,
           // this is a no-op. If fromVersion is *higher* than current, that's
           // Phase 09's existing "created by a newer version" error path —
           // don't duplicate that check here.
       }
   }
   ```
3. No tests beyond a trivial "current version in, current version out, unchanged"
   case belong here yet — writing tests against a migration for a format version that
   doesn't exist is testing a guess. Add real migration tests the day a real old
   version shows up.

## Task D — UI: Merge/Replace choice and CSV export button

Full XAML, using only the monochrome theme's tokens and keyed styles from
`../../docs/learning/04-Monochrome-Theme.md` — paste this into wherever Phase 09's
backup screen lives (e.g. `Features/Backup/BackupPage.xaml`), next to the existing
ZIP export button, and wire the bindings to your ViewModel's actual property/command
names.

```xml
<!-- Export section — CSV button added next to Phase 09's existing ZIP export -->
<Border Padding="16,12">
    <VerticalStackLayout Spacing="12">
        <Label Text="Export" Style="{StaticResource SubHeadline}" />

        <Label
            Text="CSV is a summary only — it can't be imported back into the app. Use ZIP export and import for a full, restorable backup."
            Style="{StaticResource Caption}" />

        <HorizontalStackLayout Spacing="12">
            <Button Text="Export ZIP" Command="{Binding ExportZipCommand}" />
            <Button
                Text="Export CSV"
                Style="{StaticResource SecondaryButton}"
                Command="{Binding ExportCsvCommand}" />
        </HorizontalStackLayout>
    </VerticalStackLayout>
</Border>

<!-- Import section — Merge/Replace choice -->
<Border Padding="16,12" Margin="0,12,0,0">
    <VerticalStackLayout Spacing="12">
        <Label Text="Import" Style="{StaticResource SubHeadline}" />

        <Label
            Text="Merge keeps everything already on this phone and adds any new workouts from the file. Replace deletes this phone's history first, then restores from the file."
            Style="{StaticResource Caption}" />

        <VerticalStackLayout Spacing="4">
            <RadioButton
                Content="Merge (recommended)"
                GroupName="ImportMode"
                IsChecked="{Binding IsMergeSelected}" />
            <RadioButton
                Content="Replace — deletes existing data first"
                GroupName="ImportMode"
                IsChecked="{Binding IsReplaceSelected}" />
        </VerticalStackLayout>

        <Button Text="Choose Backup File…" Command="{Binding ImportCommand}" />
    </VerticalStackLayout>
</Border>
```

Notes on wiring this up:

- `IsMergeSelected` / `IsReplaceSelected` are two boolean properties on your
  ViewModel (`[ObservableProperty]` from `CommunityToolkit.Mvvm` works here, same
  pattern as every other bindable property in this project — see the MVVM source
  generators link in `03-Doc-Links.md`). `RadioButton`'s `GroupName` handles the
  mutual exclusivity in the UI; in the ViewModel, translate whichever one is `true`
  into Task B's `ImportMode` enum when `ImportCommand` runs.
- Everything above resolves through the existing implicit styles in `Styles.xaml` —
  `Border` already gets `ColorSurface*`/`ColorBorder*`, `Button`/`RadioButton`/`Label`
  already get their themed colors, `SubHeadline` and `Caption` and `SecondaryButton`
  are the same keyed styles used elsewhere in the app. Nothing here is an inline
  color or a new resource key.
- If a disabled/in-progress state is needed for these buttons while an export or
  import is running (recommended — prevents double-tapping), bind `IsEnabled` to a
  `!IsBusy`-shaped property; the `Button` style's built-in `Disabled` visual state
  already handles the dimmed appearance, no extra XAML needed for that part.

---

## Tests

| Test | Expected |
|------|----------|
| CSV export, then inspect raw bytes | First three bytes are `EF BB BF` |
| CSV export with `CurrentCulture` set to `de-DE` mid-test | Numeric fields still use `.` as the decimal separator |
| CSV export with a comma in `Notes` | Field is quoted; column count after a naive split on `,` is still correct |
| CSV opens in Excel, comma-decimal locale | Decimal values stay in one column, non-ASCII text intact |
| Merge import — empty database | All imported workouts inserted |
| Merge import — disjoint sets | Both sets present, count is the sum |
| Merge import — partial overlap | Only new `WorkoutId`s inserted; documented same-GUID rule holds |
| Merge import — full overlap | Zero rows inserted, nothing changed |
| Statistics/PRs after any import (Merge or Replace) | Recomputed from `Workout`/`WorkoutSample`, never imported directly |

## Acceptance

- [ ] CSV opens correctly in Excel with non-ASCII text and decimal values, in a
      comma-decimal locale
- [ ] All four merge cases pass
- [ ] Statistics and PRs recomputed after any import, never imported
- [ ] Merge/Replace choice and CSV export button render correctly in both light and
      dark theme, using only existing `Colors.xaml`/`Styles.xaml` tokens
