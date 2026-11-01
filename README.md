<p align="center"><img src="assets/readyup-icon.png" width="128" alt="ReadyUp"></p>

# ReadyUp

**Queue up, step away. ReadyUp accepts the match for you.**

A tiny auto-accept for **Dota 2** and **Counter-Strike 2** on Windows. Hit *Find Match*, go grab a drink, and ReadyUp clicks **ACCEPT** when the match pops — then pings your phone so you come back in time for the pick.

[Español](README.es.md)

## Features

- ✅ Detects the green **ACCEPT** button and clicks it — even if Dota is behind another window or minimized.
- 🛡️ Uses Valve's official **Game State Integration** to know when you are in a match, and stops scanning there. No clicks during your game.
- 📱 Optional push notifications to your phone via [ntfy](https://ntfy.sh) (free, no account): *"Match accepted!"* and *"Pick started, come back!"*.
- 🪶 ~30 KB, single `.exe`, no install, no runtime to download. Lives in the system tray.
- 🔍 100% open source. Builds with the C# compiler that already ships with Windows.

## Is it safe? Will I get banned?

ReadyUp **only looks at the screen and simulates a mouse click**, like a person would. It never injects code, reads game memory or modifies game files (the only file it writes is the official GSI config, in `dota/cfg/gamestate_integration` and `csgo/cfg`).

Please don't use it to go AFK for long — accept the match and come back. Abandoning games hurts your 9 teammates and gets you low priority.

## Install

1. Download `ReadyUp.exe` from [Releases](../../releases) (or build it yourself, see below) and run it.
2. **Dota 2:** in Steam, **Dota 2 → Properties → Launch Options**, add `-gamestateintegration`, then restart Dota.
   **CS2:** nothing to do, just restart CS2 once so it loads the GSI config.
3. Choose which games to watch in the tray menu → **Games**.
4. Optional: right-click the tray icon → **Connect phone…** and follow the steps.

Windows SmartScreen may warn you because the exe isn't code-signed. Click **More info → Run anyway**, or build it from source.

## Tray icon

| Icon | Meaning |
|---|---|
| 🟢 | Armed: waiting for a match (or in a match, not scanning) |
| ⚪ | Paused (double-click the icon to toggle) |
| 🟡 | Dota is not running |

Right-click menu: pause, phone notifications, sound, pick-started alert, debug screenshot, open folder, start with Windows.

## Build from source

```bat
build.bat
```

Output: `dist\ReadyUp.exe`. Requires only Windows 10/11 (uses `csc.exe` from .NET Framework 4, preinstalled).

## Troubleshooting

- **It doesn't detect the button:** right-click → *Save debug screenshot* while the popup is visible. The image shows the search area (blue) and the detected button (red). Detection thresholds are in `%APPDATA%\ReadyUp\readyup.ini`.
- **Test the detector on an image:** `ReadyUp.exe --test screenshot.png dota2` (or `cs2`) writes `screenshot.result.png` and `.result.txt`.
- **Logs:** `%APPDATA%\ReadyUp\readyup.log`.

## License

MIT
