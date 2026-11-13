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

## Licence

V-Notch is licensed under the Apache License 2.0 (`LICENSE`, unchanged). Files added by this fork are
provided under the same licence; files under `Fork/`, `Tests/Fork/`, `CLAUDE.md` and `Directory.Build.targets`
are fork additions, and every modification inside an upstream file is marked `FORK(faridfox)`.
