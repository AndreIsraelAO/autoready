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
| `src/TrayApp.cs` | Orchestrator: tray icon, scan loop, pause, alerts, hotkey |
| `src/GameStrategy.cs` | Base class for per-game accept strategies and the `IGameHost` interface |
| `src/ScreenStrategy.cs` | Screen-based accepting (Dota 2, CS2) |
| `src/ClientApiStrategy.cs` | Local client API accepting (League of Legends) |
| `src/Panel.cs` | The settings panel (WPF) |
| `src/Detector.cs` | Finds the Accept button in a screenshot |
| `src/Gsi.cs` | Game State Integration (knowing when you're in a match) |
| `src/Games.cs` | Per-game profiles; each one picks its strategy |
| `tests/` | Automated tests and synthetic fixtures |
| `assets/` | Icons and brand assets |

## Tests

```bat
tests.bat
```

Builds the app code with `tests\*.cs` and runs every test (exit code `0` = all passed). They also run on every push and
before every release; a failing test blocks the release. Run them before you commit, and add a test with every bug fix.

## Testing the detector

```bat
dist\AutoReady.exe --test screenshot.png dota2
```

Exit code `0` means the Accept button was found, `1` means it wasn't. It also writes `screenshot.result.png` with the search
area and the detected button marked. Use `cs2` for Counter-Strike 2 screenshots.

During the beta the app also keeps real samples in `%APPDATA%\AutoReady\samples`: `accepted-*.jpg` (the popup area that
triggered a click) and `missed-*` folders (frames from the minute before a match AutoReady didn't accept). They're the
best test material for the detector.

## Principles

- Nothing that could get a player banned: no reading or writing game memory, no injection, no game file changes besides
  the official GSI config.
- Never click during a match.
- No AFK features.

## Commits and releases

- Commit messages in **English**, imperative mood ("Add CS2 support", "Fix tray icon in dark mode").
- Releases are cut by pushing a tag: `git tag v0.3.0 && git push origin v0.3.0`. GitHub Actions builds the exe and publishes
  it with its SHA-256, after the tests pass.
