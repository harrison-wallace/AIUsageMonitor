# Changelog

All notable changes to this personal copy of AIUsageMonitor. Newest first.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). `AGENTS.md` section 5 says
which change gets which increment.

## [Unreleased]

### Changed

- Every dialog (Settings, Claude and OpenAI accounts, sign-in, logs, setup windows) uses the
  VortexOne look: the same warm palette and dark buttons, fields, checkboxes, sliders, and
  scrollbars. The default action button is off-white.
- The bundled Inter and DM Mono fonts replace the system fonts (SIL OFL 1.1, `THIRD_PARTY_NOTICES.md`).
- Usage bars fill with a gradient that warms from green to amber to salmon as more is used.
- The account table is narrower: reset times are short ("4h", "Fable · 14h") with the full date in
  a tooltip, the Credits column fits its content and disappears when no account has credits, and
  status is an icon with its label in a tooltip. Compact cards use the short reset times too.

- The full-size overlay is now one table: a group per provider, a row per account, and the same
  columns for every account (Session, Week, Other limits, Credits, Status). **Active / All** hides
  providers with no usage or no data, and the compact and mini strips follow the same choice.
  See `docs/adr/0001-overlay-account-table.md`.
- The overlay uses the VortexOne dark design language: a warm canvas, hairlines, tight corners,
  and no shadows. Status uses Optimal, Good, Watch, and Needs attention, each with an icon and a
  label. Balances are neutral text instead of amber.
- README screenshots are regenerated, and the README shows the Claude sign-in window.

- Adding a Claude account no longer opens a terminal or a browser by itself. A sign-in window shows the
  link with **Copy link**, **Open in <your browser> private window**, and **Open in browser**, then takes
  the code from the sign-in page. An optional email is filled in on the sign-in page.
- A Claude sign-in is saved only after the app checks who signed in. A login as an account that is
  already saved, or as a different email than the one given, is rejected and nothing is saved.
  Before, a browser already signed in to claude.ai silently added a second copy of that account.
- Adding the first saved Claude account also saves the current `claude` CLI login, so its card no
  longer disappears.

- Quota cards show the percent **used**, the same direction as Claude Code's `/usage`. Rows read
  `33% used`, card headlines read `Anthropic - 33% used`, and bars fill as quota is used. The mini
  strip shows only the number. Status colours still turn amber and red as quota runs out. Credit
  balances still show the amount left. The Claude Code status line reads `5h 3% used`.

- The app is rebranded "AI Usage Monitor". Window titles, the tray menu, and setup text drop
  "Seth's". The About window and `README.md` credit Seth A. Robinson as the original author.
- App data moves from `%LOCALAPPDATA%\SethsAIUsageMonitor` to `%LOCALAPPDATA%\AIUsageMonitor`, and the
  start-with-Windows entry is now `AIUsageMonitor`. There is no migration: this PC had no data
  in the old folder.
- `upstream` is no longer a git remote.
- Agent rules in `AGENTS.md` now describe this repo as a personal copy. Agents commit and push to
  `origin` `main`, use SemVer with this changelog, and install the build on this PC.
- Claude account notes moved from `AGENTS.md` to `docs/claude-accounts.md`.
- `VersionText` uses SemVer: `V1.21` becomes `V1.21.0`.

### Added

- `tools/install-local.ps1` builds the app, replaces the installed copy in
  `%LOCALAPPDATA%\Programs\AIUsageMonitor`, adds an "AI Usage Monitor" Start-menu shortcut, and
  starts it. Settings and saved accounts stay.

### Removed

- `package-release.bat`, `upload-to-rtsoft.bat`, and `tools/restart-app.ps1`. They signed the
  build with the upstream author's certificate and uploaded it to the upstream author's server.

## [1.21.0] - 2026-11-01

Fork point: upstream `SethRobinson/AIUsageMonitor` V1.21, commit `1608fc0`. The README
"What's new" list has the upstream history before this point.
