# AI Usage Monitor

![Version](https://img.shields.io/badge/version-1.22.0-f7f5f0?style=flat-square&labelColor=2b2622)
![Providers](https://img.shields.io/badge/providers-7-7fd1a8?style=flat-square&labelColor=2b2622)
![Platform](https://img.shields.io/badge/platform-Windows-4a8fe0?style=flat-square&labelColor=2b2622)
![.NET](https://img.shields.io/badge/.NET-10-9085e9?style=flat-square&labelColor=2b2622)
![License](https://img.shields.io/badge/license-MIT-dad2c1?style=flat-square&labelColor=2b2622)

A Windows tray app that shows how much of each AI subscription you have used: Claude, OpenAI
Codex, GitHub Copilot, Antigravity, Gemini, and Cursor, with several accounts per provider.

![The overlay: one table, a row per account](docs/screenshot-full.png)

## Start here

```
install     tools\install-local.ps1      build, install to %LOCALAPPDATA%\Programs, start
open        Start menu > AI Usage Monitor, or the tray icon
accounts    Settings > Anthropic > Accounts · Settings > OpenAI > Accounts
providers   Settings > tick a provider > Setup explains its login
update      run tools\install-local.ps1 again: settings and accounts stay
```

## Providers

Each provider reads the login you already use. Nothing is sent anywhere except to that
provider.

| Provider | What it shows | Login it reads |
|---|---|---|
| Claude (Anthropic) | Session (5h), week (7d), model limits such as Fable, usage credits | `~/.claude`, plus saved accounts in the app |
| Anthropic API credits | Console prepaid balance | A saved Console website login |
| OpenAI Codex | Session, week, purchased credits | The Codex login, plus saved accounts in the app |
| GitHub Copilot | Monthly premium requests, chat, completions | `gh auth login`. One quota covers Copilot CLI and VS Code. |
| Antigravity | Model quotas | The running Antigravity app |
| Gemini | Code Assist model quotas | Gemini CLI |
| Cursor | Included and on-demand usage | A dashboard login, or a Teams Admin API key |

Fresh Claude numbers, and OpenAI numbers with no saved account, can need a tiny CLI probe. That
probe uses a very small amount of your plan. Saved OpenAI accounts and Copilot read their quota
with no prompt.

## The overlay

- **Full size** is one table: a group per provider, a row per account, and the same columns for
  every account (Session, Week, Other limits, Credits). Compare accounts at a glance.
- **Active / All.** Active hides providers with no usage this period or no data, and one line
  names what is hidden. The choice is saved.
- **Percent used**, the same direction as Claude Code's `/usage`. Bars warm from green to amber
  to salmon as they fill.
- **Status** is an icon with its own shape: Optimal, Good, Watch, Needs attention. Hover it for
  the label, or hover a cell for the full reset date.
- **Resize** from any edge. Smaller sizes change to compact cards, then to a one-line strip.

![Compact cards](docs/screenshot-compact.png)

![Mini strip](docs/screenshot-mini-strip.png)

## Accounts

**Claude.** Add as many accounts as you like. Each gets its own row. **Switch CLI to this** moves
the `claude` CLI login to that account, and the login you leave is kept so you can switch back.
The first saved account also saves your current `claude` login, so its row stays.

![Claude accounts](docs/screenshot-accounts.png)

Adding an account never opens a browser by itself. Copy the sign-in link, or open it in a private
window of your default browser, then paste the code. The app checks who signed in before it saves
anything: a duplicate account or the wrong email is rejected.

![Claude sign-in](docs/screenshot-claude-login.png)

**Usage credits.** Under an account, **Connect usage credits** signs in to the Claude website
once, so the prepaid balance shows in that account's Credits column.

**OpenAI.** Saved accounts use their own Codex homes, so they never change your normal Codex
login.

![OpenAI accounts](docs/screenshot-openai-accounts.png)

## Settings

The refresh interval, UI scale, which providers to check, always on top, start with Windows, and
each provider's setup.

![Settings](docs/screenshot-settings.png)

## Install

Needs Windows and the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install-local.ps1
```

The script builds a self-contained exe, replaces the copy in
`%LOCALAPPDATA%\Programs\AIUsageMonitor`, adds a Start menu shortcut, and starts it. A failed build
leaves the running copy alone. Run from a non-interactive shell (an agent in session 0), it
installs but does not start the app; start it from the Start menu. The build is not code-signed. App data (saved accounts, website
logins) is in `%LOCALAPPDATA%\AIUsageMonitor`.

## Develop

| Task | Command |
|---|---|
| Build | `dotnet build` |
| Tests | `dotnet test .\tests\AIUsageMonitor.Tests\AIUsageMonitor.Tests.csproj` |
| Fake refresh | `dotnet run --project .\tools\AIUsageMonitor.Diagnostics -- refresh --fake --scenario staggered --assert-independent` |
| Live check, one provider | `dotnet run --project .\tools\AIUsageMonitor.Diagnostics -- refresh --live --provider "GitHub Copilot"` |
| Layout sweep (544 sizes) | `dotnet run --project .\AIUsageMonitor.csproj -- --layout-sweep .\artifacts\layout-sweep` |
| README screenshots | `dotnet run --project .\AIUsageMonitor.csproj -- --screenshot .\docs\screenshot-full.png --screenshot-size 1440x800` (the other modes are in `AGENTS.md`) |

Screenshots and the sweep use the demo data in `usage.fake.json`. They never read your settings or
logins. [`AGENTS.md`](AGENTS.md) holds the rules for AI agents that work here.

## Layout

```
Collectors/     One collector per provider: reads a login and returns quota windows
Services/       Refresh, accounts, sign-in, settings, screenshots, the layout sweep
ViewModels/     Cards, the account table, status, and the VortexOne colours
Views/          The overlay, the table, and the dialogs (XAML)
Assets/Fonts/   Inter and DM Mono (SIL OFL 1.1)
App.xaml        Design tokens and the dark style for every standard control
tools/          install-local.ps1 and the diagnostics CLI
tests/          MSTest suite
docs/           Feature notes, ADRs, and README screenshots
```

## Docs

- [docs/adr/0001-overlay-account-table.md](docs/adr/0001-overlay-account-table.md): why the
  overlay is a table, the VortexOne design language, and the owner's exceptions to it.
- [docs/claude-accounts.md](docs/claude-accounts.md): Claude accounts, sign-in, the live
  `~/.claude` identity, and account switching.
- [docs/openai-accounts.md](docs/openai-accounts.md): saved OpenAI accounts and their Codex homes.
- [docs/claude-usage-credits.md](docs/claude-usage-credits.md): the website login for usage
  credits. It uses an undocumented endpoint, so a website change can stop the balance for a while.

## Versioning

`VersionText` in `Services/AppMetadata.cs` holds the version, `CHANGELOG.md` records what changed,
and each release is tagged `vX.Y.Z`. **Patch** is a fix, **minor** adds a provider or a feature,
and **major** changes the settings format or removes a provider. Run `/bump-version` to cut a
release.

## Credits

Based on [AIUsageMonitor](https://github.com/SethRobinson/AIUsageMonitor) by Seth A. Robinson
(MIT). His project history before the fork is in that repository. Fonts: Inter and DM Mono, SIL
OFL 1.1 ([THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)). Built with substantial help from AI
tools.
