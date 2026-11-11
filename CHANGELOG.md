# Changelog

All notable changes to this personal copy of AIUsageMonitor. Newest first.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). `AGENTS.md` section 5 says
which change gets which increment.

## [Unreleased]

### Changed

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
  `%LOCALAPPDATA%\Programs\AIUsageMonitor`, and starts it. Settings and saved accounts stay.

### Removed

- `package-release.bat`, `upload-to-rtsoft.bat`, and `tools/restart-app.ps1`. They signed the
  build with the upstream author's certificate and uploaded it to the upstream author's server.

## [1.21.0] - 2026-11-01

Fork point: upstream `SethRobinson/AIUsageMonitor` V1.21, commit `1608fc0`. The README
"What's new" list has the upstream history before this point.
