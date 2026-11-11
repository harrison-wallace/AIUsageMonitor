# 0002 — Session and week cells show a pace bar, and the overlay uses minimal text

**Status:** accepted
**Date:** 2026-11-11

The question was how to show Session % and Week % more clearly. A prototype compared three cells
with the demo data: a pace bar, a ring, and a large number with ten blocks. The owner chose the
pace bar. The bar keeps the used percent and adds a white tick at the share of the window that
has passed (a 5h session, a 7d week), so you see at once if usage runs ahead of time. Below the
bar, the cell shows only the time left ("4h") and a pace icon: a level arrow when on pace, an
amber rising arrow when usage is more than 10 points ahead of the time passed.

## Design rule: minimal text

The overlay shows the fewest words that keep the meaning. An icon or a mark replaces a label when
the shape is clear, and the words go in a tooltip. Examples: provider logos, status icons, the
pace icon, and "4h", not "4h left". New overlay elements follow this rule.

## Considered options

- **Ring** (the percent inside a ring gauge): compact, but rows became taller and the percent
  was small.
- **Number first** (a large percent and ten blocks): clear from a distance, but the blocks show
  only 10% steps, and the large text broke the minimal-text rule.
