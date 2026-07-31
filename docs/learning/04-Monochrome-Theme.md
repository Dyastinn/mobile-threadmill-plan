# The monochrome theme

> Implemented in [`src/MyHi.Companion/Resources/Styles/Colors.xaml`](../../src/MyHi.Companion/Resources/Styles/Colors.xaml)
> and [`Styles.xaml`](../../src/MyHi.Companion/Resources/Styles/Styles.xaml). Already
> built and building clean — you don't need to create this, only use it.

Every phase from here on that touches UI (XAML pages, styles, widgets) gets the
**actual code** written for you — see `../phases/README.md`'s collaboration model. This
doc is what that code is built on: one neutral gray ramp, no hue anywhere, used
consistently through semantic tokens so no phase has to invent colors again.

## Why monochrome, and why "soft"

Two decisions, both already made:

1. **No hue.** No blue "primary" color, no red "error" color, no green "connected"
   dot. One gray ramp, walked from both ends depending on light/dark theme. This
   isn't a placeholder — it's the actual theme. If a phase's UI seems to need a
   second color (e.g. "the Stop button should look dangerous"), it doesn't get one;
   it gets weight, an outline, an icon, or a size difference instead. See "Conveying
   state without color" below.
2. **Soft, not stark.** The ramp never touches true black (`#000000`) or true white
   (`#FFFFFF`). The darkest token is `#212123`, the lightest is `#F2F2F3`. This was
   a deliberate choice over a harsher near-black/near-white theme — easier on the
   eyes for a screen you're glancing at mid-walk, less "OLED contrast" starkness.

## The gray ramp

Defined once in `Colors.xaml`, 11 steps from `Gray050` (lightest) to `Gray950`
(darkest):

```
Gray050  #F2F2F3   Gray400  #9A9AA0   Gray800  #38383C
Gray100  #E4E4E6   Gray500  #7D7D82   Gray900  #2A2A2D
Gray200  #D6D6D9   Gray600  #626266   Gray950  #212123
Gray300  #B8B8BC   Gray700  #4A4A4E
```

**Never reference `GrayNNN` directly in a page.** It's there so the semantic tokens
below have something to point at. Every style, every page, every phase's UI code
uses the semantic names.

## Semantic tokens (what you actually bind to)

Each of these is a *pair* — a `...Light` and `...Dark` color — combined via
`AppThemeBinding` inside `Styles.xaml`'s built-in styles for `Label`, `Button`,
`Border`, `Page`, `Shell`, etc. Existing controls (`<Label>`, `<Button>`, `<Border>`,
`<Entry>`, ...) already pick these up automatically with **no `Style=` attribute
needed** — that's the point of putting them in `Styles.xaml` as implicit
(un-keyed) styles.

| Token pair | Used for | Light value | Dark value |
|---|---|---|---|
| `ColorBackground*` | Page background | Gray050 | Gray950 |
| `ColorSurface*` | Cards, `Border`, anything one step off the page | Gray100 | Gray900 |
| `ColorBorder*` | Hairlines, dividers, unfocused outlines | Gray300 | Gray700 |
| `ColorTextPrimary*` | Body text, values | Gray900 | Gray100 |
| `ColorTextSecondary*` | Captions, placeholders, timestamps | Gray600 | Gray400 |
| `ColorTextDisabled*` | Disabled text | Gray300 | Gray600 |
| `ColorDisabledBackground*` | Disabled control background | Gray200 | Gray800 |
| `ColorInteractiveBackground*` | Primary button fill | Gray800 | Gray200 |
| `ColorInteractiveText*` | Text on a primary button | Gray050 | Gray950 |
| `ColorContributionUnlit*` / `ColorContributionLit*` | Phase 03 contribution graph cells | Gray200 / Gray700 | Gray800 / Gray300 |

If a new phase needs a token that isn't here (e.g. a distinct "pressed" state), add
it to `Colors.xaml` following the same `Color...Light` / `Color...Dark` naming and
the same "one or two ramp steps off its neighbor" spacing — don't invent a one-off
hex value inline in a page.

## Reusable styles already defined (`Styles.xaml`)

Beyond the implicit per-control-type styles (every `Label`, `Button`, `Border`,
etc. is already styled with no extra XAML), these **keyed** styles exist for
recurring UI shapes across phases:

- **`SecondaryButton`** (`Style="{StaticResource SecondaryButton}"`) — outline
  button for the non-default action next to a primary `Button` (e.g. "Cancel" next
  to "Confirm Stop").
- **`Headline`** / **`SubHeadline`** — page-level titles.
- **`Caption`** — small secondary text (timestamps, hints).
- **`MetricValue`** — the large number on the dashboard (speed, distance, ...).
  40pt bold, centered.
- **`MetricLabel`** — the small unit caption under a `MetricValue` ("km/h", "kcal").

Example — a single dashboard metric tile, using only existing styles and tokens,
no inline colors:

```xml
<Border Padding="16,12">
    <VerticalStackLayout Spacing="2">
        <Label Text="{Binding SpeedKmh, StringFormat='{0:F1}'}" Style="{StaticResource MetricValue}" />
        <Label Text="km/h" Style="{StaticResource MetricLabel}" />
    </VerticalStackLayout>
</Border>
```

`Border`'s implicit style already gives it `ColorSurface*` background,
`ColorBorder*` stroke, and rounded corners — nothing above is a color, they're all
inherited.

## Conveying state without color

The dashboard needs to show "connected" vs. "disconnected", "lit" vs. "unlit" on
the contribution graph, "recording" vs. "idle" — all without a second hue. The
pattern used throughout this project:

| Instead of... | Use |
|---|---|
| Green dot = connected, red = disconnected | Filled circle (`Ellipse Fill="{StaticResource ColorTextPrimary...}"`) = connected; outline-only circle (`Fill="Transparent" Stroke="{StaticResource ColorBorder...}"`) = disconnected |
| Colored badge for FTMS-supported fields | `FontAttributes="Bold"` + full opacity for present fields, the field simply absent (not shown) otherwise — see Phase 03/04's "hidden not `--`" rule |
| Red "Stop" button | Same `Button` style as everything else, plus a **confirmation step** (per `00-Project-Plan.md`'s safety section) — the friction communicates weight, not the color |
| Colored lit/unlit contribution cells | `ColorContributionLit*` (a darker/lighter gray step) vs. `ColorContributionUnlit*` — already distinct enough in a monochrome palette because it's 5 ramp steps apart |

If a phase's design genuinely seems to need a color to be legible (contrast
between two states is too subtle), the fix is to widen the ramp-step gap between
the two tokens, or add an icon/weight difference — not to add a hue. Flag it at
the review checkpoint if this happens.

## Where this fits in a phase

Every phase README from 02 onward has a "UI code" section for any task that
produces XAML. That code is written using only the tokens and styles above —
you paste it in, adjust bindings to match your ViewModel's actual property names,
and build. See `../phases/README.md` for the exact division of labour.
