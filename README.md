<p align="center">
  <img src="assets/autoready-icon.png" width="96" alt="">
</p>

<h1 align="center">AutoReady</h1>

<p align="center">
  <b>Queue up. Step away. AutoReady accepts the match for you.</b><br>
  Match auto-accept for your Steam games on Windows. Works with Dota 2 and Counter-Strike 2.
</p>

<p align="center">
  <a href="https://github.com/hercase/autoready/releases"><img src="https://img.shields.io/badge/Download_for_Windows-3FB37F?style=for-the-badge" alt="Download for Windows"></a>
</p>

<p align="center">
  <img src="https://img.shields.io/github/v/release/hercase/autoready?include_prereleases&label=version" alt="Latest version">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 and 11">
  <img src="https://img.shields.io/github/downloads/hercase/autoready/total" alt="Downloads">
  <img src="https://img.shields.io/github/license/hercase/autoready" alt="MIT license">
  <br>
  <a href="README.es.md">Leer en español</a>
</p>

## How it works

<table>
  <tr>
    <td align="center" width="33%">🎮<br><b>Queue up</b><br>Hit <i>Find match</i> in your game.</td>
    <td align="center" width="33%">☕<br><b>Step away</b><br>Grab a drink. AutoReady keeps an eye on the game.</td>
    <td align="center" width="33%">✅<br><b>Come back in time</b><br>It clicks <i>Accept</i> and lets you know.</td>
  </tr>
</table>

## Features

<table>
  <tr>
    <td valign="top">
      <ul>
        <li><b>Accepts the match</b> even if the game is behind other windows or minimized.</li>
        <li><b>Never clicks during a match.</b> Valve's official Game State Integration tells it when you're in game.</li>
        <li><b>Lets you know</b> with a sound, a Windows notification and, if you want, your phone.</li>
        <li><b>Pause it from anywhere</b> with a hotkey you choose, even while playing.</li>
        <li><b>One small file.</b> No install, no extra downloads, and it updates itself.</li>
      </ul>
    </td>
    <td width="280" align="center">
      <img src="assets/screenshot-panel.png" width="260" alt="The AutoReady panel">
    </td>
  </tr>
</table>

## Supported games

| Game | Setup |
|---|---|
| **Dota 2** | Open the panel and click **Activate** next to *In-match protection*: it opens Dota's properties in Steam with the option ready to paste. |
| **Counter-Strike 2** | Nothing to do. Restart CS2 once after opening AutoReady. |

Want another game? [Ask for it](https://github.com/hercase/autoready/issues).

## Get started

1. [Download `AutoReady.exe`](https://github.com/hercase/autoready/releases) and open it.
2. The AutoReady icon shows up next to the clock and the panel opens. It's already running.
3. Queue up in your game and step away.

Click the icon any time to open the panel: turn accepting on or off, pick a pause hotkey, and choose how you want to be
notified. Right-click it for **Pause/Resume** and **Exit**. The icon turns gray while paused.

> [!NOTE]
> Windows may show a SmartScreen warning because AutoReady isn't code-signed yet. Click **More info → Run anyway**.

## FAQ

<details>
<summary><b>Can I get banned for this?</b></summary>
<br>
AutoReady only looks at the screen and clicks the Accept button, like you would. It doesn't read or change game memory,
doesn't inject anything and doesn't modify game files. The only file it writes is Valve's official Game State Integration
config.
</details>

<details>
<summary><b>Does it click anything during a match?</b></summary>
<br>
No. Game State Integration tells it when you're in a match, and it stops looking until the match is over. In games that support it, it also reads your Steam
status (the "Finding a match" you see in Steam) and only looks at the screen while you're queuing. If neither is available, it runs in a safe mode: it never accepts while you're using your mouse or keyboard in the game, and it only clicks.
</details>

<details>
<summary><b>Can I use it to stay AFK?</b></summary>
<br>
No, and please don't. It's there so you don't miss a match while you grab something. Accept, then come back: leaving hurts
the other nine players.
</details>

<details>
<summary><b>Does it save screenshots?</b></summary>
<br>
During the beta, yes, to make detection better: when it accepts a match, and when a match starts that it didn't accept.
Only the game window, only on your PC (<code>%APPDATA%\AutoReady\samples</code>), and it keeps the latest 15. Nothing is
uploaded. You can turn it off with <code>SaveSamples=False</code> in <code>%APPDATA%\AutoReady\autoready.ini</code>.
</details>

<details>
<summary><b>Does it work with two monitors?</b></summary>
<br>
It should, wherever the game is. Multi-monitor setups are still being tested, so if something's off,
<a href="https://github.com/hercase/autoready/issues/6">let us know</a>.
</details>

<details>
<summary><b>It didn't accept my match. What now?</b></summary>
<br>
Check that <b>Accept matches</b> is on in the panel and that <b>In-match protection</b> says it's active. If it still fails, <a href="https://github.com/hercase/autoready/issues">open an issue</a> with your game,
screen resolution and Windows display scale.
</details>

## Status

AutoReady is in **beta** (versions 0.x). See what's coming and known issues in
[Issues](https://github.com/hercase/autoready/issues). Want to build it or help out? Start with
[CONTRIBUTING.md](CONTRIBUTING.md).

<br>

<p align="center">
  <a href="https://keis.studio">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="assets/keis-wordmark-dark-tight.svg">
      <img src="assets/keis-wordmark-light-tight.svg" height="40" alt="keis.studio">
    </picture>
  </a>
  <br>
  <sub><a href="LICENSE">MIT License</a></sub>
</p>
