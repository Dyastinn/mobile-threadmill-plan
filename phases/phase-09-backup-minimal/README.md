# Phase 09 — Backup (minimal)

> Deliberately scoped down. Merge mode, CSV, and version migration are Phase 15. This
> phase exists to make data loss survivable, nothing more.

**Hardware:** none · **Size:** M · **Blocked by:** Phase 08

---

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
exported** and are reassigned on import.

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

## Tests

| Test | Expected |
|------|----------|
| Export with 50 workouts | ZIP created, opens, contains all five files |
| Export → wipe → import | Row counts match **and** SHA-256 of a canonically-ordered dump matches |
| Kill app mid-import | No data loss; pre-import backup recoverable |
| Import corrupt ZIP | Clear error, existing data untouched |
| Import archive with `../` entry | Rejected |
| Import backup with newer format version | Clear "created by a newer version" message |
| Export during active workout | Blocked with explanation |
| Auto-backup after 6 workouts | Exactly 5 files retained, oldest rotated out |

## Acceptance

- [ ] The export→wipe→import **checksum** test passes. "No data loss" is measured by
      checksum, not by eyeballing a list
- [ ] No test leaves the database in a partial state
