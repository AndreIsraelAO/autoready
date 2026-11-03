# Contributing to AutoReady

Thanks for thinking about helping. AutoReady is a small Windows app that accepts your Dota 2, CS2 and League of Legends
matches while you step away, and it's built to be easy to read and change. You don't need to be an expert: fixing a typo,
translating a string or adding a test is a real contribution.

**Where to start**

- Browse [good first issues](https://github.com/hercase/autoready/labels/good%20first%20issue) and
  [help wanted](https://github.com/hercase/autoready/labels/help%20wanted). Each one lists the files to touch and what
  "done" looks like.
- Comment on the issue before you start so nobody duplicates the work. Questions are welcome there too.
- Bug reports and ideas go in [Issues](https://github.com/hercase/autoready/issues); for code changes, open a pull request.
- Small, focused pull requests get reviewed fastest. If you're planning something big, open an issue first.

You need Windows 10/11 and nothing else: no Visual Studio, no SDK.

## Build

```bat
build.bat
```

Output: `dist\AutoReady.exe`. It builds with the C# compiler that ships with Windows (.NET Framework 4), so there's nothing
to install. That compiler only supports **C# 5**: no `$"..."`, `?.`, `nameof`, `out var` or expression-bodied members.

`build.bat 1.2.3` stamps a version; without it the build is `0.0.0` and doesn't auto-update.

Close AutoReady before building, or the compiler can't overwrite the exe.

## Adding a game

Each game is a **profile** in `src/Games.cs` (process name, GSI folder if the game has one, button color/shape, texts) that
picks one of two strategies:

- `ScreenStrategy`: looks at the game window and presses Accept (Dota 2, CS2).
- `ClientApiStrategy`: uses the game client's local API, no mouse or keyboard (League of Legends).

For a screen-based game:

1. Add a `GameProfile` next to the existing ones and list it in `GameProfile.All`. Copy `Cs2` or `Deadlock` as a starting point.
2. Get a real screenshot of the match popup (the beta saves them in `%APPDATA%\AutoReady\samples`), check it with
   `dist\AutoReady.exe --test shot.png <game-id>`, and add it to `tests\fixtures` with a test in `tests\Tests.cs`.
3. Optionally crop the Accept button into `templates\<game>@<window-height>.png`; the color/shape detector is the fallback.
4. Keep the profile **off by default** (`EnabledByDefault = false`) until it has been checked against a real popup.
5. Add every user-visible string to `src/Strings.cs` in Spanish and English; the tests check both.

If a game needs a different mechanism, add a new strategy instead of changing the existing ones. Read the architecture
notes in [AGENTS.md](AGENTS.md) first.

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

## Rules that are not negotiable

A pull request that breaks one of these will be declined, however good the code is.

- **Nothing that could get a player banned:** only look at the screen (or the game's official local client API) and
  simulate a click or key like a person would. No reading or writing game memory, no DLL injection, no game file changes
  besides the official GSI config.
- **Never click or press keys during a match.** Check "paused" and "in a match" right before every action.
- **No AFK features:** nothing that moves the character or fakes activity to dodge inactivity detection.
- **No new network traffic** beyond what already exists (GitHub releases, Steam public profile, ntfy.sh if the user turns
  it on), and no telemetry.
- **No third-party libraries or services.** The build uses only what ships with Windows.

## Style

- C# 5 only (see Build). All user-facing text goes in `src/Strings.cs`, in Spanish (voseo) and English, never as literals in UI code.
- Match the surrounding code; comments explain *why*, not *what*.

## Commits and releases

- Commit messages in **English**, imperative mood ("Add CS2 support", "Fix tray icon in dark mode").
- Releases are cut by pushing a tag: `git tag v0.3.0 && git push origin v0.3.0`. GitHub Actions builds the exe and publishes
  it with its SHA-256, after the tests pass.

## En español (resumen)

Gracias por querer ayudar. Con Windows alcanza: `build.bat` compila (`dist\AutoReady.exe`, sin instalar nada, C# 5) y
`tests.bat` corre las pruebas. Empezá por los issues con la etiqueta
[good first issue](https://github.com/hercase/autoready/labels/good%20first%20issue) y avisá en el issue antes de arrancar.
Para sumar un juego se agrega un perfil en `src/Games.cs` (ver "Adding a game"), con una captura real del popup y su prueba.
Reglas que no se negocian: nada que pueda causar un ban (solo mirar la pantalla y simular un click, nunca tocar la memoria
ni los archivos del juego), nunca hacer click en partida y nada de funciones para quedarse AFK. Los textos van en
`Strings.cs` en español (voseo) e inglés. Los commits van en inglés. Se puede escribir en español en los issues.
