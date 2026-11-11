# 0001 — The full-size overlay is one account table in the VortexOne look

**Status:** accepted
**Date:** 2026-11-11

The question was which overlay layout is cleaner, hides unused providers, and shows several
accounts per provider best. A prototype compared three layouts with the repo's demo data at the
real window size. The owner chose the account table. One table groups the rows by provider, gives
each account one row, and gives every account the same columns: Session, Week, Other limits,
Credits, and Status. The columns line up, so you compare accounts at a glance. An **Active / All**
segmented control hides providers with no usage this period or no data, and one line names what
is hidden. The compact cards and the mini strip stay as cards, because a table does not fit in a
strip, but they follow the same Active / All choice.

The look comes from the VortexOne design system (`vortexone/docs/design-system.md`): a warm dark
canvas, hairlines, 3 to 6 px radii, no shadows, and the Optimal, Good, Watch, and Needs attention
status scale with an icon and a label on each status.

## Considered options

- **Provider cards** (one card per provider, accounts as rows inside it): clean, but it was harder
  to compare the same limit across accounts, and the cards had uneven heights.
- **Focus and detail** (the tightest limit as a hero card, an account list, and a detail pane):
  good for one glance, but it needs a click to see an account's other limits.

## Consequences

- Status follows percent used: below 50 Optimal, 50 to 74 Good, 75 to 89 Watch, 90 and up Needs
  attention. Balances are neutral text, not amber, because status colours are reserved for state.
- The fonts fall back to Segoe UI Variable and Consolas. Inter and DM Mono are not installed and
  not embedded yet.
- The Settings and OpenAI Accounts dialogs still use the old palette.
