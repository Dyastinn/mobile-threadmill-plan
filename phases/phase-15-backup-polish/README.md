# Phase 15 — Backup Polish (optional)

> Only if Phases 00–14 are done **and the app is in daily use.** If it isn't in daily
> use, this phase is speculative work on a product nobody has proven they want yet.

**Hardware:** none · **Size:** M

---

## CSV export of workout summaries

**Export-only, not round-trippable — say so in the UI.** Two correctness requirements
that are easy to miss and annoying to discover later:

- Write a **UTF-8 BOM**, or Excel mangles non-ASCII.
- Use invariant culture for numbers, with a documented note: Excel parses CSV using the
  OS list separator, so comma-decimal locales will split `12.5` into two columns.

## Merge import mode

Now cheap, because `WorkoutId` exists — `INSERT OR IGNORE` on the GUID.

Still needs a **4-case test matrix**: empty / disjoint / partial overlap / full
overlap. And defined conflict semantics for same-GUID-different-data — decide and
document, do not leave it to whichever row SQLite happens to keep.

**Never de-duplicate on the integer `Id`.** The same integer means different workouts
on two phones.

## Backup format migration

For older `backupFormatVersion` values, **once such versions actually exist.** The
mechanism shipped in Phase 09; this is the migrations themselves.

## Acceptance

- [ ] CSV opens correctly in Excel with non-ASCII text and decimal values, in a
      comma-decimal locale
- [ ] All four merge cases pass
- [ ] Statistics and PRs recomputed after any import, never imported
