# The gray theme

> Built in `src/features/shared/theme/` during Phase 00 (task 0.1). You don't build it,
> you just use it. This page lists its colors, sizes and components.

The agent writes the code for every screen (see `../../phases/README.md`). This page is
what that code is built from: one set of grays, no other colors, used through named
tokens so no screen ever picks its own colors.

## Two decisions

1. **No colors, only grays.** No blue buttons, no red errors, no green "connected" dot.
   This isn't a placeholder; it's the design. If something seems to need a color (say,
   "Stop should look dangerous"), it gets a different weight, an outline, a dashed line
   or a confirmation step instead. See "Showing state without color" below.
2. **Soft, not harsh.** No pure black or pure white. The darkest gray is `#212123`, the
   lightest `#F2F2F3`. Easier on the eyes for a screen you glance at while walking.

**No UI library.** React Native's own `View`, `Text` and `Pressable`, plus about ten small
components here, cover every screen. A UI kit (React Native Paper, Tamagui, NativeWind)
would bring its own look to fight, its own upgrades, and far more than we need.

## The grays

Defined once in `tokens.ts`, from `50` (lightest) to `950` (darkest):

```
50   #F2F2F3   400  #9A9AA0   800  #38383C
100  #E4E4E6   500  #7D7D82   900  #2A2A2D
200  #D6D6D9   600  #626266   950  #212123
300  #B8B8BC   700  #4A4A4E
```

**Never use `gray[...]` directly in a screen.** Screens use the named colors below,
through `useTheme()`.

## Named colors (what screens use)

Each has a light-mode and a dark-mode value. `useTheme()` gives you the right set for the
current mode, and redraws when the mode changes, so no screen needs light/dark code.

| Name (`colors.*`) | Used for | Light | Dark |
|---|---|---|---|
| `background` | Screen background | 50 | 950 |
| `surface` | Cards, anything one step off the background | 100 | 900 |
| `border` | Lines, dividers, outlines, chart grid | 300 | 700 |
| `textPrimary` | Normal text, numbers, the main chart line | 900 | 100 |
| `textSecondary` | Captions, units, times, the second chart line | 600 | 400 |
| `textDisabled` | Disabled text | 300 | 600 |
| `disabledBackground` | Disabled button background | 200 | 800 |
| `interactiveBackground` | Main button fill, selected option | 800 | 200 |
| `interactiveText` | Text on a main button or selected option | 50 | 950 |
| `contributionUnlit` / `contributionLit` | Phase 03's calendar squares | 200 / 700 | 800 / 300 |

Need a new one (e.g. "pressed")? Add it to both sets in `tokens.ts`, one or two grays away
from its neighbour. Never type a color code straight into a screen.

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

`useColorScheme()` follows the phone's setting, and also Phase 08's dark mode setting
if you wire that up.

## The components

Built in Phase 00, used by every screen. Each one gets its colors from `useTheme()`, so
none takes a color option, and none should.

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

Two of them in full, as examples of how the rest are built:

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

Example: one dashboard number, using only existing pieces, no colors typed in:

```tsx
<MetricTile value={formatOneDecimal(speedKph)} unit="km/h" />
```

`MetricTile` already brings the card background, the border, the rounded corners, the big
number and the small unit.

## Showing state without color

| Instead of... | Use |
|---|---|
| Green dot = connected, red = not | `ConnectionDot`: filled = connected, empty outline = not |
| A colored badge for fields the treadmill sends | Fields it doesn't send just aren't shown (Phase 03) |
| A red Stop button | The same button as everything else, plus a **confirmation** (Phase 05): the extra tap carries the weight |
| Colored calendar squares | `contributionLit` vs. `contributionUnlit`: five grays apart, clearly different |
| A second chart line in another color | The same gray family, **dashed** (Phase 10) |
| A highlighted choice | Filled with `interactiveBackground`, plus bold text (`SegmentedControl`) |

If two states look too similar, move their grays further apart, or add an outline,
weight or dash difference. Not a color. Mention it at the review if it comes up.

## How this fits a phase

Every phase from 02 on includes the full code for its screens, built only from the
tokens and components above. You paste it in, adjust it to your hook, and run it. See
`../../phases/README.md`.
