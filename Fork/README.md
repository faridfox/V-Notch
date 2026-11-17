# faridfox/V-Notch — unofficial personal fork

This is a personal fork of **[w0otdev/V-Notch](https://github.com/w0otdev/V-Notch)** by w0otdev & viper1aa.
It is **not affiliated with or endorsed by** the original developers, and it publishes no installers.
Official builds, support and the project itself: https://github.com/w0otdev/V-Notch/releases.
If you like V-Notch, star and support the original project.

## What differs from upstream

The fork follows upstream closely: its `main` branch is always one exact upstream commit (the `master` branch
of this fork) plus a short list of the fork's own commits on top. Fork code lives under `Fork/` and
`Tests/Fork/`; edits inside upstream files are kept to marked one-line hooks (`FORK(faridfox)`).

Build-level differences (see `Fork/msbuild/Fork.targets`):
- local builds never recompile/overwrite upstream's committed native DLL;
- fork builds embed a different update-signing public key, so the in-app updater cannot replace a fork build
  with an official release.

## Ledger of fork changes

Keep this table current in the same commit as each change. Status: `active`, `suspended` (dormant after an
upstream change, needs work), `upstream-candidate`, `offered upstream (<link>)`, `retired`.

| Commit subject | What it does | Upstream files touched | Status |
|---|---|---|---|
| `fork: infrastructure` | workflow tooling, rules, update guard, native-DLL protection | none | active |
| `fork: daily-driver install` | `vn install` / `vn rollback`: the gated fork build is the everyday V-Notch | none | active |
| `fork: run natively on Windows (Git Bash)` | `vn` and the hooks run from Git Bash on Windows (WSL stays a fallback) | none | active |
| `feat(compact-lyrics): lyrics or title and artist in the collapsed notch` | While music shows in the collapsed notch, a line between the album art and the visualizer shows the current lyric line (Spotify) with a per-word fade in reading direction (right to left for Arabic/Hebrew), or "Title — Artist"; the pill resizes to fit each line (150–600 px). Switch: Settings > Advanced > "Show lyrics in the collapsed notch" (fork settings file). Also brings the shared plumbing: composition hook, fork settings, fork texts, UI attach (`Fork/Core/`) | `Services/ServiceConfigurator.cs` (composition hook), `Windows/MainWindow.AnimationHelpers.cs` (`GetCollapsedWidth`) | active |
| `feat(line-bar): press and hold the notch into a thin progress bar` | Press and hold the collapsed notch (450 ms): it hides (upstream's tray "Hide notch") and a thin, fully rounded bar takes its place at the top edge, showing the track's progress, or a plain line when nothing is loaded. One click on the bar (or tray "Show notch") brings the notch back, collapsed. A short click still opens the notch, on release | `Windows/MainWindow.xaml.cs` (`ToggleNotchFromClick`) | active |
| `feat(now-playing): album line and progress knob` | Expanded music view: "Album: <name>" between title and artist (name in the song's colour; hidden for singles and when unknown), and a white knob on the end of the played part of the progress bar. Inserted at runtime into upstream's `MediaPlaybackView` | none | active |

## Licence

V-Notch is licensed under the Apache License 2.0 (`LICENSE`, unchanged). Files added by this fork are
provided under the same licence; files under `Fork/`, `Tests/Fork/`, `CLAUDE.md` and `Directory.Build.targets`
are fork additions, and every modification inside an upstream file is marked `FORK(faridfox)`.
