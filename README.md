<p align="center">
  <img src="assets/autoready-icon.png" width="112" alt="">
</p>

<h1 align="center">AutoReady</h1>

<p align="center">
  <b>Queue up. Step away. AutoReady accepts the match for you.</b>
</p>

<p align="center">
  <a href="https://github.com/hercase/autoready/releases"><img src="https://img.shields.io/github/v/release/hercase/autoready?include_prereleases&label=download" alt="Latest version"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 and 11">
  <a href="https://github.com/hercase/autoready/releases"><img src="https://img.shields.io/github/downloads/hercase/autoready/total" alt="Downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/hercase/autoready" alt="MIT license"></a>
</p>

<p align="center">
  <a href="https://github.com/hercase/autoready/releases"><b>Download for Windows</b></a> · <a href="README.es.md">Leer en español</a>
</p>

<p align="center">
  <img src="assets/screenshot-panel.png" width="320" alt="The AutoReady panel">
</p>

## What it does

You hit **Find match** and go grab a drink. When the match pops, AutoReady clicks **Accept** for you and lets you know
(sound, Windows notification and, if you want, your phone) so you're back in time for the pick.

- **Accepts the match** even if the game is behind other windows or minimized.
- **Never clicks during a match:** it uses Valve's official Game State Integration to know when you're in game.
- **Pause it from anywhere** with a hotkey you choose, even while playing.
- **Phone alerts** through the free ntfy app.
- **One small file.** No install, no extra downloads, and it updates itself.

## Supported games

| Game | Status | Setup |
|---|---|---|
| Dota 2 | ✅ | Add `-gamestateintegration` to the launch options (Steam → Dota 2 → Properties) |
| Counter-Strike 2 | ✅ | Nothing to do: just restart CS2 once after opening AutoReady |

Want another game? [Open an issue](https://github.com/hercase/autoready/issues).

## Get started

1. Download `AutoReady.exe` from [Releases](https://github.com/hercase/autoready/releases) and open it.
2. The AutoReady icon shows up next to the clock and the panel opens. That's it: it's running.
3. Queue up in your game and step away.

Windows may show a SmartScreen warning because the app isn't code-signed yet. Click **More info → Run anyway**.

## Using it

Click the icon next to the clock to open the panel. Changes apply right away.

- **Accept matches:** the main switch. Turn it off to pause.
- **Hotkey:** pick a key (for example F8) to pause or resume from anywhere.
- **Alerts:** sound, a heads-up when the match actually starts, and phone notifications.
- **General:** start with Windows and automatic updates.

Right-click the icon for **Open AutoReady**, **Pause/Resume** and **Exit**. The icon turns gray while paused.

## FAQ

**Can I get banned for this?**
AutoReady only looks at the screen and clicks the Accept button, like you would. It doesn't read or change game memory,
doesn't inject anything and doesn't modify game files. The only thing it writes is Valve's official Game State Integration
config file.

**Does it click anything during a match?**
No. Game State Integration tells it when you're in a match, and it stops looking until the match ends.

**Can I use it to stay AFK?**
No, and please don't. It's meant to save you from missing a match while you grab something. Accept, then come back:
abandoning hurts the other nine players.

**Does it work with two monitors?**
It should, wherever the game is. Multi-monitor setups are still being tested; if something's off, please
[let us know](https://github.com/hercase/autoready/issues/6).

**It didn't accept my match.**
Make sure **Accept matches** is on and, for Dota 2, that `-gamestateintegration` is in the launch options.
If it still fails, [open an issue](https://github.com/hercase/autoready/issues) with your game, resolution and Windows
display scale.

## Status

AutoReady is in **beta** (versions 0.x). The roadmap and known issues are in
[Issues](https://github.com/hercase/autoready/issues).

## Contributing

Bug reports and ideas are welcome. To build it yourself or send changes, see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)

---

<p align="center">
  <a href="https://keis.studio">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="assets/keis-wordmark-dark.svg">
      <img src="assets/keis-wordmark-light.svg" height="32" alt="keis.studio">
    </picture>
  </a>
</p>
