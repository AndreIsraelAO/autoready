<p align="center"><img src="assets/autoready-icon.png" width="128" alt="AutoReady"></p>

# AutoReady

**Queue up, step away. AutoReady accepts the match for you.**

A tiny auto-accept for **Dota 2** and **Counter-Strike 2** on Windows. Hit *Find Match*, go grab a drink, and AutoReady clicks **ACCEPT** when the match pops — then pings your phone so you come back in time for the pick.

[Español](README.es.md)

## Features

- ✅ Detects the green **ACCEPT** button and clicks it — even if Dota is behind another window or minimized.
- 🛡️ Uses Valve's official **Game State Integration** to know when you are in a match, and stops scanning there. No clicks during your game.
- 📱 Optional push notifications to your phone via [ntfy](https://ntfy.sh) (free, no account): *"Match accepted!"* and *"Pick started, come back!"*.
- ⌨️ Configurable hotkey to pause or resume from anywhere, even in game.
- 🪶 Single `.exe` (~150 KB), no install, no runtime to download. Lives in the notification area, with a small panel to set it up.
- 🔍 100% open source. Builds with the C# compiler that already ships with Windows.

## Is it safe? Will I get banned?

AutoReady **only looks at the screen and simulates a mouse click**, like a person would. It never injects code, reads game memory or modifies game files (the only file it writes is the official GSI config, in `dota/cfg/gamestate_integration` and `csgo/cfg`).

Please don't use it to go AFK for long — accept the match and come back. Abandoning games hurts your 9 teammates and gets you low priority.

## Install

1. Download `AutoReady.exe` from [Releases](../../releases) (or build it yourself, see below) and run it.
2. **Dota 2:** in Steam, **Dota 2 → Properties → Launch Options**, add `-gamestateintegration`, then restart Dota.
   **CS2:** nothing to do, just restart CS2 once so it loads the GSI config.
3. Optional: click the AutoReady icon, turn on **Phone** and scan the QR code with your phone camera.

Windows SmartScreen may warn you because the exe isn't code-signed. Click **More info → Run anyway**, or build it from source.

## Using it

Click the icon to open the panel: the main **Accept matches** switch, the pause hotkey, alerts (sound, match started, phone) and general options. Changes apply right away. Right-click the icon for **Open AutoReady**, **Pause/Resume** and **Exit**.

| Icon | Meaning |
|---|---|
| 🟢 | Waiting for a match (or in a match, not scanning) |
| ⚪ | Paused |
| 🟠 | No game running |

## Build from source

```bat
build.bat
```

Output: `dist\AutoReady.exe`. Requires only Windows 10/11 (uses `csc.exe` from .NET Framework 4, preinstalled).

## Troubleshooting

- **It doesn't detect the button:** open the panel → *General* → *Advanced* → *Save screenshot* while the popup is visible. The image shows the search area (blue) and the detected button (red). Detection thresholds are in `%APPDATA%\AutoReady\autoready.ini`.
- **Test the detector on an image:** `AutoReady.exe --test screenshot.png dota2` (or `cs2`) writes `screenshot.result.png` and `.result.txt`.
- **Logs:** `%APPDATA%\AutoReady\autoready.log`.

## License

MIT

---

<p align="center">
  <a href="https://keis.studio">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="assets/keis-wordmark-dark.svg">
      <img src="assets/keis-wordmark-light.svg" height="36" alt="keis.studio">
    </picture>
  </a>
  <br>
  <sub>Made by <a href="https://keis.studio">keis.studio</a></sub>
</p>
