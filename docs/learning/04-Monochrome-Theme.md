# The monochrome theme

> Implemented in `src/features/shared/theme/` during Phase 00 (task 0.1). You don't
> need to create this, only use it. This doc is its specification: the tokens, and the
> small set of components every phase's UI code is built from.

Every phase from here on that touches UI (screens, components) gets the **actual
code** written for you; see `../../phases/README.md`'s collaboration model. This doc is
what that code is built on: one neutral gray ramp, no hue anywhere, used consistently
through semantic tokens so no phase has to invent colors again.

## Why monochrome, and why "soft"

Two decisions, both already made:

1. **No hue.** No blue "primary" color, no red "error" color, no green "connected"
   dot. One gray ramp, walked from both ends depending on light/dark theme. This
   isn't a placeholder. It's the actual theme. If a phase's UI seems to need a
   second color (e.g. "the Stop button should look dangerous"), it doesn't get one;
   it gets weight, an outline, a dash pattern, or a confirmation step instead. See
   "Conveying state without color" below.
2. **Soft, not stark.** The ramp never touches true black (`#000000`) or true white
   (`#FFFFFF`). The darkest token is `#212123`, the lightest is `#F2F2F3`. This was
   a deliberate choice over a harsher near-black/near-white theme, easier on the
   eyes for a screen you're glancing at mid-walk, less "OLED contrast" starkness.

**No UI library.** React Native's own `View`/`Text`/`Pressable` plus about ten small
components here cover every screen this app has. A component library (React Native
Paper, Tamagui, NativeWind) would bring its own design language to override, its own
upgrade cadence, and far more surface than ten components; none of it buys anything
for an app whose whole visual identity is "gray, quiet, legible."

## The gray ramp

Defined once in `tokens.ts`, 11 steps from `50` (lightest) to `950` (darkest):

```
50   #F2F2F3   400  #9A9AA0   800  #38383C
100  #E4E4E6   500  #7D7D82   900  #2A2A2D
200  #D6D6D9   600  #626266   950  #212123
300  #B8B8BC   700  #4A4A4E
```

**Never reference `gray[...]` directly in a screen.** It's there so the semantic tokens
below have something to point at. Every component and every screen uses the semantic
names, through `useTheme()`.

## Semantic tokens (what you actually use)

Each token has a light and a dark value. `useTheme()` returns the right set for the
current color scheme (the system setting, or Phase 08's override), and re-renders
every component using it when the scheme changes, so light/dark switching needs no
code in any screen.

| Token (`colors.*`) | Used for | Light | Dark |
|---|---|---|---|
| `background` | Screen background | 50 | 950 |
| `surface` | Cards, `Surface`, anything one step off the screen | 100 | 900 |
| `border` | Hairlines, dividers, outlines, grid lines | 300 | 700 |
| `textPrimary` | Body text, values, the primary chart line | 900 | 100 |
| `textSecondary` | Captions, units, timestamps, the secondary chart line | 600 | 400 |
| `textDisabled` | Disabled text | 300 | 600 |
| `disabledBackground` | Disabled control background | 200 | 800 |
| `interactiveBackground` | Primary button fill, selected segment | 800 | 200 |
| `interactiveText` | Text on a primary button / selected segment | 50 | 950 |
| `contributionUnlit` / `contributionLit` | Phase 03 contribution graph cells | 200 / 700 | 800 / 300 |

If a new phase needs a token that isn't here (e.g. a distinct "pressed" state), add it
to both sets in `tokens.ts` following the same "one or two ramp steps off its
neighbour" spacing. Don't invent a one-off hex value inline in a screen.

## `tokens.ts`

```ts
export const gray = {
  50: '#F2F2F3', 100: '#E4E4E6', 200: '#D6D6D9', 300: '#B8B8BC',
  400: '#9A9AA0', 500: '#7D7D82', 600: '#626266', 700: '#4A4A4E',
  800: '#38383C', 900: '#2A2A2D', 950: '#212123',
} as const;

export interface ThemeColors {
  readonly background: string;
  readonly surface: string;
  readonly border: string;
  readonly textPrimary: string;
  readonly textSecondary: string;
  readonly textDisabled: string;
  readonly disabledBackground: string;
  readonly interactiveBackground: string;
  readonly interactiveText: string;
  readonly contributionUnlit: string;
  readonly contributionLit: string;
}

export const lightColors: ThemeColors = {
  background: gray[50], surface: gray[100], border: gray[300],
  textPrimary: gray[900], textSecondary: gray[600], textDisabled: gray[300],
  disabledBackground: gray[200], interactiveBackground: gray[800], interactiveText: gray[50],
  contributionUnlit: gray[200], contributionLit: gray[700],
};

export const darkColors: ThemeColors = {
  background: gray[950], surface: gray[900], border: gray[700],
  textPrimary: gray[100], textSecondary: gray[400], textDisabled: gray[600],
  disabledBackground: gray[800], interactiveBackground: gray[200], interactiveText: gray[950],
  contributionUnlit: gray[800], contributionLit: gray[300],
};

/** Spacing scale. Every gap and padding comes from here. */
export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;

export const radius = { sm: 6, md: 10, lg: 14 } as const;

/** The type scale, smallest to largest. Phase 13 adds `body` (17/24). */
export const typography = {
  caption: { fontSize: 12, lineHeight: 16 },
  metricLabel: { fontSize: 13, lineHeight: 18 },
  default: { fontSize: 15, lineHeight: 21 },
  subheadline: { fontSize: 22, lineHeight: 28, fontWeight: '600' },
  headline: { fontSize: 28, lineHeight: 34, fontWeight: '700' },
  metric: { fontSize: 40, lineHeight: 46, fontWeight: '700' },
} as const;

/** Android's accessibility guidance: interactive elements at least 48 x 48 dp. */
export const minTouchTarget = 48;
```

`useTheme.ts`:

```ts
import { useColorScheme } from 'react-native';

import { darkColors, lightColors, type ThemeColors } from './tokens';

export function useTheme(): { colors: ThemeColors; scheme: 'light' | 'dark' } {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light';
  return { colors: scheme === 'dark' ? darkColors : lightColors, scheme };
}
```

`useColorScheme()` follows the phone's setting, and also follows
`Appearance.setColorScheme(...)` if Phase 08's dark-mode override is wired up.

## The components

Built in Phase 00, used by every screen after it. Each one reads its colors from
`useTheme()`, so none of them takes a color prop, and none of them should.

| Component | Props | What it is |
|---|---|---|
| `Screen` | `children`, `style?` | Full-screen container: `background` color, safe-area padding, `flex: 1` |
| `Text` | `variant?` (`caption`, `metricLabel`, `default`, `subheadline`, `headline`, `metric`; Phase 13 adds `body`), plus every React Native `Text` prop | Text in the type scale. `caption` and `metricLabel` use `textSecondary`; everything else `textPrimary` |
| `Surface` | `children`, `style?`, plus `View` props | One step off the screen: `surface` background, `border` hairline, `radius.md` corners |
| `Button` | `label`, `onPress`, `disabled?`, `accessibilityLabel?`, `accessibilityHint?`, `style?` | Primary action: `interactiveBackground` fill, `interactiveText` label, `minHeight: 48`, disabled uses `disabledBackground`/`textDisabled` |
| `SecondaryButton` | same as `Button` | The non-default action next to a primary one: transparent fill, `border` outline, `textPrimary` label, `minHeight: 48` |
| `MetricTile` | `value`, `unit`, `size?` (`large` default, `compact`; Phase 11), `style?` | The dashboard's big number: a `Surface` with the value in `metric` (or `subheadline` when compact), centred, and the unit in `metricLabel` under it |
| `ConnectionDot` | `connected` | 12 dp circle: filled `textPrimary` when connected, `border`-outlined and empty when not |
| `SegmentedControl` | `options`, `value`, `onChange`, `label`, `accessibilityLabel` | Added in Phase 08: pick one of a few; selected segment is filled `interactiveBackground` and bold |
| `EmptyState`, `LoadingOverlay` | see Phase 13 | Added in Phase 13 |

Two of them in full, as the pattern the rest follow:

```tsx
// Button.tsx
import { Pressable, type StyleProp, type ViewStyle } from 'react-native';

import { Text } from './Text';
import { minTouchTarget, radius, spacing } from './tokens';
import { useTheme } from './useTheme';

export interface ButtonProps {
  readonly label: string;
  readonly onPress: () => void;
  readonly disabled?: boolean;
  readonly accessibilityLabel?: string;
  readonly accessibilityHint?: string;
  readonly style?: StyleProp<ViewStyle>;
}

export function Button({ label, onPress, disabled = false, accessibilityLabel, accessibilityHint, style }: ButtonProps) {
  const { colors } = useTheme();
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ disabled }}
      style={({ pressed }) => [
        {
          minHeight: minTouchTarget,
          minWidth: minTouchTarget,
          paddingHorizontal: spacing.lg,
          borderRadius: radius.md,
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: disabled ? colors.disabledBackground : colors.interactiveBackground,
          opacity: pressed ? 0.8 : 1,
        },
        style,
      ]}
    >
      <Text style={{ color: disabled ? colors.textDisabled : colors.interactiveText, fontWeight: '600' }}>{label}</Text>
    </Pressable>
  );
}
```

```tsx
// ConnectionDot.tsx
import { View } from 'react-native';

import { useTheme } from './useTheme';

export function ConnectionDot({ connected }: { connected: boolean }) {
  const { colors } = useTheme();
  return (
    <View
      style={{
        width: 12,
        height: 12,
        borderRadius: 6,
        borderWidth: 1.5,
        borderColor: connected ? colors.textPrimary : colors.border,
        backgroundColor: connected ? colors.textPrimary : 'transparent',
      }}
    />
  );
}
```

Example: a single dashboard metric, using only existing components and tokens, no
inline colors:

```tsx
<MetricTile value={formatOneDecimal(speedKph)} unit="km/h" />
```

`MetricTile` already gives it the `surface` background, the `border` hairline, rounded
corners, the big number and the small unit caption. Nothing above is a color; it's all
inherited.

## Conveying state without color

The dashboard needs to show "connected" vs. "disconnected", "lit" vs. "unlit" on the
contribution graph, "recording" vs. "idle", all without a second hue. The pattern used
throughout this project:

| Instead of... | Use |
|---|---|
| Green dot = connected, red = disconnected | `ConnectionDot`: filled `textPrimary` circle = connected; outline-only `border` circle = disconnected |
| Colored badge for FTMS-supported fields | The field simply absent (not rendered) when the device doesn't send it, see Phase 03's "hidden not `--`" rule |
| Red "Stop" button | Same `Button` as everything else, plus a **confirmation step** (see Phase 05's safety section, task 5.4): the friction communicates weight, not the color |
| Colored lit/unlit contribution cells | `contributionLit` vs. `contributionUnlit`: five ramp steps apart, already distinct in a monochrome palette |
| A second-colored chart line | Same gray family, **dashed** (`strokeDasharray`), see Phase 10 |
| A highlighted selected option | Filled `interactiveBackground` plus bold text, see `SegmentedControl` |

If a phase's design genuinely seems to need a color to be legible (contrast between two
states is too subtle), the fix is to widen the ramp-step gap between the two tokens, or
add an outline/weight/dash difference, not a hue. Flag it at the review checkpoint if
this happens.

## Where this fits in a phase

Every phase README from 02 onward has UI code for any task that produces a screen or
component. That code is written using only the tokens and components above: you paste
it in, adjust it to match your hook's actual return values, and run it. See
`../../phases/README.md` for the exact division of labour.
