# Phase 08 — Settings (Flutter track)

**Hardware:** none · **Size:** S · **Blocked by:** Phase 07

## Goal

User preferences, persisted — same split as the original track: scalar
settings in `shared_preferences` (Android `SharedPreferences` underneath,
same as MAUI's `Preferences`), Saved Devices left in the `Device` SQLite
table from Phase 06.

## The concept

Six settings ("dark mode: on") are single values overwritten in place.
Saved Devices is a growing, individually-queried collection. Same reasoning
as the original for keeping them in different stores: a `Settings` table in
SQLite means a schema migration for every new toggle and an async round trip
every time a `Switch` flips, for data that's fundamentally "hold six named
values." A JSON blob for Saved Devices in `shared_preferences` means
deserializing and reserializing the whole list to update one device's
`lastSeenUtc` — exactly the per-row update problem SQLite exists to solve.

**The one rule that matters**, unchanged: store metric always, convert at
display time only. No screen in this phase writes a converted value back to
storage.

## The seam: `PreferencesStore`

Same pattern as `TreadmillService`: `shared_preferences` is a platform API
that shouldn't leak into `myhi_companion_core`, so a tiny interface stands
between the pure setting logic and the real store — testable with `dart
test`, no platform channel involved.

```dart
// myhi_companion_core/lib/settings/preferences_store.dart
abstract interface class PreferencesStore {
  bool getBool(String key, bool defaultValue);
  Future<void> setBool(String key, bool value);
  String getString(String key, String defaultValue);
  Future<void> setString(String key, String value);
}
```

```dart
// myhi_companion/lib/features/settings/shared_preferences_store.dart
class SharedPreferencesStore implements PreferencesStore {
  final SharedPreferences _prefs;
  SharedPreferencesStore(this._prefs);

  @override
  bool getBool(String key, bool defaultValue) => _prefs.getBool(key) ?? defaultValue;
  @override
  Future<void> setBool(String key, bool value) => _prefs.setBool(key, value);
  @override
  String getString(String key, String defaultValue) => _prefs.getString(key) ?? defaultValue;
  @override
  Future<void> setString(String key, String value) => _prefs.setString(key, value);
}
```

```dart
// myhi_companion_core/test/settings/fake_preferences_store.dart
class FakePreferencesStore implements PreferencesStore {
  final _values = <String, Object>{};
  @override
  bool getBool(String key, bool defaultValue) => (_values[key] as bool?) ?? defaultValue;
  @override
  Future<void> setBool(String key, bool value) async => _values[key] = value;
  @override
  String getString(String key, String defaultValue) => (_values[key] as String?) ?? defaultValue;
  @override
  Future<void> setString(String key, String value) async => _values[key] = value;
}
```

## `AppSettingsService`

```dart
// myhi_companion_core/lib/settings/app_settings_service.dart
enum AppThemePreference { system, light, dark }
enum MeasurementUnits { metric, imperial }

class AppSettingsService {
  final PreferencesStore _store;
  AppSettingsService(this._store);

  static const _autoReconnectKey = 'settings.auto_reconnect';
  static const _themeKey = 'settings.theme';
  static const _unitsKey = 'settings.units';
  // ... keepScreenAwake, voiceAnnouncements, dashboardLayout keys, same shape

  bool get autoReconnect => _store.getBool(_autoReconnectKey, true);
  set autoReconnect(bool v) => _store.setBool(_autoReconnectKey, v);

  AppThemePreference get theme {
    final raw = _store.getString(_themeKey, AppThemePreference.system.name);
    return AppThemePreference.values.firstWhere(
      (e) => e.name == raw,
      orElse: () => AppThemePreference.system, // never throw on a corrupted value
    );
  }
  set theme(AppThemePreference v) => _store.setString(_themeKey, v.name);

  MeasurementUnits get units {
    final raw = _store.getString(_unitsKey, MeasurementUnits.metric.name);
    return MeasurementUnits.values.firstWhere(
      (e) => e.name == raw,
      orElse: () => MeasurementUnits.metric,
    );
  }
  set units(MeasurementUnits v) => _store.setString(_unitsKey, v.name);

  // TODO: keepScreenAwake, voiceAnnouncements (bool, same shape as
  // autoReconnect), dashboardLayout (String, same shape as theme/units but
  // no enum — a plain string like "Default"/"Compact"/"Detailed")
}
```

`Enum.values.firstWhere(..., orElse: ...)` is this track's
`Enum.TryParse`-with-fallback: a future app version writing a value this one
doesn't recognize falls back to the default instead of throwing, same rule
as the original.

## `SettingsNotifier` and screen

```dart
final settingsProvider = NotifierProvider<SettingsNotifier, SettingsUiState>(SettingsNotifier.new);

class SettingsNotifier extends Notifier<SettingsUiState> {
  @override
  SettingsUiState build() {
    final settings = ref.watch(appSettingsServiceProvider);
    return SettingsUiState(
      autoReconnect: settings.autoReconnect,
      theme: settings.theme,
      units: settings.units,
    );
  }

  void setAutoReconnect(bool value) {
    ref.read(appSettingsServiceProvider).autoReconnect = value;
    state = state.copyWith(autoReconnect: value);
  }

  void setTheme(AppThemePreference value) {
    ref.read(appSettingsServiceProvider).theme = value;
    state = state.copyWith(theme: value);
  }
  // ... setUnits, setKeepScreenAwake, etc. — same one-line "write straight
  // through, then update local state" shape
}
```

```dart
class SettingsScreen extends ConsumerWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = ref.watch(settingsProvider);
    final notifier = ref.read(settingsProvider.notifier);
    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: ListView(children: [
        SwitchListTile(
          title: const Text('Auto-reconnect'),
          subtitle: const Text('Reconnect automatically after a dropped connection'),
          value: s.autoReconnect,
          onChanged: notifier.setAutoReconnect,
        ),
        ListTile(
          title: const Text('Dark mode'),
          trailing: DropdownButton<AppThemePreference>(
            value: s.theme,
            items: AppThemePreference.values
                .map((t) => DropdownMenuItem(value: t, child: Text(t.name)))
                .toList(),
            onChanged: (v) => v != null ? notifier.setTheme(v) : null,
          ),
        ),
        // Units, dashboard layout, keep-screen-awake, voice announcements —
        // same SwitchListTile/DropdownButton pattern.
      ]),
    );
  }
}
```

`SwitchListTile`/`DropdownButton` are this track's `Switch`/`Picker` with
two-way binding built into the widget itself — no separate `IsToggled="{Binding ...}"`
markup, the `value`/`onChanged` pair *is* the binding.

## Tests

- Fresh `AppSettingsService` over an empty `FakePreferencesStore` returns
  every documented default.
- `units = imperial` then read back returns `imperial`; set back to
  `metric`, reads `metric`.
- Write an unrecognized string directly into the fake store under the theme
  key, read `AppSettingsService.theme` through a *new* instance, assert it
  comes back `system`, not a thrown error.
- `[HUMAN]`: change every setting, force-stop, relaunch — all persist.

## Acceptance

- [ ] All settings survive force-stop
- [ ] No unit conversion ever touches the database
- [ ] `AppSettingsService` tests and every prior phase's tests pass
