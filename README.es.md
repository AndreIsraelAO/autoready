<p align="center"><img src="assets/autoready-icon.png" width="128" alt="AutoReady"></p>

# AutoReady

**Buscá partida, andá a hacer otra cosa. AutoReady la acepta por vos.**

Un auto-accept chiquito para **Dota 2** y **Counter-Strike 2** en Windows. Apretás *Buscar partida*, te vas a buscar algo de tomar y AutoReady hace click en **ACEPTAR** cuando aparece la partida. Después te avisa al celular para que vuelvas a tiempo para el pick.

[English](README.md)

## Qué hace

- ✅ Detecta el botón verde **ACEPTAR** y le hace click, aunque Dota esté tapado por otra ventana o minimizado.
- 🛡️ Usa **Game State Integration**, la integración oficial de Valve, para saber cuándo estás en partida, y ahí deja de escanear. Nunca hace clicks dentro del juego.
- 📱 Si querés, te manda avisos al celular con [ntfy](https://ntfy.sh) (gratis, sin cuenta): *"¡Partida aceptada!"* y *"¡Empezó el pick, volvé!"*.
- ⌨️ Atajo de teclado configurable para pausar o reanudar desde cualquier lado, incluso dentro del juego.
- 🪶 Es un solo `.exe` (~150 KB), no se instala y no necesita descargar nada más. Vive en el área de notificación, con un panel chico para configurarlo.
- 🔍 Es 100% código abierto y se compila con el compilador de C# que ya trae Windows.

## ¿Es seguro? ¿Me pueden banear?

AutoReady **solo mira la pantalla y simula un click**, como haría una persona. No inyecta código, no lee la memoria del juego y no modifica archivos de Dota. Lo único que escribe es la config oficial de GSI, en `dota/cfg/gamestate_integration` y en `csgo/cfg`.

No lo uses para quedarte AFK mucho tiempo: aceptá la partida y volvé. Abandonar perjudica a los otros 9 jugadores y te manda a low priority.

## Instalación

1. Bajá `AutoReady.exe` de [Releases](../../releases) (o compilalo vos, ver más abajo) y abrilo.
2. **Dota 2:** en Steam, **Dota 2 → Propiedades → Opciones de lanzamiento**, agregá `-gamestateintegration` y reiniciá Dota.
   **CS2:** no hay que hacer nada, solo reiniciá CS2 una vez para que cargue la config de GSI.
3. Opcional: tocá el ícono de AutoReady, prendé **Celular** y escaneá el QR con la cámara del celular.

Windows SmartScreen puede mostrar una advertencia porque el exe no está firmado. Tocá **Más información → Ejecutar de todas formas**, o compilalo desde el código.

## Cómo se usa

Tocá el ícono para abrir el panel: el interruptor **Aceptar partidas**, el atajo para pausar, los avisos (sonido, arranque de partida, celular) y las opciones generales. Los cambios se aplican al instante. Con click derecho tenés **Abrir AutoReady**, **Pausar/Reanudar** y **Salir**.

| Ícono | Significado |
|---|---|
| 🟢 | Esperando partida (o en partida, sin escanear) |
| ⚪ | En pausa |
| 🟠 | No hay ningún juego abierto |

## Compilar

```bat
build.bat
```

Genera `dist\AutoReady.exe`. Solo necesitás Windows 10/11 (usa el `csc.exe` de .NET Framework 4, que ya viene instalado).

## Problemas

- **No detecta el botón:** con el popup en pantalla, abrí el panel → *General* → *Avanzado* → *Guardar captura*. La imagen muestra el área de búsqueda (en celeste) y el botón detectado (en rojo). Los valores de detección están en `%APPDATA%\AutoReady\autoready.ini`.
- **Probar el detector con una imagen:** `AutoReady.exe --test captura.png dota2` (o `cs2`) genera `captura.result.png` y `.result.txt`.
- **Logs:** `%APPDATA%\AutoReady\autoready.log`.

## Licencia

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
  <sub>Hecho por <a href="https://keis.studio">keis.studio</a></sub>
</p>
