# Contributing to AutoReady

Thanks for helping. Bug reports and ideas go in [Issues](https://github.com/hercase/autoready/issues); for code changes,
open a pull request.

## Build

```bat
build.bat
```

Output: `dist\AutoReady.exe`. It builds with the C# compiler that ships with Windows (.NET Framework 4), so there's nothing
to install. That compiler only supports **C# 5**: no `$"..."`, `?.`, `nameof`, `out var` or expression-bodied members.

`build.bat 1.2.3` stamps a version; without it the build is `0.0.0` and doesn't auto-update.

Close AutoReady before building, or the compiler can't overwrite the exe.

## Project layout

The full architecture, design decisions and known pitfalls are in [AGENTS.md](AGENTS.md). In short:

| Path | What's there |
|---|---|
| `src/TrayApp.cs` | Tray icon, scan loop, accepting, alerts, hotkey |
| `src/Panel.cs` | The settings panel (WPF) |
| `src/Detector.cs` | Finds the Accept button in a screenshot |
| `src/Gsi.cs` | Game State Integration (knowing when you're in a match) |
| `src/Games.cs` | Per-game settings (Dota 2, CS2) |
| `assets/` | Icons and brand assets |

## Testing the detector

```bat
dist\AutoReady.exe --test screenshot.png dota2
```

Exit code `0` means the Accept button was found, `1` means it wasn't. It also writes `screenshot.result.png` with the search
area and the detected button marked. Use `cs2` for Counter-Strike 2 screenshots.

During the beta the app also keeps real samples in `%APPDATA%\AutoReady\samples`: `accepted-*.png` (the frame that
triggered a click) and `missed-*` folders (frames from the 90 seconds before a match AutoReady didn't accept). They're the
best test material for the detector.

## Principles

- Nothing that could get a player banned: no reading or writing game memory, no injection, no game file changes besides
  the official GSI config.
- Never click during a match.
- No AFK features.

## Commits and releases

- Commit messages in **English**, imperative mood ("Add CS2 support", "Fix tray icon in dark mode").
- Releases are cut by pushing a tag: `git tag v0.3.0 && git push origin v0.3.0`. GitHub Actions builds the exe and publishes
  it with its SHA-256. Versions `0.x` are published as pre-releases.
