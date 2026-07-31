# Phase 13 — UI Polish

**Hardware:** none · **Size:** M · **Blocked by:** Phase 12

---

## Tasks

- App icon, splash
- Typography scale, consistent spacing
- Loading and empty states for **every** screen
- **Error messages that say what to do, not what failed.** "Couldn't reach the
  treadmill — check it's powered on and try again" beats "GATT error 133", though the
  code still belongs in the log
- Accessibility: content descriptions, minimum 48 dp touch targets, contrast check
- Haptics on speed change confirmation
- The diagnostic screens from Phase 00 stay, behind a developer toggle. They are how
  the next firmware surprise gets diagnosed — do not delete them

## Tests

- Every screen reviewed in light and dark, at 33% and full width
- TalkBack pass over the dashboard and controls
- Font scale at 200% does not break layouts

## Acceptance

- [ ] Release candidate
