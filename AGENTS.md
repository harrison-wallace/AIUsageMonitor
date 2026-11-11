# Agent instructions

AIUsageMonitor is a Windows tray app that shows remaining AI subscription usage for Anthropic,
OpenAI and Codex, Antigravity, Gemini, and Cursor. This repo is Harrison's personal copy. It
started as a fork of `SethRobinson/AIUsageMonitor` at V1.21 (2026-11-01). It is for Harrison's
own use on this PC. It does not track upstream, and changes do not go back upstream. The app
is rebranded "AI Usage Monitor". Keep the credit to Seth A. Robinson in `README.md`, the About
window, and `LICENSE`.

`CLAUDE.md` is only `@AGENTS.md`. This file is the one source of agent rules. If a rule is not
clear, ask Harrison before you continue.

---

## 1. Core Rules

- **Think before you code.** State your assumptions. Ask when you are not sure. Never guess.
  Ask as a numbered list. Each question must be answerable in a few words.
- **Goal-driven execution.** Change a vague instruction into a success criterion that you can
  verify. Do this before you write code.
- **Simplicity first.** Write the minimum code that solves the problem. Do not add an
  abstraction that nobody asked for.
- **Surgical changes.** Do not touch code that the request does not cover. Each changed line
  must trace back to the request.
- **Long-term architecture.** Do not accept a temporary fix that someone must replace later.
- **Prefer established libraries** over a custom implementation, if a suitable one exists.
- **Security first.** Never write a credential, a token, or account data into the repository.
- **Do not preserve backward compatibility** unless Harrison asks for it. There is one
  exception: the installed app must still load the existing `monitor.settings.json` and the
  saved accounts under `%LOCALAPPDATA%\AIUsageMonitor`. A change that breaks them needs a
  migration in `AppSettings.Normalize()` and a MAJOR version bump.
- **Phase board.** If `docs/plans/NOW.md` exists, read it first, then only the phase doc it
  names. `docs/plans/` stays out of git.

---

## 2. Language Style

Write all replies and all documentation in ASD-STE100 Simplified Technical English:

- Use the active voice and simple tenses. Keep each sentence to 20 words or fewer.
- Give one idea in each sentence. Keep each paragraph to 6 sentences or fewer.
- Use the same word for the same idea each time. Do not use idioms or slang.
- Make each "it", "they", and "this" point to one clear thing. If you are not sure, repeat the
  noun.
- Do not use slashes or semicolons. Do not use em dashes.
- Use the real name of a file, command, flag, or class, in code font.
- Be specific. Write "an empty token shows another account's numbers", not "this can cause
  issues".

Keep output short. Keep clarity and information density.

---

## 3. Git

Agents **are allowed and expected** to commit and `git push origin main` in this repo.

- **No branches.** Work on `main` only. Do not open feature branches or PRs.
- **Never push to `upstream`** (`SethRobinson/AIUsageMonitor`). Never open a PR or an issue
  there. `upstream` is a read-only reference.
- **Commit messages** are conventional and specific: `feat(scope):`, `fix(scope):`,
  `docs(scope):`, `chore(scope):`, `test(scope):`. The subject says what changed. The body,
  when useful, says why. Example: `fix(cursor): keep the last good balance when the dashboard
  times out`. Not: `update`, `fixes`, or `wip`.
- Stage only the files for this change. Do not `git add .` when unrelated files are dirty.
- Before each commit, review the staged diff for secrets. Section 6 lists what counts.
- The agent shell has no terminal for Git Credential Manager, so a plain `git push` fails. Push
  with the `gh` login for that one command:
  `git -c credential.helper= -c 'credential.helper=!gh auth git-credential' push origin main`.
  Do not change git config.

---

## 4. Build, Test, and Run

This is a .NET 10 WPF and WinForms app. It needs the .NET 10 SDK
(`winget install Microsoft.DotNet.SDK.10`). The root project is `AIUsageMonitor.csproj`
(`net10.0-windows`). Root `dotnet build` builds only the app. `tests/` and `tools/` are
excluded from its compile items.

| Task | Command |
| --- | --- |
| Build | `dotnet build` |
| Tests | `dotnet test .\tests\AIUsageMonitor.Tests\AIUsageMonitor.Tests.csproj` |
| One test class | add `--filter FullyQualifiedName~MultiAccountAggregationTests` |
| Fake refresh, independent providers | `dotnet run --project .\tools\AIUsageMonitor.Diagnostics -- refresh --fake --scenario staggered --assert-independent` |
| Fake refresh, failure | `... refresh --fake --scenario failure` |
| Fake refresh, cancel | `... refresh --fake --scenario cancel --cancel-after-ms 500` |
| Layout sweep | `dotnet run --project .\AIUsageMonitor.csproj -- --layout-sweep .\artifacts\layout-sweep` |
| Install on this PC | `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install-local.ps1` |

- Run the tests that cover the change. Run the full test project once at the end when shared
  code changed: the aggregator, settings, models, or a collector base.
- In an agent shell, the 3 WebView2 tests in `ClaudeBillingBrowserTests` fail with
  `Invalid window handle. (0x80070578)`, with or without the sandbox. They need a normal
  desktop session. This failure is known (2026-11-11, V1.21.0). Report it, and ask Harrison to run
  that class from his own terminal when billing browser code changes.
- After a new SDK install, refresh `PATH` in the shell first:
  `$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')`.
- `install-local.ps1` writes to `%LOCALAPPDATA%` and starts a GUI process. Run it outside the
  agent sandbox.
- Use fake diagnostics and tests by default. Live collectors run local CLIs, use saved
  credentials, call provider APIs, and can use a small amount of quota. Run a live check only
  when Harrison asks: `dotnet run --project .\tools\AIUsageMonitor.Diagnostics -- refresh --live
  --provider Anthropic --force-refresh --timeout-seconds 120`. `--provider` runs every account
  of that provider. Add `--account <id-or-label>` to run one. Diagnostics `provider-result`
  events include `planName`.
- The layout sweep renders 544 size, scale, and content combinations from fake data. It returns
  a nonzero exit code on a geometry, missing-content, or excessive-gap failure. Run it after a
  change to card layout or `ResponsiveLayoutEngine`.

### The installed copy

- `tools\install-local.ps1` publishes a self-contained single-file build, stops the installed
  copy (matched by full path), copies the new exe to `%LOCALAPPDATA%\Programs\AIUsageMonitor`,
  writes the "AI Usage Monitor" Start-menu shortcut, and starts it detached from the calling shell.
  Start with Windows is on. The app owns that Run key and rewrites it from
  `AutoRunAtLoginEnabled` when Settings is saved, so change the setting, not only the key. A failed build leaves the installed copy
  running. The build is not code-signed.
- After a task that changes app code, and after the build and tests pass, run
  `install-local.ps1` so Harrison runs the latest build. Report the `INSTALLED` and `STARTED`
  lines. The installed tray app is the one process that you leave running.
- Do not start the installed exe with plain `Start-Process`. The agent shell can hold it in a
  kill-on-close Windows job, and the app then closes with the session and no crash event.
- `monitor.settings.json` and `monitor.log.jsonl` sit next to the installed exe. The install
  script never overwrites them. `AppSettingsService` falls back to the current directory when
  no settings file exists next to the exe, so always start the app with its own folder as the
  working directory.
- `dotnet run` builds an exe with the same name, `AIUsageMonitor.exe`. Stop only the process you
  started. Never stop processes by name.

---

## 5. Version and Changelog

The version lives in one place: `VersionText` in `Services\AppMetadata.cs`, as `V<x.y.z>`.
Releases are tagged `v<x.y.z>`. `CHANGELOG.md` follows Keep a Changelog, newest first, with
`## [Unreleased]` at the top and `Added`, `Changed`, `Fixed`, `Removed`, and `Security`
sections.

- Add a line under `## [Unreleased]` in the same commit as each behaviour change.
- A release happens only when Harrison asks for one. Use the `bump-version` skill. It sets
  `VersionText`, dates the changelog block, and prints the commit and tag commands. Run those
  commands (section 3 allows this), push with `git push --follow-tags origin main`, then run
  `install-local.ps1`.
- The README "What's new" list stops at V1.21, which is upstream history. Do not extend it.
  New history goes in `CHANGELOG.md` only.

| Increment | When to use |
| --- | --- |
| **PATCH** (x.y.+1) | Bug fix, layout fix, docs, or build script change. |
| **MINOR** (x.+1.0) | New provider, new card or setting, new feature. |
| **MAJOR** (+1.0.0) | A settings or saved-account format change that needs a migration, or a removed provider. |

---

## 6. Prohibited Actions

- **Secrets.** Do not read, print, or copy a value from `.credentials.json`, `.claude.json`,
  `auth.json`, a WebView2 profile, or `monitor.settings.json` protected fields. Never commit
  them. Tests use fake home directories and fake settings.
- **Real login files.** Do not run code that writes to the real `~/.claude`, `~/.codex`, or
  `%LOCALAPPDATA%\AIUsageMonitor` during development. Account switching, token refresh, and
  adoption change the CLI login that Harrison uses for work.
- **Rename of app data.** Do not rename `AIUsageMonitor` in data paths or in `StartupEntryName`.
  Saved accounts, billing profiles, and the Run key use these names. Another rename needs a
  migration that moves them first.
- **Desktop control.** Do not take desktop screenshots or send input to the desktop unless the
  current request allows it. The app's own `--screenshot` modes render offscreen and are always
  allowed.
- **Destructive deletes.** A recursive or wildcard delete must spell a literal part of the path
  at the delete, for example `(Join-Path $repo 'artifacts\publish')`. Never target a bare
  variable or the current directory.

---

## 7. Architecture Rules

- Provider refreshes run independently. Do not reintroduce all-or-nothing refresh, where one
  slow collector keeps every card on `checking`.
- Keep collector work off the WPF dispatcher. Marshal only view-model updates back to it.
- `ProviderAccount.DisplayKey` is the provider key everywhere: aggregator, backoff state,
  checking placeholders, and card upserts. Never compute it anywhere else. Two collectors must
  never share a DisplayKey. `ProviderUsage.AccountLabel` is display data only.
- `AppSettingsService.Update` serializes settings updates. `TrayIconService` caches one
  `AppSettings` for the app lifetime, so it saves through `SaveSettingsPreservingAccounts()`.
  A direct save of the cached copy rolls back accounts and slot identity.
- Settings saves are atomic: temp file, `Flush(true)`, then rename. Do not add a persistent
  `.bak` file. An unparseable file goes to `.corrupt-<timestamp>` and is replaced from the newest
  readable `.tmp`, else from defaults. IO errors still throw. Tests:
  `AppSettingsServiceRecoveryTests`.
- `App.OnStartup` shows a message and exits on a startup failure. It must never leave a process
  with no tray icon that holds the single-instance lock.
- Quota rows and card headlines show percent **used** (`UsageWindowDisplay.ValueText`,
  `ProviderUsageCard.PrimaryUsedPercent`), derived as 100 minus the remaining percent.
  Collector-supplied text, such as a balance, is shown as it is (`UsageWindowDisplay.HasCustomText`).
  The mini strip omits the word "used" for space.
- The UI follows the VortexOne design language (ADR 0001): the tokens are in
  `ViewModels/UsageBrushes.cs` and `Views/UsageTableView.xaml`. No shadows, no gradients, no pill
  toggles, 3 to 6 px radii. A status always shows its icon and its label (`UsageStatus`: Optimal
  below 50% used, Good, Watch from 75%, Needs attention from 90%). Status colours are for state only.
- Balance rows (`IsBalance`) stay out of headline quota percentages and use neutral text, not a
  status colour. A balance-only card or row has the status "Balance".

### Feature index

Read the linked doc before you change the feature.

| Feature | Doc | Constraint that is easy to miss |
| --- | --- | --- |
| Claude accounts, sign-in, slot identity, account switch | [docs/claude-accounts.md](docs/claude-accounts.md) | The `~/.claude` slot identity is resolved live every refresh. Token and `oauthAccount` block always move together. Sign-in saves nothing until the identity is checked. |
| OpenAI saved accounts | [docs/openai-accounts.md](docs/openai-accounts.md) | Isolated Codex homes. Never read shared sessions or switch the normal Codex login. |
| Claude usage credits and website billing | [docs/claude-usage-credits.md](docs/claude-usage-credits.md) | `spend.balance: null` never means zero. Billing reads go through the WebView2 browser, not `HttpClient`. |
| Overlay account table | [docs/adr/0001-overlay-account-table.md](docs/adr/0001-overlay-account-table.md) | Full size only (`DisplayMode` Full); compact and mini stay cards. `UsageTableBuilder` groups cards by `SourceProviderName`, so a model-group card rejoins its account row. Active / All (`ShowUnusedProviders`, saved in settings) also filters `VisibleProviders` for the strips; when every row is unused, all show. The layout sweep measures the table in Full mode. |
| ChatGPT purchased credits | `Collectors\CodexCredits.cs` | A credit count, not dollars. Added only when the account holds credits. |
| Claude subscription credits | `Collectors\ClaudeUsageCredits.cs` | Read `spend.balance`. Never derive it from `monthly_limit - used_credits`. |

### README screenshots

Screenshots render offscreen from fake data. They never read real settings or credentials.

- Overlay: `dotnet run --project .\AIUsageMonitor.csproj -- --screenshot .\docs\screenshot-full.png --screenshot-size 1440x740`. Use `1440x136` for `screenshot-compact.png` and `1440x60` for `screenshot-mini-strip.png`. Then copy `screenshot-full.png` to `screenshot.png`.
- Settings: `-- --screenshot-settings .\docs\screenshot-settings.png`
- Claude accounts: `-- --screenshot-accounts .\docs\screenshot-accounts.png`
- OpenAI accounts: `-- --screenshot-openai-accounts .\docs\screenshot-openai-accounts.png`
- Claude sign-in window, step 2 with a demo link: `-- --screenshot-claude-login <path.png>`. The private-window button names this PC's default browser.

---

## 8. Definition of Done

1. The change meets the success criterion that you stated at the start.
2. You did not change unrelated code.
3. `dotnet build` passes, and the tests that cover the change pass. Say which ones you ran. If a
   check fails, report it with the output.
4. App code changed: `install-local.ps1` ran, and you report its `INSTALLED` and `STARTED` lines.
5. `CHANGELOG.md` has an `[Unreleased]` line for each behaviour change. Docs that the change made
   wrong are fixed, here and in `docs/`.
6. The change is committed and pushed to `origin main`.
7. You state what you did not do, and why.

---

## 9. Subagent Model Routing

- Set `model` on every Agent call, and `effort` on each one except a Haiku read.
- Find files, search, read logs: `haiku`. Repeated review or research: `sonnet`, `medium`. One
  fresh-context judgement: `opus`, `high`. Edits that need this conversation: the main session.
- Never go above Opus `high`. Do not let two subagents write the same files.
- Set `model` on Explore calls, usually `haiku`.
