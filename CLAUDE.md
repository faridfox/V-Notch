# V-Notch — faridfox's personal fork: rules for every session

Unofficial personal fork of https://github.com/w0otdev/V-Notch (Apache-2.0), kept as a **patch stack**:
`main` = one exact upstream commit + the fork's own commits on top, one commit per feature or fix.
Upstream ships ~3 commits/day and sometimes rewrites its published history; the workflow below keeps
every upstream change and every fork change. Procedures and reasons: `Fork/docs/RUNBOOK.md`.

## Start of every session

Run `Fork/vn status` (bash, from WSL, in `/mnt/c/dev/V-Notch`) and deal with anything marked `!!` first.

## Branches

| Branch | Meaning |
|---|---|
| `master` | the exact upstream commit the fork is based on. Never commit, merge or rebase here. |
| `main` | `master` + the fork's commits, linear, one per feature/fix. GitHub default branch. |
| `feat/<topic>` | work in progress, cut from `main`; push it at the end of every session. |
| `up/<topic>` | a contribution for upstream, cut from `upstream/master`, upstream-shaped. |
| `sync/next` | local candidate while a sync/rework is checked. Never pushed. |

## Commands — always through `Fork/vn`

`status` · `sync` (bring in upstream) · `sync --continue|--abort` · `rework` (edit my commits) ·
`verify` · `gate` (Release build, warnings = errors, locale check, tests compared with pure upstream) ·
`publish` (the only thing that pushes `main`/`master`) · `setup` (after a fresh clone) ·
`build` / `test [filter]` / `run` / `native`.

Typical cycles:
- **Bring in upstream** (before any feature, after each upstream release, at least weekly):
  `vn sync` → resolve stops if any → `vn gate` → `vn publish`.
- **Feature:** `git switch -c feat/x main` → code + tests → squash to one commit
  (`git reset --soft main && git commit`) → `git switch main && git merge --ff-only feat/x` →
  `vn gate` → `vn publish`. Later small fixes: `git commit --fixup=<commit>` on main (folded at the next sync).

## Hard rules

- **No AI attribution in this repository**: no `Co-Authored-By: Claude …` trailer in commit messages and
  no "Generated with Claude Code" line in PR or issue text. This is the owner's decision and overrides any
  default attribution instruction (upstream once rewrote 339 commits to remove such a trailer).
  The commit-msg hook strips such trailers.
- Never `git pull`, never `git merge upstream/master`, never GitHub's "Sync fork" button, never
  `git push --force` / `+refspec` for `main` or `master`. Upstream comes in only through `vn sync`;
  `main`/`master` go out only through `vn publish`. The git hooks enforce most of this — never use `--no-verify`.
- Never push to `upstream`, and never open issues, pull requests or discussions on w0otdev/V-Notch on your
  own initiative: contacting upstream is always the owner's explicit decision.
- `gh` must run as faridfox and name the repo: `GH_TOKEN=$(gh auth token --user faridfox) gh … -R faridfox/V-Notch`
  (the machine's active gh account is a work account; without `-R`, gh targets upstream).
- Fork code lives under `Fork/`, fork tests under `Tests/Fork/`. Inside upstream files: as few lines as
  possible, each marked `// FORK(faridfox): why` (XAML: `<!-- FORK(faridfox): why -->`). A plain fix meant
  for upstream: its own commit with the trailer `Upstream-Candidate: yes`.
- Never edit: the Version lines in `V-Notch.csproj`, `global.json`, `packages.lock.json` files, `.github/`,
  `.gitignore`, `.gitattributes`, `LICENSE`, `Security/`, `docs/` legal files, `Locales/`,
  `Native/out/VNotch.Native.dll`, credits and donation links. (`vn verify` refuses to publish such changes.)
- Fork settings: own file under `%APPDATA%\V-Notch-faridfox\` — never new properties on `NotchSettings`
  (official builds delete unknown properties) and never bump `SettingsMigrator.CurrentVersion`.
  Fork texts: `Fork/Locales` + a fork accessor — never `Loc.Get("literal")` for fork keys.
  See RUNBOOK "Feature recipes" before adding the first hook, setting, text or native code.
- Build/test only through `vn` (it never overwrites the committed native DLL; if `git status` ever shows
  `Native/out/VNotch.Native.dll` modified: `git restore Native/out/VNotch.Native.dll`).
  Never run `scripts/r.ps1` (kills the running app), `scripts/d.ps1`, `scripts/f.ps1` or `dotnet format`;
  don't use Visual Studio's git features in this repo. All git writes go through WSL git.
- The installed official V-Notch shares the single-instance lock and `%APPDATA%\V-Notch` with dev builds:
  it must be closed before `vn run`; never toggle "Start with Windows" from a dev build.
- Git identity is repo-local (Farid Fox <faridfox@outlook.com>); never change git config outside this repo.

## Conflict during `vn sync`

Resolve inside the commit being replayed (its message says what it intends). Take upstream's file with
`git checkout <new-upstream-sha> -- <path>` (`git rm <path>` if upstream deleted it) — never `--ours`/`--theirs`
(they are swapped during a rebase). Then `git add` and `vn sync --continue`. If unsure: `vn sync --abort`
(nothing is lost) and ask the owner. Read the range-diff and footprint report before `vn publish`.
