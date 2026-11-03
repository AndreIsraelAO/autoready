<p align="center">
  <img src="assets/autoready-icon.png" width="96" alt="">
</p>

<h1 align="center">AutoReady</h1>

<p align="center">
  <b>Go grab a snack. Your match accepts itself.</b><br>
  A free Windows app that accepts your matches and tells you when to come back.<br>
  Works with Dota 2, Counter-Strike 2 and League of Legends.
</p>

<p align="center">
  <a href="https://github.com/hercase/autoready/releases/latest/download/AutoReady.exe"><img src="https://img.shields.io/badge/Download_for_Windows-1E7A52?style=for-the-badge" alt="Download for Windows"></a>
</p>

<p align="center">
  <picture>
    <source media="(prefers-reduced-motion: reduce)" srcset="assets/demo-static.svg">
    <img src="assets/demo.svg" width="700" alt="Animated demo: the Dota 2 "Your game is ready" popup gets accepted automatically, then a Windows notification and a phone notification say the match was accepted.">
  </picture>
</p>

<p align="center">
  <img src="https://img.shields.io/github/v/release/hercase/autoready?label=version" alt="Latest version">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 and 11">
  <img src="https://img.shields.io/github/downloads/hercase/autoready/total" alt="Downloads">
  <img src="https://img.shields.io/github/license/hercase/autoready" alt="MIT license">
  <br>
  <a href="https://hercase.github.io/autoready/"><b>hercase.github.io/autoready</b></a> · <a href="README.es.md">Leer en español</a>
</p>

<table>
  <tr>
    <td valign="top">
      <h3>How it works</h3>
      <ol>
        <li><b>Queue up</b> in your game.</li>
        <li><b>Step away.</b> AutoReady keeps an eye on the game.</li>
        <li>When the match pops, it <b>clicks Accept</b> and lets you know with a sound, a Windows notification and, if you want, your phone.</li>
      </ol>
      <h3>Why it's safe</h3>
      <ul>
        <li><b>It never clicks during a match.</b> Your Steam status and Valve's official Game State Integration tell it when you're playing.</li>
        <li><b>It doesn't touch the game.</b> No memory reading, no injection, no modified game files.</li>
        <li><b>No accounts, ads or telemetry.</b> Open source, and it only goes online for what's listed in the <a href="PRIVACY.md">privacy policy</a>.</li>
      </ul>
    </td>
    <td width="290" align="center" valign="top">
      <img src="assets/panel-en.png" width="270" alt="The AutoReady panel: main switch, pause hotkey, sound and phone notifications">
    </td>
  </tr>
</table>

## Get started

1. **[Download `AutoReady.exe`](https://github.com/hercase/autoready/releases/latest/download/AutoReady.exe)** and open it.
2. Click **Install**. It installs for your user in a few seconds, no admin rights needed.
3. The icon appears next to the clock (it may be hidden under **^**: drag it out to keep it handy).
4. Queue up in your game and step away.

> [!NOTE]
> Windows may show a SmartScreen warning the first time, because AutoReady isn't code-signed yet
> ([why](#code-signing-policy)). Click **More info → Run anyway**.

## Using it

Click the icon to open the panel:

- **The switch at the top** turns accepting on or off. The status next to it tells you what AutoReady is doing.
- **Pause hotkey:** pick a key (F1–F12 or a combination) to pause and resume from anywhere, even mid-game.
- **Phone notifications:** scan the QR code with your phone camera. A [short page](https://hercase.github.io/autoready/phone/) walks you through connecting the free [ntfy](https://ntfy.sh) app (one tap on Android, copy and paste on iPhone).
- **Updates:** when a new version is out, the panel shows an **Update** button. It stays disabled while you're searching for a match or playing, so an update never interrupts you. With automatic updates on, it installs itself once you're back at the menu.
- **More options:** start with Windows, automatic updates and the current version.

Right-click the icon for **Pause/Resume** and **Exit**. The icon turns gray while paused.
AutoReady is meant to stay on: a pause lasts until you resume it or close the game.
It's in English or Spanish depending on your Windows language.

## Supported games

| Game | Setup |
|---|---|
| **[Dota 2](https://hercase.github.io/autoready/dota-2/)** | Nothing to do. If a one-time step is needed, the panel shows **Set up**: it copies the launch option and opens Dota's properties in Steam so you just paste it. |
| **[Counter-Strike 2](https://hercase.github.io/autoready/cs2/)** | Nothing to do. Restart CS2 once after installing AutoReady. |
| **[League of Legends](https://hercase.github.io/autoready/league-of-legends/)** | Nothing to do. AutoReady talks to the League client's local API on your PC (the same one community tools use): it accepts without touching your mouse and does nothing in champ select or in game. |

Playing something else? **[Ask for your game](https://github.com/hercase/autoready/issues/new?template=game-request.yml)**: it takes a minute, and a screenshot of the match found popup helps a lot.

## FAQ

More answers, including exactly what gets installed, on the [website](https://hercase.github.io/autoready/faq/).

<details>
<summary><b>Can I get banned for this?</b></summary>
<br>
AutoReady only looks at the game window and presses Accept, like you would. It doesn't read or change game memory,
doesn't inject anything and doesn't modify game files. The only file it adds is Valve's official Game State Integration
config, which exists for third-party tools. Steam's terms do mention automation software, so the risk isn't zero, but
nothing here touches the match itself.
</details>

<details>
<summary><b>Does it click anything during a match?</b></summary>
<br>
No. Once a match starts it stops looking until you're back in the menu. If it can't tell whether you're in a match, it
runs in a safe mode: it only clicks, and never while you're using your mouse or keyboard in the game.
</details>

<details>
<summary><b>Can I use it to stay AFK?</b></summary>
<br>
No, and please don't. It's there so you don't miss a match while you grab something. Accept, then come back: leaving
hurts the other nine players.
</details>

<details>
<summary><b>It didn't accept my match. What now?</b></summary>
<br>
Check that the switch at the top of the panel is on and that there's no <b>Set up</b> notice. If it still fails,
<a href="https://github.com/hercase/autoready/issues/new">open an issue</a> with your game, screen resolution and
Windows display scale.
</details>

<details>
<summary><b>Does it work with two monitors?</b></summary>
<br>
It should, wherever the game is. Multi-monitor setups are still being tested, so if something's off,
<a href="https://github.com/hercase/autoready/issues/6">let us know</a>.
</details>

<details>
<summary><b>Does it save screenshots?</b></summary>
<br>
During the beta, yes, to improve detection: when it accepts a match, and when a match starts that it didn't accept.
Only the game window, only on your PC (<code>%APPDATA%\AutoReady\samples</code>), and only the latest 15. Nothing is
uploaded. Turn it off with <code>SaveSamples=False</code> in <code>%APPDATA%\AutoReady\autoready.ini</code>.
</details>

<details>
<summary><b>How do I uninstall it?</b></summary>
<br>
<b>Settings → Apps → Installed apps → AutoReady → Uninstall.</b> It removes the app, its shortcuts and the Game State
Integration files, and asks whether to delete your settings too.
</details>

## Code signing policy

AutoReady's releases are **not code-signed yet**. Free certificates for open-source projects go to projects with an
established public track record, and AutoReady is new. We'll apply again as it grows. Until then, you can check any
download yourself:

- Every release is built from this repository by GitHub Actions and scanned with Microsoft Defender.
- Each release publishes its checksum (`AutoReady.exe.sha256`) and carries a GitHub attestation. Check yours with
  `gh attestation verify AutoReady.exe -R hercase/autoready`.

**Privacy:** AutoReady has no accounts, analytics or telemetry. It only connects to GitHub (updates), Steam Community
(your own Steam status) and ntfy (phone notifications, off by default), and each one can be turned off. See the
[privacy policy](PRIVACY.md).

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
  <br>
  <sub>AutoReady is an independent project, not affiliated with or endorsed by the companies behind the games it supports. Game names are trademarks of their owners.</sub>
</p>
