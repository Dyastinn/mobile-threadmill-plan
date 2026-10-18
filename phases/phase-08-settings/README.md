# Phase 08 — Settings

**Hardware:** none · **Size:** S · **Blocked by:** Phase 07

---

## Goal

User preferences, persisted.

### Understanding what you're building (read this before the tasks)

**Why two storage mechanisms.** Six settings ("dark mode: on", "units: metric") are
single values that only ever get overwritten in place, not unlike a house key on a
hook by the door. Saved Devices is a growing list of Bluetooth devices, each with its
own name, MAC address, last-seen time, and preferred flag, individually added,
removed, and queried; more of a filing cabinet than a hook. This phase stores the
first group in a **key/value store** (`expo-sqlite/kv-store`: flat, synchronous,
strings in and strings out) and leaves the second group exactly where Phase 06
already put it: the SQLite `Device` table.

**Why not put everything in one place.** The simplest-sounding rule would be "this
app already has SQLite, so put every persisted thing in a table": one mechanism, one
thing to learn. Concretely, that means a `Settings` table, a schema migration every
time a new toggle is added, and an async database round trip every time a switch
flips or a screen wants to read a boolean during render. Real latency and real
ceremony for data that's fundamentally just "hold six named values." The opposite
naive rule, keeping Saved Devices in the key/value store too as one JSON blob under a
single key, is wrong in the other direction. Adding one device means parsing the whole
blob, appending, and re-serialising everything back; there's no way to update just one
device's `LastSeenUtc` without touching every other device's data, and two writes
racing (auto-reconnect updating `LastSeenUtc` while the settings screen happens to be
open) can silently clobber each other. SQLite exists specifically to make per-row
updates and concurrent access safe; a hand-rolled JSON blob just reinvents a worse
version of exactly that. Splitting the two isn't extra complexity for its own sake.
Each half is genuinely simpler than forcing the other mechanism onto it would be.

**The pattern, named plainly.** `PreferencesStore` (task 8.1) is the same **seam**
you already built in Phase 01b for `TreadmillService`: a small interface standing
between logic (`AppSettings`) and a real platform API (the kv-store) that `core/`
isn't allowed to import. The cost is the same kind, just smaller in this phase: one
interface, one thin wrapper (`kvPreferencesStore.ts`), one in-memory fake for tests, a
handful of extra lines for two method signatures. The payoff is the same shape too:
`appSettings.test.ts` (task 8.4) can assert "an unrecognized theme string falls back
to `system`, never throws" without a phone anywhere in the loop. Notice where this
project *doesn't* draw the same line: it doesn't build a matching seam over SQLite for
Saved Devices in this phase. The `Device` table already goes through the `Db`
interface from Phase 00, its own seam, built when it was actually needed. A seam earns
its keep only where something on the other side of it genuinely needs to be swapped
or faked for a test, not as a default habit applied to every dependency in the app.

## Learning goals

- The **seam pattern again, one layer down**: the kv-store (an Expo module) can't be
  imported from `src/core/`, same rule you already hit with everything else. This
  phase draws the seam as a tiny `PreferencesStore` interface, so the actual setting
  logic (defaults, parsing, "never throw on a corrupted value") lives in `core/` and
  gets a real Jest test against a fake store. `features/` supplies the one file that
  wraps the real store.
- **Controlled inputs**: a `Switch`'s `value` comes from state and its
  `onValueChange` writes the new value back. Nothing changes on screen until your code
  says so. That's React's model for every input, and it's why "write straight through
  to storage" is two lines here.
- **String-literal unions as setting values**, and parsing an unknown string into one
  safely (a type guard), which is what "fall back, never throw" actually looks like in
  TypeScript.
- Writing straight through to storage the instant the user flips a switch (no
  separate "Save" button): a deliberate choice worth noticing, not an accident.

## Technology decision: `expo-sqlite/kv-store` for settings

**What problem does it solve?** A handful of single values (toggles, a theme choice, a
unit choice) need to survive an app restart and be readable *synchronously*, during
render, with no loading state.

**Why are we using it?** It ships inside `expo-sqlite`, which this app already depends
on, so it costs zero new dependencies. It has a synchronous API (`getItemSync`,
`setItemSync`), so a settings read during render is a plain function call, and it
stores its data in its own SQLite file, separate from `myhi.db`, so a settings write
never touches workout data or the schema. Phase 00 (probe checklist) and Phase 02
(last device) already use it.

**Alternatives considered:**

1. **`react-native-mmkv`.** Pros: the fastest key/value store for React Native,
   synchronous, typed getters (`getBoolean`). Cons: another third-party native module
   to keep compatible across SDK upgrades, for six values that are read a few times a
   minute; its speed advantage is invisible at this scale. Learning curve: low. The
   better choice for an app doing thousands of reads per second (caches, feature flags
   read in hot loops).
2. **`@react-native-async-storage/async-storage`.** Pros: the long-standing community
   standard, huge usage. Cons: async only, so every settings read needs a loading
   state or a startup preload, which is exactly the ceremony this phase is avoiding.
   Learning curve: low. The better choice when you're already async everywhere and
   don't need render-time reads.
3. **A `Settings` table in `myhi.db`.** Pros: one storage mechanism for everything.
   Cons: a migration per new toggle, async reads, and settings writes now share a
   connection and a queue with the workout recorder. Learning curve: none. The better
   choice if settings ever need to be queried relationally, which they won't.

**Why not the alternatives?** MMKV and AsyncStorage both add a dependency to replace
something already installed; AsyncStorage also adds async ceremony. A table couples
six toggles to the schema.

**Long-term considerations.** First-party, versioned with `expo-sqlite`. The
`PreferencesStore` seam means swapping to MMKV later is one new wrapper file.

**Practical example:** `src/features/settings/kvPreferencesStore.ts`, this phase.

## Storage decision

| Setting | Store | Default |
|---------|-------|---------|
| Auto-reconnect | kv-store | true |
| Keep screen awake during workout | kv-store | true |
| Dark mode | kv-store | system |
| Units (metric / imperial) | kv-store | metric |
| Voice announcements | kv-store | off |
| Dashboard layout | kv-store (string) | "default" |
| Saved devices | SQLite (`Device` table) | — |

**Saved Devices** is a collection with a lifecycle and lives in SQLite (the `Device`
table from `../phase-06-recording-schema/README.md`, owned by the Phase 02/06
connection flow). It does **not** get a control on the Settings screen built in this
phase. There's nothing here to manage yet (no "forget this device" UI is in scope), so
it's listed in the table above for completeness only.

## The one rule that matters

**Store metric always; convert at display time only.** The `units` setting built in
this phase is only ever a *preference token*. Whichever screen renders a distance
reads `settings.getUnits()` and converts the number it displays; it never writes a
converted value back to the kv-store or to the database. An imperial toggle that writes
converted values corrupts the dataset permanently and there is no way back. See
`../phase-06-recording-schema/README.md`'s "All measurements are metric" section.

---

## Reference docs

- **`expo-sqlite/kv-store`**: the "Key-value storage" section of
  https://docs.expo.dev/versions/latest/sdk/sqlite/. Read this before task 8.3; note
  that values are strings only, which is why `PreferencesStore` below only has string
  methods.
- **`Switch`**: https://reactnative.dev/docs/switch. `value` + `onValueChange`, the
  controlled-input pair task 8.6 uses.
- **`useSyncExternalStore`**: https://react.dev/reference/react/useSyncExternalStore,
  same as Phases 02 and 05; task 8.5's hook is that shape again.
- **Narrowing**: https://www.typescriptlang.org/docs/handbook/2/narrowing.html. The
  "Using type predicates" section is the `isThemePreference` guard in task 8.2.
- **Color themes in Expo**: https://docs.expo.dev/develop/user-interface/color-themes/
  and **`Appearance`**: https://reactnative.dev/docs/appearance. Only needed for the
  optional bonus in task 8.5 that actually flips the app's light/dark theme live; not
  required for this phase's acceptance criteria.
- **`docs/learning/04-Monochrome-Theme.md`**: the token/component reference for task
  8.6's UI. Every color on that screen comes from here; nothing is invented.

---

## Walkthrough

### 8.1 — The seam: `PreferencesStore`

Creates: `src/core/settings/preferencesStore.ts`.

A tiny interface so the setting logic in 8.2 can be unit-tested without the real
store, same reasoning as `TreadmillService` in Phase 01b, just much smaller.

```ts
export interface PreferencesStore {
  getString(key: string): string | null;
  setString(key: string, value: string): void;
}

/** In-memory implementation for tests. */
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

This is small enough to type in directly rather than being handed a skeleton. There's
no design decision left to make.

### 8.2 — `AppSettings` (the logic — you write this)

Creates: `src/core/settings/appSettings.ts`.

The shape, not the implementation:

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

export function isThemePreference(value: string | null): value is ThemePreference {
  // TODO: true if value is one of THEME_PREFERENCES. `value is ThemePreference` makes
  // this a type guard: after `if (isThemePreference(x))`, TypeScript knows x's type.
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

  // TODO: setKeepScreenAwake, setVoiceAnnouncements: same shape
  // TODO: setTheme(value: ThemePreference), setUnits, setDashboardLayout: same shape

  private read(): SettingsSnapshot {
    return {
      autoReconnect: this.readBool(KEYS.autoReconnect, true),
      // TODO: the other five. theme: read the string, isThemePreference(...) ? it :
      // 'system'. A future app version could store a value this version doesn't
      // recognize; never throw on that, just fall back. Same for units and layout.
    };
  }

  private readBool(key: string, fallback: boolean): boolean {
    // TODO: 'true' -> true, 'false' -> false, anything else (including null) -> fallback
  }

  private refresh(): void {
    this.snapshot = this.read();
    this.changes.emit(undefined);
  }
}
```

The `as const` array plus `(typeof X)[number]` is the trick that gives you both a
runtime list (for the segmented control's options, and for the type guard) and a type
(`'system' | 'light' | 'dark'`) from one declaration, so they can never drift apart.

Concrete steps:
1. Fill in the `TODO`s, following the pattern `setAutoReconnect` and `readBool` show.
2. `npx tsc --noEmit` and `npx expo lint`.

### 8.3 — `kvPreferencesStore` (the wrapper — you write this)

Creates: `src/features/settings/kvPreferencesStore.ts`.

This one imports an Expo module, so it lives in `features/`, not `core/`. It's mostly
boilerplate, no new decisions:

```ts
import Storage from 'expo-sqlite/kv-store';

import type { PreferencesStore } from '@/core/settings/preferencesStore';

export const kvPreferencesStore: PreferencesStore = {
  getString: (key) => Storage.getItemSync(key),
  // TODO: setString: one-line delegation to Storage.setItemSync
};
```

### 8.4 — Unit tests for `AppSettings`

Creates: `src/core/settings/appSettings.test.ts`.

This is the phase's real automated coverage. The manual "change every setting,
force-stop, relaunch" test below still matters, but it can't run without the phone.

Concrete steps:
1. A fresh `AppSettings` over an empty `MemoryPreferencesStore` returns every
   documented default (`autoReconnect` true, `theme` `'system'`, `units` `'metric'`,
   etc.).
2. `setUnits('imperial')` then reading it back returns `'imperial'`; setting it back
   to `'metric'` and reading again returns `'metric'`. This is the "round-trip through
   imperial and back" test from the table below, proved against the *setting itself*
   rather than against workout data.
3. Write an unrecognized string directly into the store under the theme key
   (`store.setString('settings.theme', 'purple')`), then construct a *new*
   `AppSettings` over the same store and expect `theme` to be `'system'`, not an
   exception. This is what "fall back, never throw" in task 8.2 is actually protecting
   against: a future version writing a value this one won't recognize.
4. `getSnapshot()` returns the identical object until a setter runs, then a new one,
   and a subscribed listener is called exactly once per setter call.
5. `npm test`: all green, including every prior phase's tests (regression).

### 8.5 — `useSettings` (the hook — you write this)

Creates: `src/features/settings/useSettings.ts`.

1. Create `AppSettings` once in `services.tsx`, over `kvPreferencesStore`.
2. The hook returns `useSyncExternalStore(settings.subscribe, settings.getSnapshot)`
   plus the `settings` object, so a screen can read values and call setters. Same
   shape as Phase 05's `useSpeedControl`.
3. Wire `autoReconnect` into Phase 02's `ReconnectionManager`: it should read
   `settings.getSnapshot().autoReconnect` before starting a backoff loop. Pass
   `settings` into its constructor in `services.tsx`.
4. Optional bonus, not required for this phase's acceptance: actually flip the running
   app's theme live. React Native's `Appearance.setColorScheme('light' | 'dark')`
   overrides the system setting app-wide, and the theme's `useTheme()` (which reads
   `useColorScheme()`) follows it automatically. Check the `Appearance` doc linked
   above for the value that means "follow the system again" in your React Native
   version. Call it from a small effect in the root layout that watches
   `snapshot.theme`. Either answer is fine; just make it deliberate.

### 8.6 — UI: the settings screen (agent-authored)

Per the collaboration model in `../README.md`, this is UI. The full TSX is below,
ready to paste in. It uses only tokens and components from
`docs/learning/04-Monochrome-Theme.md`, plus one new small component,
`SegmentedControl`, added to the theme here because three settings need "pick one of
a few" and React Native has no built-in picker.

Creates: `src/features/shared/theme/SegmentedControl.tsx`,
`src/features/settings/SettingsScreen.tsx`, `src/app/settings.tsx`.

**`SegmentedControl.tsx`:**

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

Selected state is carried by fill *and* weight, never a hue, per the theme's
"conveying state without color" rule. `accessibilityRole="radio"` plus
`accessibilityState={{ selected }}` is what makes TalkBack announce "selected".

**`SettingsScreen.tsx`:**

```tsx
import type { ReactNode } from 'react';
import { ScrollView, Switch, View } from 'react-native';

import { useSettings } from './useSettings';
import {
  DASHBOARD_LAYOUTS,
  MEASUREMENT_UNITS,
  THEME_PREFERENCES,
} from '@/core/settings/appSettings';
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
        <Row title="Keep screen awake" caption="Prevent the screen from locking during a workout">
          <SettingSwitch value={s.keepScreenAwake} onChange={(v) => settings.setKeepScreenAwake(v)} label="Keep screen awake" />
        </Row>

        <Text variant="subheadline" style={{ marginTop: spacing.md }}>Appearance</Text>
        <Row title="Dark mode">
          <SegmentedControl
            options={THEME_PREFERENCES}
            value={s.theme}
            onChange={(v) => settings.setTheme(v)}
            label={titleCase}
            accessibilityLabel="Dark mode"
          />
        </Row>
        <Row title="Units">
          <SegmentedControl
            options={MEASUREMENT_UNITS}
            value={s.units}
            onChange={(v) => settings.setUnits(v)}
            label={titleCase}
            accessibilityLabel="Units"
          />
        </Row>
        <Row title="Dashboard layout" caption="Phase 11 decides what Compact means">
          <SegmentedControl
            options={DASHBOARD_LAYOUTS}
            value={s.dashboardLayout}
            onChange={(v) => settings.setDashboardLayout(v)}
            label={titleCase}
            accessibilityLabel="Dashboard layout"
          />
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

**`src/app/settings.tsx`**, the route, same one-line pattern as the dashboard:

```tsx
import { SettingsScreen } from '@/features/settings/SettingsScreen';

export default SettingsScreen;
```

Things worth understanding, even though you didn't write this:
- `value={s.autoReconnect}` + `onValueChange={...}` is a **controlled** input: the
  switch shows what the snapshot says, and flipping it calls your setter, which writes
  the store, which updates the snapshot, which re-renders the switch. If the setter
  did nothing, the switch would snap back. That loop is the whole of React's input
  model.
- `SegmentedControl` is generic (`<T extends string>`), so passing `THEME_PREFERENCES`
  makes `onChange` receive a `ThemePreference`, not a plain `string`. A typo'd option
  can't reach your setter.
- "Dark mode" offering `System / Light / Dark` rather than an on/off switch is
  deliberate: "follow the phone" is the default and needs to be choosable again after
  you've picked one.

### 8.7 — Wire it up

Touches: `src/features/services.tsx`, `src/features/dashboard/DashboardScreen.tsx`.

Concrete steps:
1. `services.tsx`: `settings: new AppSettings(kvPreferencesStore)` (task 8.5 step 1).
   `AppSettings` is a single instance: it holds no per-screen state, and the store
   underneath is global anyway.
2. Expo Router already registered the route: `src/app/settings.tsx` *is* `/settings`.
   Add a link to it on the dashboard next to "Diagnostics":
   `<Link href="/settings"><Text variant="caption">Settings</Text></Link>`.
3. Run it, open Settings, flip every toggle, force-stop the app (Settings → Apps →
   MyHi Companion → Force stop, or `adb shell am force-stop com.myhi.companion`), and
   relaunch to confirm persistence by eye before calling the phase done.

---

## Tests

- `appSettings.test.ts` (task 8.4): defaults, round-trip, corrupt-value fallback,
  snapshot identity. Automated.
- Change every setting, force-stop, relaunch: all persist. **`[HUMAN]`**, on-device.
- Units toggle round-trips through the setting (covered by 8.4's test 2) without
  touching the database. There is no database write path from the settings code at
  all in this phase, which is itself the proof.

## Acceptance

- [ ] All settings survive force-stop
- [ ] No unit conversion ever touches the database
- [ ] `appSettings.test.ts` and every prior phase's tests pass; `tsc` and lint clean
