# Phase 08 — Settings

**Treadmill needed:** no · **Size:** S · **Waits for:** Phase 07

---

## Goal

Settings that are saved and remembered.

### The idea

**Two kinds of saved data, two places.** The six settings ("dark mode", "units") are
single values that just get replaced. Saved devices are a list of records that each get
added, changed and removed. So settings go in a simple key/value store
(`expo-sqlite/kv-store`), and devices stay in the database's `Device` table from Phase 06.

**Why not put everything in the database?** Then each new setting needs a migration, and
reading a switch's value needs an async database call. Too much ceremony for six values.

**Why not put the devices in the key/value store too, as one JSON list?** Then changing
one device means rewriting the whole list, and two changes at the same time can
overwrite each other. That's exactly what a database exists to prevent.

**The interface again.** `core/` can't import the key/value store, so the settings
logic talks to a tiny `PreferencesStore` interface (task 8.1), the same trick as
`TreadmillService`. That lets the logic ("a garbage value falls back to the default,
never crashes") be tested on the PC. It costs one interface and one small wrapper.

There's no such interface for the `Device` table in this phase, because it already goes
through the `Db` interface. Only add an interface where something actually needs to be
swapped or faked.

## What you'll learn

- **The interface trick again**, one level down.
- **Controlled inputs**: a `Switch` shows the value from your state, and its
  `onValueChange` saves the new one. Nothing changes on screen until your code says so.
  That's how every input works in React, and why "save as soon as it changes" is two
  lines here.
- **Settings as fixed lists of strings**, and safely checking that a saved string is one
  of them (a "type guard"). That's what "fall back, never crash" looks like in
  TypeScript.
- Saving the moment you flip a switch, with no "Save" button. On purpose.

## Technology decision: `expo-sqlite/kv-store` for settings

**Problem.** A few single values need to survive restarts, and be readable instantly
while a screen is drawing, with no loading state.

**Why this.** It's part of `expo-sqlite`, which the app already has, so nothing new to
install. It has instant (synchronous) reads and writes, and it keeps its data separate
from the workout database. Phase 00 and Phase 02 already use it.

**Alternatives.**

1. **`react-native-mmkv`.** Good: the fastest option, with typed getters. Bad: another
   native library to keep working across upgrades, for six values read a few times a
   minute. Better for apps reading values thousands of times a second.
2. **AsyncStorage.** Good: the long-time standard. Bad: async only, so every read needs a
   loading state, which is the ceremony we're avoiding. Better when everything is async
   anyway.
3. **A `Settings` table in the database.** Good: one storage system. Bad: a migration per
   setting, and async reads. Better if settings ever needed searching, which they won't.

**Why not those.** The first two add a library to replace one we already have. The table
ties six switches to the database.

**Long term.** Maintained by Expo. Because of the interface, switching to MMKV later is
one new file.

**In the code:** `src/features/settings/kvPreferencesStore.ts`.

## The settings

| Setting | Saved in | Default |
|---------|-------|---------|
| Auto-reconnect | kv-store | on |
| Keep screen awake during a workout | kv-store | on |
| Dark mode | kv-store | follow the phone |
| Units (metric / imperial) | kv-store | metric |
| Voice announcements | kv-store | off |
| Dashboard layout | kv-store | "default" |
| Saved devices | database (`Device` table) | — |

Saved devices don't get anything on the Settings screen in this phase; they're listed
for completeness.

## The one rule that matters

**Always save metric. Convert only on screen.** The units setting is only a preference.
A screen showing distance reads it and converts the number it shows. It never saves a
converted value anywhere. An imperial setting that saved converted values would ruin
the data for good.

---

## Read first

- **kv-store**: the "Key-value storage" section of
  https://docs.expo.dev/versions/latest/sdk/sqlite/. Values are strings only, which is
  why the interface below only has string methods.
- **`Switch`**: https://reactnative.dev/docs/switch (`value` + `onValueChange`)
- **`useSyncExternalStore`**: https://react.dev/reference/react/useSyncExternalStore
- **Type guards**: the "Using type predicates" section of
  https://www.typescriptlang.org/docs/handbook/2/narrowing.html
- **Light/dark themes**: https://docs.expo.dev/develop/user-interface/color-themes/ and
  https://reactnative.dev/docs/appearance (only for the optional extra in task 8.5)
- [`04-Monochrome-Theme.md`](../../docs/learning/04-Monochrome-Theme.md): every color
  on the screen comes from here

---

## Tasks

### 8.1 — The interface: `PreferencesStore`

Create `src/core/settings/preferencesStore.ts`. Small enough to type in as is:

```ts
export interface PreferencesStore {
  getString(key: string): string | null;
  setString(key: string, value: string): void;
}

/** In-memory version, for tests. */
export class MemoryPreferencesStore implements PreferencesStore {
  private readonly values = new Map<string, string>();
  getString(key: string): string | null {
    return this.values.get(key) ?? null;
  }
  setString(key: string, value: string): void {
    this.values.set(key, value);
  }
}
```

### 8.2 — `AppSettings` (yours)

Create `src/core/settings/appSettings.ts`:

```ts
export const THEME_PREFERENCES = ['system', 'light', 'dark'] as const;
export type ThemePreference = (typeof THEME_PREFERENCES)[number];

export const MEASUREMENT_UNITS = ['metric', 'imperial'] as const;
export type MeasurementUnits = (typeof MEASUREMENT_UNITS)[number];

export const DASHBOARD_LAYOUTS = ['default', 'compact', 'detailed'] as const;
export type DashboardLayout = (typeof DASHBOARD_LAYOUTS)[number];

const KEYS = {
  autoReconnect: 'settings.autoReconnect',
  keepScreenAwake: 'settings.keepScreenAwake',
  theme: 'settings.theme',
  units: 'settings.units',
  voiceAnnouncements: 'settings.voiceAnnouncements',
  dashboardLayout: 'settings.dashboardLayout',
} as const;

/** true if `value` is one of THEME_PREFERENCES. After this check, TypeScript knows its type. */
export function isThemePreference(value: string | null): value is ThemePreference {
  // TODO
}

export interface SettingsSnapshot {
  readonly autoReconnect: boolean;
  readonly keepScreenAwake: boolean;
  readonly theme: ThemePreference;
  readonly units: MeasurementUnits;
  readonly voiceAnnouncements: boolean;
  readonly dashboardLayout: DashboardLayout;
}

export class AppSettings {
  private snapshot: SettingsSnapshot;
  private readonly changes = createEmitter<void>();

  constructor(private readonly store: PreferencesStore) {
    this.snapshot = this.read();
  }

  getSnapshot = (): SettingsSnapshot => this.snapshot;
  subscribe = (listener: () => void): Unsubscribe => this.changes.subscribe(listener);

  setAutoReconnect(value: boolean): void {
    this.store.setString(KEYS.autoReconnect, String(value));
    this.refresh();
  }

  // TODO: setKeepScreenAwake, setVoiceAnnouncements (same as above)
  // TODO: setTheme, setUnits, setDashboardLayout (same, with a string)

  private read(): SettingsSnapshot {
    return {
      autoReconnect: this.readBool(KEYS.autoReconnect, true),
      // TODO: the other five. For theme: if isThemePreference(saved), use it,
      // otherwise 'system'. Same idea for units and layout.
    };
  }

  private readBool(key: string, fallback: boolean): boolean {
    // TODO: 'true' -> true, 'false' -> false, anything else -> fallback
  }

  private refresh(): void {
    this.snapshot = this.read();
    this.changes.emit(undefined);
  }
}
```

`as const` plus `(typeof X)[number]` gives you a list you can show on screen *and* a
type (`'system' | 'light' | 'dark'`) from one line. They can never get out of sync.

1. Fill in the TODOs, following `setAutoReconnect` and `readBool`.
2. `npx tsc --noEmit` and `npx expo lint`.

### 8.3 — The real store (yours)

Create `src/features/settings/kvPreferencesStore.ts`. It imports an Expo library, so it's
in `features/`:

```ts
import Storage from 'expo-sqlite/kv-store';

import type { PreferencesStore } from '@/core/settings/preferencesStore';

export const kvPreferencesStore: PreferencesStore = {
  getString: (key) => Storage.getItemSync(key),
  // TODO: setString, one line, using Storage.setItemSync
};
```

### 8.4 — Tests

Create `src/core/settings/appSettings.test.ts`:

1. A new `AppSettings` with an empty `MemoryPreferencesStore` returns every default.
2. `setUnits('imperial')`, read it back → `'imperial'`; back to `'metric'` → `'metric'`.
3. Put a junk value straight into the store (`store.setString('settings.theme', 'purple')`),
   make a *new* `AppSettings` with the same store, and expect `theme` to be `'system'`,
   not a crash. A future version might save values this version doesn't know.
4. `getSnapshot()` returns the same object until a setter runs, then a new one; a
   listener is called once per setter call.
5. `npm test`.

### 8.5 — `useSettings` (yours)

Create `src/features/settings/useSettings.ts`.

1. Add `settings: new AppSettings(kvPreferencesStore)` to the services file.
2. The hook returns `useSyncExternalStore(settings.subscribe, settings.getSnapshot)` as
   `snapshot`, plus `settings` itself, so a screen can read values and call setters.
3. Make Phase 02's `ReconnectionManager` respect "auto-reconnect": check
   `settings.getSnapshot().autoReconnect` before starting a retry loop.
4. Optional: switch the app's light/dark mode live. React Native's
   `Appearance.setColorScheme('light' | 'dark')` overrides the phone's setting, and the
   theme follows it automatically. Check the `Appearance` doc for the value that means
   "follow the phone again". Call it from a small effect in the root layout that watches
   `snapshot.theme`. Doing it or not are both fine; just decide.

### 8.6 — The settings screen (given)

New component for the theme, `src/features/shared/theme/SegmentedControl.tsx`, because
three settings are "pick one of a few" and React Native has no built-in picker:

```tsx
import { Pressable, View } from 'react-native';

import { Text } from './Text';
import { radius, spacing } from './tokens';
import { useTheme } from './useTheme';

interface Props<T extends string> {
  readonly options: readonly T[];
  readonly value: T;
  readonly onChange: (value: T) => void;
  readonly label: (value: T) => string;
  readonly accessibilityLabel: string;
}

export function SegmentedControl<T extends string>({ options, value, onChange, label, accessibilityLabel }: Props<T>) {
  const { colors } = useTheme();
  return (
    <View
      accessibilityRole="radiogroup"
      accessibilityLabel={accessibilityLabel}
      style={{ flexDirection: 'row', borderWidth: 1, borderColor: colors.border, borderRadius: radius.md, overflow: 'hidden' }}
    >
      {options.map((option) => {
        const selected = option === value;
        return (
          <Pressable
            key={option}
            onPress={() => onChange(option)}
            accessibilityRole="radio"
            accessibilityState={{ selected }}
            style={{
              minHeight: 48,
              paddingHorizontal: spacing.md,
              justifyContent: 'center',
              backgroundColor: selected ? colors.interactiveBackground : 'transparent',
            }}
          >
            <Text style={{ color: selected ? colors.interactiveText : colors.textPrimary, fontWeight: selected ? '600' : '400' }}>
              {label(option)}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}
```

The selected option is filled and bold, never a different color. The accessibility
props make screen readers say "selected".

`src/features/settings/SettingsScreen.tsx`:

```tsx
import type { ReactNode } from 'react';
import { ScrollView, Switch, View } from 'react-native';

import { useSettings } from './useSettings';
import { DASHBOARD_LAYOUTS, MEASUREMENT_UNITS, THEME_PREFERENCES } from '@/core/settings/appSettings';
import { Screen } from '@/features/shared/theme/Screen';
import { SegmentedControl } from '@/features/shared/theme/SegmentedControl';
import { Surface } from '@/features/shared/theme/Surface';
import { Text } from '@/features/shared/theme/Text';
import { spacing } from '@/features/shared/theme/tokens';
import { useTheme } from '@/features/shared/theme/useTheme';

const titleCase = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

function Row({ title, caption, children }: { title: string; caption?: string; children: ReactNode }) {
  return (
    <Surface style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingHorizontal: 14, paddingVertical: 10 }}>
      <View style={{ flex: 1, gap: 2 }}>
        <Text>{title}</Text>
        {caption !== undefined && <Text variant="caption">{caption}</Text>}
      </View>
      {children}
    </Surface>
  );
}

function SettingSwitch({ value, onChange, label }: { value: boolean; onChange: (v: boolean) => void; label: string }) {
  const { colors } = useTheme();
  return (
    <Switch
      value={value}
      onValueChange={onChange}
      accessibilityLabel={label}
      trackColor={{ false: colors.border, true: colors.interactiveBackground }}
      thumbColor={colors.surface}
    />
  );
}

export function SettingsScreen() {
  const { snapshot: s, settings } = useSettings();

  return (
    <Screen>
      <ScrollView contentContainerStyle={{ padding: spacing.lg, gap: spacing.md }}>
        <Text variant="subheadline">Connection</Text>
        <Row title="Auto-reconnect" caption="Reconnect automatically after a dropped connection">
          <SettingSwitch value={s.autoReconnect} onChange={(v) => settings.setAutoReconnect(v)} label="Auto-reconnect" />
        </Row>
        <Row title="Keep screen awake" caption="Keep the screen on during a workout">
          <SettingSwitch value={s.keepScreenAwake} onChange={(v) => settings.setKeepScreenAwake(v)} label="Keep screen awake" />
        </Row>

        <Text variant="subheadline" style={{ marginTop: spacing.md }}>Appearance</Text>
        <Row title="Dark mode">
          <SegmentedControl options={THEME_PREFERENCES} value={s.theme} onChange={(v) => settings.setTheme(v)} label={titleCase} accessibilityLabel="Dark mode" />
        </Row>
        <Row title="Units">
          <SegmentedControl options={MEASUREMENT_UNITS} value={s.units} onChange={(v) => settings.setUnits(v)} label={titleCase} accessibilityLabel="Units" />
        </Row>
        <Row title="Dashboard layout" caption="Phase 11 decides what Compact looks like">
          <SegmentedControl options={DASHBOARD_LAYOUTS} value={s.dashboardLayout} onChange={(v) => settings.setDashboardLayout(v)} label={titleCase} accessibilityLabel="Dashboard layout" />
        </Row>

        <Text variant="subheadline" style={{ marginTop: spacing.md }}>Workout</Text>
        <Row title="Voice announcements" caption="Speak distance and pace milestones during a workout">
          <SettingSwitch value={s.voiceAnnouncements} onChange={(v) => settings.setVoiceAnnouncements(v)} label="Voice announcements" />
        </Row>
      </ScrollView>
    </Screen>
  );
}
```

`src/app/settings.tsx`, the route:

```tsx
import { SettingsScreen } from '@/features/settings/SettingsScreen';

export default SettingsScreen;
```

Worth knowing:
- The switch shows what the settings say; flipping it calls your setter, which saves
  and updates, which redraws the switch. If the setter did nothing, the switch would
  jump back. That loop is how every input works in React.
- `SegmentedControl` is generic, so with `THEME_PREFERENCES` its `onChange` receives a
  `ThemePreference`, not just any string. A typo can't reach your setter.
- Dark mode offers System / Light / Dark rather than on/off, so you can always go back
  to "follow the phone".

### 8.7 — Hook it up

1. `AppSettings` goes in the services file (8.5). One copy for the whole app.
2. The route already exists: `src/app/settings.tsx` *is* `/settings`. Add a link on the
   dashboard next to "Diagnostics": `<Link href="/settings"><Text variant="caption">Settings</Text></Link>`.
3. Run it, flip every setting, force-stop the app
   (`adb shell am force-stop com.myhi.companion`), reopen, and check everything stuck.

---

## Tests

- `appSettings.test.ts` (8.4): defaults, round trip, junk values, snapshot. Automated.
- Change every setting, force-stop, reopen: all kept. On the phone.
- Units round-trip without touching the database. There's no database code in the
  settings at all, which proves it.

## Done when

- [ ] Every setting survives a force-stop
- [ ] No unit conversion ever touches the database
- [ ] All tests pass; `tsc` and lint are clean
