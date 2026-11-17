# Runbook — faridfox fork of V-Notch

Everything needed to run this fork for years without losing upstream's work or your own.
`CLAUDE.md` (repo root) holds the short rules; this file holds the procedures and the reasons.

Contents: 1 Why it works this way · 2 Layout · 3 New machine / fresh clone · 4 Daily work ·
5 Syncing with upstream · 6 Conflict playbook · 7 Editing your own commits (rework) · 8 The gate ·
9 Feature recipes · 10 Your own build on your PC · 11 Contributing upstream · 12 GitHub Actions ·
13 Recovery · 14 Reference

---

## 1. Why it works this way

Facts about upstream (w0otdev/V-Notch, measured November 2026):

- **It rewrites published history.** 16 force-pushes in 5 months: 14 "amend the last commit" a few minutes
  (worst case 12 hours) after pushing, and on 2026-11-01 one that replaced 339 commits with identical content
  but new commit IDs (to strip an AI co-author trailer). Every fork that existed then is stranded.
- ~3 commits/day, about one 100+ file commit per week, a release every 1–2 weeks. The C# → C++ migration
  keeps turning managed method bodies into native calls.
- Constantly changing files: the **committed** `Native/out/VNotch.Native.dll`, the Version lines in
  `V-Notch.csproj`, `Windows/MainWindow.xaml(.cs)`, `Windows/SettingsWindow.*`, all 17 `Locales/*.json`
  (always together), the lock files.
- Its CI is red most of the time; tags are unreliable (30 of 59 point at the abandoned pre-rewrite line).

Consequences:

- **No merges.** Merging upstream into a fork after a rewrite produced 626 conflicted files on the real
  history. Instead the fork is a *patch stack*: your commits are replayed onto upstream's newest commit
  (`git rebase --onto NEW OLDBASE`). The same command handles a normal advance, an amended tip and a full
  rewrite, because the old base is read from your own branch (the parent of the commit with the trailer
  `Fork-Stack-Root: true`), never guessed. On the real rewrite this took 2 conflict stops.
- **Your code lives in your own files** (`Fork/`, `Tests/Fork/`), so it never conflicts. Upstream files get
  at most a few one-line hooks, each marked `FORK(faridfox)`.
- **Nothing is published unverified.** A sync is prepared on `sync/next`, checked (range-diff, footprint,
  structure, build, tests), and only then pushed — atomically, with lease checks.

## 2. Layout

```
w0otdev/V-Notch   master  ─A─B─C─D…      upstream; moves often, sometimes rewritten
faridfox/V-Notch  master  ─────C         the exact upstream commit the stack is based on
                  main    ─────C─[fork: infrastructure]─[feature 1]─[feature 2]…   (default branch)
                  feat/*, up/*           work branches; refs/archive/<UTC>/main = what each sync replaced
```

Remotes: `origin` = `git@github.com:faridfox/V-Notch.git` (SSH; the default key `~/.ssh/id_ed25519` is the
faridfox account). `upstream` = `https://github.com/w0otdev/V-Notch.git`, fetches only `master`, no tags,
push URL `DISABLED`.

Files the fork owns (and nothing else is added):

| Path | Purpose |
|---|---|
| `CLAUDE.md` | rules for every Claude session (force-added: upstream's `.gitignore` ignores it) |
| `Fork/vn` | the workflow helper (bash) |
| `Fork/hooks/*` | git hooks; `vn setup` installs them into `.git/hooks` so they work on every branch |
| `Fork/docs/RUNBOOK.md`, `Fork/README.md` | this file; notice + ledger of fork features |
| `Fork/.gitignore`, `Fork/.gitattributes` | track Markdown under `Fork/` (upstream ignores `*.md`); keep scripts LF |
| `Directory.Build.targets` | one line importing `Fork/msbuild/Fork.targets` (upstream has no such file) |
| `Fork/msbuild/Fork.targets` | native-DLL protection + update guard (section 10) |
| `Fork/msbuild/no-update-public-key.pem` | the update guard key (public half only; private half destroyed) |
| `Tests/Fork/*Tests.cs` | fork tests (`ForkIdentityTests` proves the update guard) |

Local state lives in `.git/vn/` (sync state, gate stamps, test baselines, logs, a copy of `vn` and
`CLAUDE.md`). Test baselines are built in the sibling worktree `C:\dev\V-Notch.baseline`; patch backups go to
`C:\dev\V-Notch-backup\<UTC>`.

## 3. New machine / fresh clone

Prerequisites on Windows: .NET SDK **8.0.423 exactly** (upstream's `global.json` disables roll-forward;
installer from https://dotnet.microsoft.com/download/dotnet/8.0), Git for Windows, and for native work
VS Build Tools 2022 with the C++ x64 tools (MSVC 14.44 — the same linker upstream uses), Python 3 (the `py`
launcher) and the GitHub CLI signed in as faridfox. Everything runs from **Git Bash** (since 2026-11-14 this
project works natively on Windows; `Fork/vn` also runs from WSL as a fallback, via `/mnt/c/dev/V-Notch`).

```bash
git clone -c core.autocrlf=false -c core.eol=lf -c core.fileMode=false \
    git@github.com:faridfox/V-Notch.git /c/dev/V-Notch
cd /c/dev/V-Notch
git config user.name "Farid Fox"; git config user.email "faridfox@outlook.com"
git switch main            # if the clone did not already check it out
bash Fork/vn setup         # repo config, upstream remote, hooks
Fork/vn status
```

Archives (`refs/archive/*`) are not fetched by default: `git fetch origin 'refs/archive/*:refs/archive/*'`.

## 4. Daily work

- **Session start:** `Fork/vn status`. It fetches, says whether upstream moved or rewrote history, shows
  your stack, unpublished work, gate state, outdated hooks, and anything unfinished.
- **Start a feature:** sync first if upstream moved (section 5), then `git switch -c feat/<topic> main`.
- **While working:** commit as often as you like on `feat/<topic>`; push it at the end of every session
  (`git push origin feat/<topic>`; after rebasing it: `git push --force-with-lease=feat/<topic>:<old sha> origin feat/<topic>`).
- **Land it as one commit:**
  ```bash
  git switch feat/<topic>
  git reset --soft main && git commit -m "feat(<topic>): what it does"     # squash to one commit
  git switch main && git merge --ff-only feat/<topic>
  Fork/vn gate && Fork/vn publish
  git branch -D feat/<topic> && git push origin --delete feat/<topic>
  ```
  If `main` moved meanwhile: `git rebase main` on the feature branch first.
- **Small follow-up to a landed feature:** commit on `main` with `git commit --fixup=<that commit>`; the next
  `vn sync` folds it in (and checks that folding changed nothing), then `vn gate`, `vn publish`.
- Commit messages: `feat(x): …`, `fix(x): …`, `fork: …`. Never AI attribution lines.
- Update the ledger in `Fork/README.md` in the same commit as the feature.

## 5. Syncing with upstream

When: before starting a feature, after each upstream release, at least weekly. Small steps are cheaper.

```bash
Fork/vn sync            # prepare on sync/next; main untouched
Fork/vn gate            # build + tests
Fork/vn publish         # promote and push main + master atomically
```

What `vn sync` does and prints:
1. Picks the target: the newest upstream commit that descends from your base and is at least 24 h old
   (upstream amends fresh commits). `vn sync --now` takes the tip; `vn sync <sha>` a specific commit.
2. Says whether upstream advanced or **rewrote** history — the procedure is the same either way.
3. Refuses if upstream now owns a path the fork owns (section 6, "path collision").
4. Lists upstream changes to files that affect the fork's build or terms (`ATTENTION`: `global.json`,
   workflows, licence…). Read them. A new SDK pin means installing that SDK before the gate can pass.
5. Backs up your commits as patch files, folds `fixup!` commits (content must stay identical), replays your
   commits onto the target. Commits that upstream already contains identically are dropped automatically.
6. Prints the **range-diff** (`=` untouched, `!` changed during the replay, `<`/`>` dropped/added), runs
   `vn verify`, and compares your **footprint** in upstream-owned files before and after. A changed footprint
   (a hook lost, upstream code reverted) blocks `vn publish` until you pass `--footprint-reviewed`.
7. Tells you how to move other work branches (`git rebase --onto …`) — do that after publishing.

Long gap (weeks): sync release by release, e.g. `vn sync <sha of each release commit>`, gate and publish
each step. Find release commits with `git log --oneline upstream/master -- V-Notch.csproj`.

Upstream renamed its default branch (`vn status`/`vn sync` stop with a message): check
`git ls-remote --symref upstream HEAD`, then point `remote.upstream.fetch` and `UPSTREAM_BRANCH` in
`Fork/vn` at the new name (commit that change to `Fork/vn`).

## 6. Conflict playbook

A conflict stop shows the commit being replayed and the conflicted files. Resolve **inside that commit**
(its message states its intent), `git add`, then `Fork/vn sync --continue`. Abort any time with
`Fork/vn sync --abort` — `main` is untouched until publish.

- **Take upstream's version of a file:** `git checkout <NEW> -- <path>` (`<NEW>` is printed). If upstream
  deleted the file: `git rm <path>`. Never `--ours`/`--theirs`: during a rebase they are swapped.
- **A marked hook line** (`FORK(faridfox)`): keep upstream's new code exactly, re-add the one hook line.
- **Upstream moved or deleted the code a hook sits in** (e.g. migrated it to C++): re-create the hook at the
  new place if that is easy. If it needs real work, take upstream's file, let the feature go dormant, mark it
  *suspended* in the ledger (`Fork/README.md`), publish, and fix it as a normal task. Never port the old body.
- **Locale files, lock files, csproj Version lines, the native DLL:** the fork never edits these, so they
  cannot conflict. If one does, a rule was broken: take upstream's version and move the change into `Fork/`.
- **Path collision** (upstream adds a path the fork owns, e.g. its own `Directory.Build.targets`):
  keep upstream's file and add the fork's import as a marked hook line; then in `Fork/vn` remove that path from
  `FORK_OWNED`/`UPSTREAM_OWNED`/`collision_check`. Do this as a rework before the sync.
- **rerere is off on purpose:** it silently replayed a wrong resolution after an aborted sync in testing.
- After resolving, read the range-diff (`!` lines) and the footprint report before publishing.

## 7. Editing your own commits (rework)

Drop, reorder, reword, split or squash stack commits — e.g. to retire a feature upstream now ships:

```bash
Fork/vn rework                         # creates sync/next from main
git rebase -i <base printed by vn>     # keep the "Fork-Stack-Root: true" commit first
Fork/vn rework --done                  # range-diff, verify, footprint
Fork/vn gate && Fork/vn publish        # (--footprint-reviewed if hooks were added/removed on purpose)
```

**Upstream shipped something you carry:** an identical fix drops out by itself at the next sync. A feature
upstream re-implemented its own way does not: remove your commit with a rework, update the ledger.

## 8. The gate

`vn gate` (on `main` or `sync/next`, clean tree): `vn verify` → locked restore → Release build of the
solution with warnings as errors (upstream CI does the same) → `Tools/validate_locales.py` → fast test suite
(`Category!=DesktopIntegration`). Failing tests are re-run once (flaky ones are reported, not counted), the
rest are compared with **pure upstream at your base**: failures that also happen there are *inherited*
(reported, not blamed on the fork). A cached full baseline (`vn baseline <sha>`) answers first; any failure it
does not explain is re-run on pure upstream right away, in the sibling worktree `C:\dev\V-Notch.baseline`,
because timing and rendering tests depend on the machine's state (load, a locked or sleeping screen). A pass writes a stamp for that exact content;
`vn publish` requires it. Logs: `.git/vn/gate/<UTC>/`.

Side effects of upstream's test suite on this machine (seen on 2026-11-14): it writes test-host entries into the
real `%APPDATA%\V-Notch\vnotch-crash.log` and creates empty `WhiteNoise` / `translation-models` folders in
`%LOCALAPPDATA%\V-Notch`; it did not change `settings.json`. The installed V-Notch also restarted once and exited
once during gate runs (cause not established), so `vn gate` warns if the app was running before the tests and is
not afterwards. Keep a backup of `%APPDATA%\V-Notch` (e.g. `C:\dev\backups\`).

`VN_SKIP_GATE=1 Fork/vn publish` exists for emergencies only (e.g. upstream's new SDK not installable yet).

Not covered locally (only by GitHub Actions, section 12): CodeQL, the 70 % coverage threshold, gitleaks,
upstream's update-compatibility probes.

## 9. Feature recipes

Each recipe is built by the first feature that needs it; keep this section current when you build one.

**Composition hook (built by `feat(compact-lyrics)`).** The last statement of
`Services/ServiceConfigurator.ConfigureServices` is
`Fork.ForkServices.Configure(services); // FORK(faridfox): single composition hook`, which calls
`Fork/Core/ForkServices.cs`. Register fork services, modules and Spotlight providers there. Keep `Configure` free of
side effects: upstream tests call `ConfigureServices`. UI start-up: `Fork/Core/ForkUi.cs` registers WPF class handlers
for the `Loaded` event of `MainWindow` and `SettingsWindow` (the mechanism upstream uses in
`Services/LocalizedPresentation.cs`), only when `Application.Current is App` (upstream's tests use a plain
`Application`, so nothing attaches there), and wraps each attach in a try/catch that logs (`FORK-UI`): a fork bug must
not take the notch down. Add a feature's attach call there.

**Reaching upstream window internals.** A fork-owned `public partial class MainWindow` (or `SettingsWindow`) file
under `Fork/Features/<X>/` may use the window's private fields and methods: every use is compile-checked at each sync,
unlike reflection. Prefix all its members with `Fork`/`_fork` so they can't collide with upstream's. Upstream calls
into it only through marked hook lines (example: `GetCollapsedWidth` → `ForkCompactExtraWidth()`). Tests can build
windows with upstream's own fixtures (`GreetingAcceptanceTests.MainWindowFixture`) and call the attach method directly.

**Interactive fork UI lives under `NotchWrapper`.** While the notch is visible, the window answers `WM_NCHITTEST`
with "transparent" for every point whose element is not under `NotchWrapper` (or a few named upstream surfaces):
`OverlayWindowController.HandleNcHitTest` → `MainWindow.IsPointInteractive`. So clickable fork UI is a child of
`NotchWrapper`; to draw beside the notch without changing its layout, give it a fixed width and an equal negative
margin (example: `Fork/Features/AppDock`). Layered-window rule on top: a pixel with alpha 0 never gets the mouse, so
invisible hit areas use alpha 1 (`#01000000`). The line bar is the exception: it is only shown while the notch is
hidden, when the check is skipped.

**Service internals via a fork partial.** Upstream services are `partial` too (e.g. `MediaDetectionService`): a fork
file in `namespace VNotch.Services` can add an internal interface implementation that reads private state (example:
`Fork/Features/MediaSwitcher/MediaDetectionService.Sessions.cs`). Keep it read-only; let window code reach it through
a seam (`Func<IForkX?>`) so tests can stub it and upstream's fakes simply leave the feature inert.

**New widget / module.** Code under `Fork/Features/<X>/`: data/timers in a module derived from upstream's
`Modules/NotchModuleBase` (frozen upstream), visuals in a UserControl with `Attach / ApplySettings / Dispose`
like `Controls/WeatherWidget`. Insert it at runtime into `MainWindow.NotchLiveContent` — the grid upstream
marks "Add live widgets inside this group". Verify on first use that greetings/animations treat it correctly.
Do not join the expanded-widget cycle (7 upstream files) unless unavoidable.

**New setting (built by `feat(compact-lyrics)`).** Add a property to `ForkSettingsService.Data`
(`Fork/Core/ForkSettingsService.cs`), stored in `%APPDATA%\V-Notch-faridfox\settings.json` (official builds delete
unknown properties from the shared `%APPDATA%\V-Notch\settings.json`, and `SettingsMigrator` numbers belong to
upstream). It follows the settings window's model: a toggle calls `Preview…` (live), the window's
`ISettingsApplicationService.SettingsApplied` (Save/Apply) calls `CommitPreview`, the window's `Closed` calls
`RevertPreview`. UI rows: insert at runtime from a fork `partial class SettingsWindow` (example:
`Fork/Features/CompactLyrics/SettingsWindow.CompactLyrics.cs`), built like upstream's own runtime rows
(`SettingsWindow.Privacy.cs`): a `SettingRowBorder` border, CheckBox + `SettingHintText` hint + a collapsed keywords
TextBlock, inserted before the settings search first indexes rows (at `Loaded`).

**New text (built by `feat(compact-lyrics)`).** `Fork/Locales/en.json` (+ other languages as wanted, same keys —
`Tests/Fork/ForkTextTests` checks), embedded by `Fork.targets` as `VNotchFork.Locales.<lang>.json` (never a name
starting with `VNotch.Locales.`), read through `ForkText.T("key")`, which follows the app language and falls back to
English, then to the key. Re-apply texts on `Loc.LanguageChanged`. Never pass a fork key to upstream's `Loc.Get`:
upstream's `LocalizationTests` scan every `.cs`/`.xaml` file in the repository — **comments included** — for
`Loc.Get(` followed by a quoted literal, and require each such key in all 17 locale files with real Hindi.

**Change existing behaviour.** Prefer events and decorators registered through the composition hook. If an
upstream method must change: one marked line calling into a fork method
(`if (Fork.X.TryHandle(this)) return; // FORK(faridfox): why`), in the smallest, quietest method possible.
Avoid `Services/*` method bodies that are candidates for the C++ migration.

**Native C++.** Never in `Native/` (upstream's glob compiles all of `Native/**/*.cpp` into its DLL, which the
fork never commits). Put sources in `Fork/Native/`, build them into `VNotch.Fork.Native.dll` with a target in
`Fork.targets` (vcvars64 + cl.exe like upstream's `CompileVNotchNative`), commit that DLL, and add a contract
test like `Tests/NativeServiceContractTests` for the fork library name. Exports: `VN_EXPORT vn_fork_*`.

**NuGet packages.** Avoid: lock files are upstream-owned (17 changes in 30 days). If unavoidable, add the
`PackageReference` in `Fork.targets` and regenerate the lock files in a dedicated top commit at every sync.

**Tests.** Every feature ships tests in `Tests/Fork/` (xUnit, like upstream's), so a sync that breaks it fails
the gate instead of failing silently.

## 10. Your own build on your PC

`Fork/msbuild/Fork.targets` does two things for the app project:
- **Native DLL protection:** local builds use upstream's committed `Native/out/VNotch.Native.dll` instead of
  recompiling (and overwriting) it. `vn native` compiles it explicitly (proves the C++ toolchain); undo with
  `git restore Native/out/VNotch.Native.dll`. GitHub Actions keeps upstream's behaviour.
- **Update guard:** the embedded update-signing key is replaced by a key whose private half was destroyed, so
  no official update package can ever verify — the in-app updater cannot replace a fork build with an official
  release. Turn off "Check for updates" in a fork build anyway: with this guard the app would otherwise
  re-check the feed every 90 seconds while a newer official release exists.

Dev runs: `Fork/vn run` (refuses while the installed V-Notch runs: same single-instance lock and settings).
Never toggle "Start with Windows" from a dev build (it would repoint autostart at `bin\Debug`).

**Daily use — your fork build is your V-Notch** (set up 2026-11-14):

- `Fork/vn install` publishes the *gated* `main` into `%LOCALAPPDATA%\Programs\V-Notch-fork\current`
  (framework-dependent single-file build, same layout as the official installer; version
  `<upstream version>+faridfox.<commit>`). Before swapping it checks that the published native DLL is upstream's
  committed one and that the exe embeds the fork update key, not the official one. Then it stops the running
  V-Notch, keeps the old build as `previous`, turns off "Check for updates automatically" in `settings.json`
  (copy kept in `.git/vn/install/`) and deletes `update-cache.json`, repoints the shortcuts, and starts the
  new build (`--no-start` to skip).
- **After every sync or feature:** `vn gate` → `vn publish` → `vn install`. `vn status` tells you when the
  installed build is older than `main`.
- **Something wrong with a new build:** `Fork/vn rollback` swaps `current` and `previous` (run again to undo).
- **Shortcuts:** Desktop and Start-menu `V-Notch` start the fork build; Start-menu `V-Notch (official)` starts
  the untouched official 2.3.1 install as a fallback. The official installer's original shortcuts are saved in
  `.git/vn/install/original-shortcuts/` (copy them back to undo).
- **Settings are shared** with the official app (`%APPDATA%\V-Notch`), so switching between the two keeps your
  settings. Never run both at once (single-instance lock).
- **Don'ts:** don't use the app's own "Uninstall" button and don't run the official uninstaller: both delete
  `%APPDATA%\V-Notch` (and the official uninstaller removes the Start-menu folder with these shortcuts).
  Don't click "Download update": a fork build can never accept an official update (it fails safely).
- "Start with Windows" is off. If you turn it on in the fork build's Settings, it registers
  `V-Notch-fork\current\V-Notch.exe`, which stays valid across `vn install` and `vn rollback`.
- Version stamps are always `+faridfox.<commit>` build metadata, never a `-suffix` (the updater ranks
  `2.3.2-anything` *below* 2.3.2 and would offer the official release).

## 11. Contributing upstream

Upstream has never received a pull request; the maintainer answers issues within hours and often builds good
ideas themself. Contributing is always the owner's explicit decision — an agent never opens anything upstream.

1. Reproduce on pure upstream (`vn baseline`'s worktree or the official build). Never report fork-only bugs.
2. Ask first: an issue, or a Discussion in "Ideas", with concrete data. Skip what the maintainer declines:
   anything needing a backend, unofficial/ToS-violating APIs, non-macOS-like behaviour.
3. Prepare an upstream-shaped branch:
   ```bash
   git fetch upstream && git switch -c up/<topic> upstream/master
   git cherry-pick <commit>     # then convert: no Fork/ paths, no FORK markers, strings in all 17 locale
                                # files (real translations, Devanagari Hindi), tests, upstream conventions
   ```
   Build/test it in the baseline worktree or by checking it out; optionally run upstream's `scripts/c.ps1`
   (it recompiles the DLL: `git restore` afterwards).
4. `git push origin up/<topic>` (the pre-push hook rejects fork files, markers and AI lines), then
   `GH_TOKEN=$(gh auth token --user faridfox) gh pr create -R w0otdev/V-Notch --base master --head faridfox:up/<topic>`
   with a short description that lists checks already failing on upstream master. No AI attribution.
5. If upstream moves meanwhile: `git rebase --onto upstream/master <old base> up/<topic>`, push with
   `--force-with-lease=up/<topic>:<old sha>`.
6. When merged: the fork's commit drops out at the next sync if identical; otherwise retire it (section 7).

Etiquette: star the repo, keep credits/donation links and the licence intact, keep the fork's description
saying it is unofficial, start with small low-risk changes.

## 12. GitHub Actions (off for now)

Forks start with Actions disabled. To enable later (e.g. before a first contribution):
1. Create the tag the forward-compatibility gate needs on the new-line twin of upstream's `v2.2.0` (same tree,
   no abandoned history): `git tag v2.2.0 2fb9e775bb5366c7b6138ba5c9de376de76a359a` and push it
   (`git push origin refs/tags/v2.2.0` — the only tag the hooks allow).
2. Enable Actions in the fork's Actions tab, then immediately:
   `GH_TOKEN=$(gh auth token --user faridfox) gh workflow disable "Release Installer" -R faridfox/V-Notch`
   and the same for `"Desktop integration"`. Never edit the workflow files.
3. After every sync check `gh workflow list -a -R faridfox/V-Notch`: new upstream workflows arrive enabled.
4. Read results as a difference: `master` (pure upstream) vs `main`; only failures on `main` that are absent on
   `master` are the fork's.

## 13. Recovery

- **Anything during a sync:** `Fork/vn sync --abort` → `main` unchanged.
- **Crash during a sync:** `Fork/vn status` names the state; continue (`vn sync --continue`) or abort.
- **Crash between promote and push:** `vn status` says so; run `Fork/vn publish` again.
- **A bad resolution was published:** the replaced stack is in `refs/archive/<UTC>/main` and in
  `C:\dev\V-Notch-backup\<UTC>\*.patch`. Redo with a rework, or rebuild:
  `git switch -c sync/fix refs/archive/<UTC>/main` … then `git rebase --onto master <old base> sync/fix`.
- **Accidental merge or commits on the wrong branch:** `git branch rescue/<UTC>` first, then
  `git reset --keep origin/main` (or `origin/master`), and re-apply what was meant with cherry-pick.
- **"origin moved" (someone clicked "Sync fork", or another machine published):** fetch, inspect
  `git log --oneline main..origin/main`, rescue anything wanted into a branch, then restore the expected state
  (`origin/main` = your stack, `origin/master` = its base) with a careful
  `VN_PUBLISH=1 git push --force-with-lease=main:<their sha> origin main` — owner's decision.
- **Machine lost:** section 3 (everything that matters is on GitHub; archives with
  `git fetch origin 'refs/archive/*:refs/archive/*'`).
- **Settings:** official uninstall deletes `%APPDATA%\V-Notch`; keep backups (e.g. `C:\dev\backups\`).

## 14. Reference

`Fork/vn` commands: `status`, `sync [--now|<sha>|--continue|--abort]`, `rework [--done|--abort]`,
`verify [ref]`, `gate`, `baseline [sha]`, `publish [--footprint-reviewed]`, `install [--no-start]`,
`rollback`, `setup`, `build [Debug|Release]`,
`test [filter]`, `run`, `native`.

Hooks: `pre-commit` (no commits on `master`, no merge commits on `main`, no DLL/Version-line changes),
`commit-msg` (strips AI attribution lines), `pre-merge-commit` and `pre-rebase` (protect `main`/`master`),
`pre-push` (`main`/`master`/archives only via `vn publish`, no tags except the CI one, nothing to `upstream`,
`up/*` must be upstream-shaped), `post-checkout` (keeps `CLAUDE.md` available on branches cut from upstream).

Environment: `VN_SETTLE_HOURS` (default 24), `VN_DOTNET`, `VN_BASELINE_DIR`, `VN_BACKUP_DIR`,
`VN_SKIP_GATE` (emergency). Git config `vn.pushArchives` (true = also push `refs/archive/*`).
