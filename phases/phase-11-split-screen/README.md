# Phase 11 — Split Screen

> One of the two reasons this app exists at all. Treat it as a feature, not a layout
> chore.

**Hardware:** required to verify · **Size:** M · **Blocked by:** Phase 10

---

## Goal

Usable alongside YouTube.

## Features

- Responsive layout at 33% / 50% / 75% / full
- Compact dashboard variant below a height threshold
- `android:resizeableActivity="true"`, correct `configChanges` handling

## Implementation requirements

- Handle configuration changes **without recreating the BLE connection**. The Phase 07
  service owns it, so this should already hold — verify that it does rather than
  assuming it.
- **Speed controls must remain reachable at 33% height.** This is the real constraint;
  everything else follows from it.
- No horizontal scrolling at any width.

## Tests

| Test | | |
|------|---|---|
| Each of 4 sizes, portrait and landscape | Controls reachable and tappable | `[HUMAN]` |
| Resize mid-workout | No disconnect, no data gap | `[HUMAN]` |
| YouTube / Messenger / Chrome in the other pane | Dashboard keeps updating | `[HUMAN]` |

## Acceptance

- [ ] Fully usable at 33% with no disconnect on resize
