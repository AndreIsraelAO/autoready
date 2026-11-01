<p align="center">
  <img src="assets/autoready-icon.png" width="112" alt="">
</p>

<h1 align="center">AutoReady</h1>

<p align="center">
  <b>Buscá partida, andá a hacer otra cosa. AutoReady la acepta por vos.</b>
</p>

<p align="center">
  <a href="https://github.com/hercase/autoready/releases"><img src="https://img.shields.io/github/v/release/hercase/autoready?include_prereleases&label=descargar" alt="Última versión"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 y 11">
  <a href="https://github.com/hercase/autoready/releases"><img src="https://img.shields.io/github/downloads/hercase/autoready/total?label=descargas" alt="Descargas"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/hercase/autoready?label=licencia" alt="Licencia MIT"></a>
</p>

<p align="center">
  <a href="https://github.com/hercase/autoready/releases"><b>Descargar para Windows</b></a> · <a href="README.md">Read in English</a>
</p>

<p align="center">
  <img src="assets/screenshot-panel.png" width="320" alt="El panel de AutoReady">
</p>

## Qué hace

Apretás **Buscar partida** y te vas a buscar algo de tomar. Cuando aparece la partida, AutoReady toca **Aceptar** por vos y
te avisa (sonido, notificación de Windows y, si querés, en el celular) para que vuelvas a tiempo para el pick.

- **Acepta la partida** aunque el juego esté tapado por otras ventanas o minimizado.
- **Nunca hace clicks durante una partida:** usa la integración oficial de Valve (Game State Integration) para saber cuándo
  estás jugando.
- **Pausalo desde cualquier lado** con la tecla que elijas, incluso jugando.
- **Avisos al celular** con la app gratuita ntfy.
- **Un solo archivo chico.** No se instala, no baja nada más y se actualiza solo.

## Juegos compatibles

| Juego | Estado | Configuración |
|---|---|---|
| Dota 2 | ✅ | Agregá `-gamestateintegration` en las opciones de lanzamiento (Steam → Dota 2 → Propiedades) |
| Counter-Strike 2 | ✅ | Nada: solo reiniciá CS2 una vez después de abrir AutoReady |

¿Querés otro juego? [Abrí un issue](https://github.com/hercase/autoready/issues).

## Empezar

1. Bajá `AutoReady.exe` de [Releases](https://github.com/hercase/autoready/releases) y abrilo.
2. Aparece el ícono de AutoReady al lado del reloj y se abre el panel. Listo: ya está funcionando.
3. Buscá partida en tu juego y andá a hacer otra cosa.

Windows puede mostrar un aviso de SmartScreen porque la app todavía no está firmada. Tocá **Más información → Ejecutar de
todas formas**.

## Cómo se usa

Tocá el ícono al lado del reloj para abrir el panel. Los cambios se aplican al instante.

- **Aceptar partidas:** el interruptor principal. Apagalo para pausar.
- **Atajo:** elegí una tecla (por ejemplo F8) para pausar o reanudar desde cualquier lado.
- **Avisos:** sonido, aviso cuando la partida arranca de verdad y notificaciones al celular.
- **General:** iniciar con Windows y actualizaciones automáticas.

Con click derecho en el ícono tenés **Abrir AutoReady**, **Pausar/Reanudar** y **Salir**. En pausa, el ícono se pone gris.

## Preguntas frecuentes

**¿Me pueden banear por usarlo?**
AutoReady solo mira la pantalla y toca el botón Aceptar, como harías vos. No lee ni cambia la memoria del juego, no inyecta
nada y no modifica archivos del juego. Lo único que escribe es el archivo de configuración oficial de Game State
Integration de Valve.

**¿Hace clicks durante la partida?**
No. Game State Integration le avisa cuando estás en partida, y deja de mirar hasta que termina.

**¿Lo puedo usar para quedarme AFK?**
No, y por favor no lo hagas. Sirve para que no se te pase una partida mientras vas a buscar algo. Aceptá y volvé:
abandonar perjudica a los otros nueve jugadores.

**¿Funciona con dos monitores?**
Debería, esté donde esté el juego. Todavía lo estamos probando con varios monitores; si algo falla,
[avisanos](https://github.com/hercase/autoready/issues/6).

**No me aceptó la partida.**
Fijate que **Aceptar partidas** esté prendido y, en Dota 2, que `-gamestateintegration` esté en las opciones de
lanzamiento. Si sigue fallando, [abrí un issue](https://github.com/hercase/autoready/issues) con el juego, la resolución y
la escala de pantalla de Windows.

## Estado

AutoReady está en **beta** (versiones 0.x). Lo que viene y los problemas conocidos están en
[Issues](https://github.com/hercase/autoready/issues).

## Colaborar

Reportes de errores e ideas son bienvenidos. Para compilarlo vos o mandar cambios, mirá [CONTRIBUTING.md](CONTRIBUTING.md).

## Licencia

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
