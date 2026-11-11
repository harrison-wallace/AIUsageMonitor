# Changelog

All notable changes to this personal copy of AIUsageMonitor. Newest first.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). `AGENTS.md` section 5 says
which change gets which increment.

## [Unreleased]

### Changed

- Session and Week bars show a white tick at how much of the 5h session or the 7d week has
  passed. Below the bar are only the time left and a pace icon: a level arrow on pace, an amber
  rising arrow when usage is more than 10 points ahead of the time passed. Hover for the words.
  See `docs/adr/0002-pace-bar-and-minimal-text.md`.
- The overlay shows provider logos instead of provider names: in the table group headers, on the
  cards, and in the compact and mini strips. Hover a logo for the provider name. Antigravity has
  no logo yet and keeps its name.

## [1.22.0] - 2026-11-11

### Added

- GitHub Copilot: monthly premium requests, chat, and completions for the `gh` login, read from
  GitHub's Copilot user endpoint. One quota covers Copilot CLI and VS Code. Enable it in Settings.
- The full-size overlay is one table: a group per provider, a row per account, and the same
  columns for every account (Session, Week, Other limits, Credits, Status). **Active / All** hides
  providers with no usage or no data, also in the compact and mini strips, and the choice is
  saved. See `docs/adr/0001-overlay-account-table.md`.
- A Claude sign-in window replaces the terminal login. It never opens a browser by itself: it
  shows the link with **Copy link**, **Open in <your browser> private window**, and **Open in
  browser**, then takes the code from the sign-in page. An optional email is filled in.
- Bundled Inter and DM Mono fonts (SIL OFL 1.1, `THIRD_PARTY_NOTICES.md`).
- `tools/install-local.ps1` builds the app, replaces the installed copy in
  `%LOCALAPPDATA%\Programs\AIUsageMonitor`, adds an "AI Usage Monitor" Start-menu shortcut, and
  starts it. Settings and saved accounts stay.

### Changed

- The app is rebranded "AI Usage Monitor". Window titles, the tray menu, and setup text drop
  "Seth's". The About window and `README.md` credit Seth A. Robinson as the original author.
- App data moves from `%LOCALAPPDATA%\SethsAIUsageMonitor` to `%LOCALAPPDATA%\AIUsageMonitor`, and
  the start-with-Windows entry is now `AIUsageMonitor`. There is no migration: this PC had no data
  in the old folder.
- Usage shows percent **used**, the same direction as Claude Code's `/usage`, in the overlay and
  in the Claude Code status line (`5h 3% used`). Credit balances still show the amount left.
- The overlay and every dialog use the VortexOne dark design language: a warm palette, hairlines,
  tight corners, no shadows, and dark buttons, fields, checkboxes, sliders, and scrollbars. The
  default action button is off-white. Balances are neutral text instead of amber.
- Status uses Optimal, Good, Watch, and Needs attention, each with its own icon shape. The table
  shows the icon alone, with the label in a tooltip.
- Usage bars fill with a gradient that warms from green to amber to salmon as more is used.
- Reset times are short ("4h", "Fable · 14h") with the full date in a tooltip. The Credits column
  fits its content and disappears when no shown account has credits.
- Adding the first saved Claude account also saves the current `claude` CLI login, so its card no
  longer disappears.
- The README is rewritten in the ai-engineering style: badges, Start here, a providers table, and
  fresh screenshots. The "What's new" list is gone; history is in this file. Demo data uses
  generic names (Personal, Work, Research) and includes Copilot.
- `VersionText` uses SemVer: `V1.21` becomes `V1.21.0`.
- Agent rules in `AGENTS.md` describe this repo as a personal copy: agents commit and push to
  `origin` `main`, use SemVer with this changelog, and install the build on this PC. `upstream` is
  no longer a git remote. Claude account notes moved to `docs/claude-accounts.md`.

### Fixed

- A Claude sign-in in a browser already signed in to claude.ai silently added a second copy of
  that account. A sign-in is now saved only after the app checks who signed in: a duplicate, or a
  different email than the one given, is rejected and nothing is saved.
- Copilot kept reading the old account after `gh auth switch`, until a 401 or a restart. The token
  is now read on every refresh. A `gh auth token` call that timed out was left running.
- `install-local.ps1` started the app in session 0 when an agent ran it, where it is invisible and
  runs beside your own copy. It now prints `NOT STARTED` there instead.

### Removed

- `package-release.bat`, `upload-to-rtsoft.bat`, and `tools/restart-app.ps1`. They signed the
  build with the upstream author's certificate and uploaded it to the upstream author's server.

## [1.21.0] - 2026-11-01

Fork point: upstream `SethRobinson/AIUsageMonitor` V1.21, commit `1608fc0`. The history before
this point is in that repository.
