<p align="center">
  <img src="assets/autoready-icon.png" width="96" alt="">
</p>

<h1 align="center">AutoReady</h1>

<p align="center">
  <b>Buscá partida, andá a hacer otra cosa. AutoReady la acepta por vos.</b><br>
  Auto-accept para Dota 2 y Counter-Strike 2 en Windows.
</p>

<p align="center">
  <a href="https://github.com/hercase/autoready/releases"><img src="https://img.shields.io/badge/Descargar_para_Windows-3FB37F?style=for-the-badge" alt="Descargar para Windows"></a>
</p>

<p align="center">
  <img src="https://img.shields.io/github/v/release/hercase/autoready?include_prereleases&label=versi%C3%B3n" alt="Última versión">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 y 11">
  <img src="https://img.shields.io/github/downloads/hercase/autoready/total?label=descargas" alt="Descargas">
  <img src="https://img.shields.io/github/license/hercase/autoready?label=licencia" alt="Licencia MIT">
  <br>
  <a href="README.md">Read in English</a>
</p>

## Cómo funciona

<table>
  <tr>
    <td align="center" width="33%">🎮<br><b>Buscá partida</b><br>Apretá <i>Buscar partida</i> en Dota 2 o CS2.</td>
    <td align="center" width="33%">☕<br><b>Andá a hacer otra cosa</b><br>Buscá algo de tomar. AutoReady vigila el juego.</td>
    <td align="center" width="33%">✅<br><b>Volvé a tiempo</b><br>Toca <i>Aceptar</i> y te avisa.</td>
  </tr>
</table>

## Qué hace

<table>
  <tr>
    <td valign="top">
      <ul>
        <li><b>Acepta la partida</b> aunque el juego esté tapado por otras ventanas o minimizado.</li>
        <li><b>Nunca hace clicks durante una partida.</b> La integración oficial de Valve (Game State Integration) le avisa cuándo estás jugando.</li>
        <li><b>Te avisa</b> con un sonido, una notificación de Windows y, si querés, en el celular.</li>
        <li><b>Pausalo desde cualquier lado</b> con la tecla que elijas, incluso jugando.</li>
        <li><b>Un solo archivo chico.</b> No se instala, no baja nada más y se actualiza solo.</li>
      </ul>
    </td>
    <td width="280" align="center">
      <img src="assets/screenshot-panel.png" width="260" alt="El panel de AutoReady">
    </td>
  </tr>
</table>

## Juegos compatibles

| Juego | Configuración |
|---|---|
| **Dota 2** | Agregá `-gamestateintegration` en las opciones de lanzamiento (Steam → Dota 2 → Propiedades). |
| **Counter-Strike 2** | Nada. Reiniciá CS2 una vez después de abrir AutoReady. |

¿Querés otro juego? [Pedilo](https://github.com/hercase/autoready/issues).

## Empezar

1. [Bajá `AutoReady.exe`](https://github.com/hercase/autoready/releases) y abrilo.
2. Aparece el ícono de AutoReady al lado del reloj y se abre el panel. Ya está funcionando.
3. Buscá partida en tu juego y andá a hacer otra cosa.

Tocá el ícono cuando quieras para abrir el panel: prender o apagar la aceptación, elegir una tecla para pausar y cómo
querés que te avise. Con click derecho tenés **Pausar/Reanudar** y **Salir**. En pausa, el ícono se pone gris.

> [!NOTE]
> Windows puede mostrar un aviso de SmartScreen porque AutoReady todavía no está firmado. Tocá **Más información →
> Ejecutar de todas formas**.

## Preguntas frecuentes

<details>
<summary><b>¿Me pueden banear por usarlo?</b></summary>
<br>
AutoReady solo mira la pantalla y toca el botón Aceptar, como harías vos. No lee ni cambia la memoria del juego, no inyecta
nada y no modifica archivos del juego. Lo único que escribe es la configuración oficial de Game State Integration de Valve.
</details>

<details>
<summary><b>¿Hace clicks durante la partida?</b></summary>
<br>
No. Game State Integration le avisa cuando estás en partida, y deja de mirar hasta que termina.
</details>

<details>
<summary><b>¿Lo puedo usar para quedarme AFK?</b></summary>
<br>
No, y por favor no lo hagas. Está para que no se te pase una partida mientras vas a buscar algo. Aceptá y volvé: irte
perjudica a los otros nueve jugadores.
</details>

<details>
<summary><b>¿Funciona con dos monitores?</b></summary>
<br>
Debería, esté donde esté el juego. Todavía lo estamos probando con varios monitores; si algo falla,
<a href="https://github.com/hercase/autoready/issues/6">avisanos</a>.
</details>

<details>
<summary><b>No me aceptó la partida. ¿Qué hago?</b></summary>
<br>
Fijate que <b>Aceptar partidas</b> esté prendido en el panel y, en Dota 2, que <code>-gamestateintegration</code> esté en las
opciones de lanzamiento. Si sigue fallando, <a href="https://github.com/hercase/autoready/issues">abrí un issue</a> con el
juego, la resolución y la escala de pantalla de Windows.
</details>

## Estado

AutoReady está en **beta** (versiones 0.x). Lo que viene y los problemas conocidos están en
[Issues](https://github.com/hercase/autoready/issues). ¿Querés compilarlo o colaborar? Empezá por
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
  <sub><a href="LICENSE">Licencia MIT</a></sub>
</p>
