# Phase 08 — Settings

**Hardware:** none · **Size:** S · **Blocked by:** Phase 07

---

## Goal

User preferences, persisted.

## Storage decision

Scalar settings live in **MAUI `Preferences`** (Android `SharedPreferences`), not
SQLite: synchronous access, no async ceremony in ViewModels, no database hit at startup
for six booleans.

**Exception:** *Saved Devices* is a collection with a lifecycle and lives in SQLite.

| Setting | Store | Default |
|---------|-------|---------|
| Auto-reconnect | Preferences | true |
| Keep screen awake during workout | Preferences | true |
| Dark mode | Preferences | system |
| Units (metric / imperial) | Preferences | metric |
| Voice announcements | Preferences | off |
| Dashboard layout | Preferences (JSON string) | default |
| Saved devices | SQLite | — |

## The one rule that matters

**Store metric always; convert at display time only.** An imperial toggle that writes
converted values corrupts the dataset permanently and there is no way back.

## Tests

- Change every setting, force-stop, relaunch — all persist
- Units toggle converts displayed values without touching stored data
- Round-trip a value through imperial and back; the stored bytes are unchanged

## Acceptance

- [ ] All settings survive force-stop
- [ ] No unit conversion ever touches the database
