# AutoReady — guía para agentes de IA

Leé esto antes de tocar el proyecto. Resume la idea, las decisiones ya tomadas (y por qué) y las trampas conocidas.
El trabajo pendiente está en los issues de GitHub: https://github.com/hercase/autoready/issues

## Qué es
App de Windows que **acepta sola las partidas** de Dota 2 y CS2: el usuario busca partida, se va, y AutoReady hace click en
el botón ACCEPT cuando aparece el popup. Después avisa (sonido, notificación de Windows y opcionalmente al celular) para que
vuelva a tiempo. Producto de **keis.studio** (estudio del dueño del repo). Antes se llamó ReadyUp; el nombre cambió porque
"ReadyUp" ya lo usaban otras apps de gaming.

## Principios que no se negocian
- **Nada que pueda causar un ban:** solo se mira la pantalla y se simula un click, como una persona. Nunca leer o escribir
  memoria del juego, inyectar DLLs ni modificar archivos del juego. Lo único que se escribe en las carpetas de los juegos es
  la config oficial de **GSI** (Game State Integration de Valve).
- **Nunca hacer click dentro de una partida:** con GSI se sabe si el usuario está en partida y ahí no se escanea.
- **No es una herramienta para quedarse AFK.** Se rechazó explícitamente un "anti-AFK" (mover el personaje para evitar la
  detección de inactividad): perjudica a los otros jugadores y esquiva un sistema anti-abuso. No implementarlo.
- **Sin créditos a terceros:** el código y los README no mencionan otros proyectos de auto-accept que se investigaron, y los
  commits/PRs **no llevan** `Co-Authored-By` ni "Generated with ...". Sí va la marca propia keis.studio.
- **Commits en inglés**, en imperativo ("Add CS2 support", "Fix tray icon"), aunque la conversación sea en castellano.
  El historial se reescribió al inglés el 2026-11-01; el anterior quedó en la rama local `backup/spanish-history`.

## Cómo funciona
1. Busca la ventana del juego por nombre de proceso (`dota2`, `cs2`).
2. Captura la ventana con `PrintWindow` (funciona aunque esté tapada; si está minimizada espera a que la barra de tareas
   titile, lo que el juego hace al encontrar partida, y la restaura).
3. **Detector** (`Detector.FindButton`): si el juego tiene un **recorte** del botón (`src/Template.cs`), lo busca por
   correlación normalizada en color, reescalado según la altura de la ventana. Si no, cae a la detección por **color y
   forma** (`Detector.Find`: máscara HSV en la zona central → componentes conectados → el que tiene forma de botón).
   Pide 2 detecciones seguidas antes de aceptar.
4. **Acepta** según el perfil: con **Enter** (`AcceptWithEnter`, Dota 2) y, si al segundo el popup sigue en pantalla,
   con click; o directo con **click** en el centro del botón, devolviendo el mouse a donde estaba.
5. **GSI** (`src/Gsi.cs`): servidor HTTP local (TcpListener en 127.0.0.1:53111) que recibe el estado del juego. Si hay
   `map.game_state` (Dota) o `map.phase` (CS2) = en partida → no escanear. Sin GSI, después de aceptar sigue 90 s y se pausa.
6. **Modo seguro (sin GSI):** 3 detecciones seguidas en el mismo lugar, solo click (nunca Enter: en Dota abriría el chat) y
   no acepta si el juego está al frente y hubo mouse/teclado en los últimos 3 s (`Win32.IdleTime`).
8. **Estado de Steam (Dota, `src/Presence.cs`):** se lee el Rich Presence de la cuenta conectada desde
   `steamcommunity.com/miniprofile/<id>` (en inglés, cada 6 s, solo con Dota abierto). Si dice "Finding A Match" se mira la
   pantalla; en "Main Menu" o cualquier estado de partida, no. Si no se puede leer (perfil privado), se cae a GSI o al modo
   seguro. No avisa el popup en sí: pasa de Finding a Hero Selection cuando todos aceptan.
7. **Protección en partida (panel):** Dota necesita la opción de lanzamiento `-gamestateintegration`. `SteamConfig` lee
   (nunca escribe) `userdata/<cuenta activa>/config/localconfig.vdf` para saber si está; el botón Configurar (aviso del panel, solo si falta el paso) copia las opciones actuales del jugador + la que falta (para reemplazar sin perder nada) y
   abre `steam://gameproperties/570`. No se edita el archivo: Steam lo pisa si está abierto y puede haber cuentas ajenas.

## Sumar juegos (la estrategia)
No hay API oficial de Steam/Valve para el "partida encontrada → aceptar" (pasa por el Game Coordinator), GSI no informa
ese momento y conectarse por fuera (SteamKit) abre otra sesión y saca al jugador del juego. Leer `console.log` con
`-condebug` se descartó por invasivo (exige otra opción de lanzamiento). Por eso cada juego es un **perfil** (`Games.cs`):
1. **Detectar el popup:** por imagen. Lo general es un recorte real del botón en
   `%APPDATA%\AutoReady\templates\<juego>@<alto-de-ventana>.png`; el color/forma queda de respaldo.
2. **Aceptar:** click o tecla (Enter), siempre con verificación y click de respaldo.
3. **Saber si está en partida:** GSI si el juego lo tiene; si no, se pausa sola un rato después de aceptar.

Para sumar un juego: proceso, una captura del popup (sale de las capturas automáticas), el recorte del botón y la acción.

## Estructura
| Archivo | Qué hace |
|---|---|
| `src/Program.cs` | Entrada, instancia única (mutex), modo `--test`, espera tras actualizar |
| `src/TrayApp.cs` | Ícono de la bandeja, menú chico, loop de escaneo, aceptar, avisos, atajo global; implementa `IPanelHost` |
| `src/Panel.cs` | Panel WPF (click izquierdo en el ícono): interruptor en el encabezado, aviso de protección (solo si falta), atajo, sonido, celular (QR colapsable), "Más opciones" cerrado. Menos filas y menos texto a propósito |
| `docs/` | Sitio en GitHub Pages (hercase.github.io/autoready): `index.html` una sola pantalla con la descarga directa; `phone/` guía para conectar ntfy (el QR del panel abre `phone/#<código>`). Estático, EN/ES, sin scripts de terceros |
| `src/Controls.cs` | Tema (claro/oscuro, contraste ≥ 4,5:1) e interruptor/botón accesibles (Tab, Enter/Espacio, Narrador) |
| `src/Strings.cs`, `src/Lang.cs` | Todos los textos en español (voseo) e inglés; idioma de Windows o `Language=es|en` en el ini |
| `src/Games.cs` | Perfil por juego: proceso, carpeta de GSI, clave de estado, colores/forma del botón, textos |
| `src/Detector.cs` | Detección del botón ACCEPT: por recorte (si hay) o por color y forma |
| `src/Template.cs` | Detección por recorte del botón (NCC en color, multi-escala) |
| `src/Samples.cs` | Capturas reales de la beta para calibrar (aceptadas y partidas que se escaparon) |
| `src/Gsi.cs` | Instala la config de GSI en cada biblioteca de Steam y recibe el estado |
| `src/Win32.cs` | P/Invoke: ventanas, captura, foco, click, atajos |
| `src/Toast.cs` | Notificaciones nativas de Windows (WinRT) con logo y botón; fallback a globos de la bandeja |
| `src/Notifier.cs` | Push al celular con ntfy.sh |
| `src/QrCode.cs` | Generador de QR propio (modo byte, nivel M, versiones 1–6), sin librerías ni servicios externos |
| `src/Updater.cs` | Auto-actualización desde GitHub Releases con verificación SHA-256 |
| `src/Config.cs` | `%APPDATA%\AutoReady\autoready.ini` + log |
| `src/Shortcut.cs` | Acceso directo en el menú Inicio (para que aparezca en la búsqueda de Windows) |
| `src/KeisMark.cs` | Wordmark de keis.studio (trazos del SVG oficial) para el pie del panel |
| `assets/` | Íconos (`autoready.ico`, `tray-*.png`, `logo-64.png`, `autoready-icon.png`) y wordmarks de keis |

## Compilar
`build.bat [versión]` → `dist\AutoReady.exe`. Usa el **`csc.exe` de .NET Framework 4 que trae Windows**: no hace falta
instalar nada. Consecuencias:
- **C# 5:** sin `$"..."`, `?.`, `nameof`, `out var`, propiedades `=>`, ni inicializadores de auto-propiedades.
- Fuentes en UTF-8 (`/codepage:65001`), textos con tildes.
- WPF y WinRT se referencian por ruta (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\...`, `System32\WinMetadata\*.winmd`).
- Eventos WinRT: este csc no compila `+=` ni `add_X()` sobre eventos WinRT → se registran por reflexión
  (`typeof(ToastNotification).GetEvent("Activated").AddEventHandler(...)`).
- Si AutoReady está abierto, el build falla porque el exe está en uso: cerrarlo antes.

## Releases y actualizaciones
- Un tag `vX.Y.Z` dispara `.github/workflows/release.yml`: compila con esa versión y publica `AutoReady.exe` +
  `AutoReady.exe.sha256`. Las `0.x` salen como **pre-release**.
- La app consulta `api.github.com/repos/hercase/autoready/releases` (incluye pre-releases), baja el exe del mismo tag,
  verifica el SHA-256, renombra el exe actual a `AutoReady.old.exe`, pone el nuevo y se reinicia. Nunca durante una partida.
- Las builds locales son `0.0.0` y no se actualizan solas.

## Probar
- `AutoReady.exe --test captura.png [dota2|cs2]` → exit 0 si encuentra el botón, 1 si no; guarda `captura.result.png` con el
  área de búsqueda y el botón marcados.
- Hasta ahora se probó con imágenes sintéticas, con un "Dota falso" (un exe llamado `dota2.exe` que muestra el popup y
  registra clicks) y con requests falsos de GSI. **Falta probar con popups reales** (issue #6). El color del botón de CS2
  es una suposición.
- **Capturas reales (beta, `src/Samples.cs`):** en `%APPDATA%\AutoReady\samples` se guardan `accepted-<juego>-<fecha>.png`
  (la imagen con la que se decidió el click) y carpetas `missed-<juego>-<fecha>\` con las imágenes reducidas del último
  minuto cuando GSI ve que arrancó una partida que AutoReady no aceptó. Sirven de fixtures para #6. Se conservan
  15 entradas (JPG chicos); se apaga con `SaveSamples=False` en el ini.
- Para revisar el panel sin mostrar ventanas: compilar un programa aparte que cree `Panel` con un `IPanelHost` falso, lo
  muestre fuera de la pantalla y lo guarde con `RenderTargetBitmap`.

## UI y marca
- Sigue las guías de Microsoft: click izquierdo en el ícono abre el panel; click derecho = menú mínimo (Abrir, Pausar/
  Reanudar, Salir); los cambios se aplican al instante; interruptores para sí/no; tema claro/oscuro según Windows.
- Ícono: check con flecha impreso en planchas CMY corridas sobre `ink` (#09090b), el lenguaje visual de keis.studio.
  En pausa, el ícono de la bandeja pasa a gris. Windows usa ese mismo ícono en los globos de notificación.
- Marca keis.studio: repo privado `hercase/keis-studio` (`brand/logo/README.md`, `brand/identity.md`) y
  https://keis.studio/brand. En tamaños chicos los colores de la `k` solo se ven con el corrimiento exagerado
  (como `mark-*-small`). Los paths del SVG se rellenan con **nonzero** (en WPF prefijo `F1`); con el evenodd por defecto
  quedan huecos en la `t` y la `e`.
- Todo texto visible va en `Strings.cs` (`S.*`), en español rioplatense (voseo) e inglés; nunca literales en la UI. Los logs quedan en un solo idioma. Textos genéricos: el nombre del juego entra como parámetro.

## Celular (ntfy)
- Código aleatorio `ar-xxxxxxxx` (sin letras ambiguas). El QR del panel es `https://ntfy.sh/<código>`, que cualquier cámara
  abre y que ya suscribe en la versión web.
- La app de ntfy para **iPhone no soporta links ni QR**; en Android funciona `ntfy://ntfy.sh/<código>`. Mejorar esto es #1.

## Trampas conocidas
- Si un agente abre la app desde una terminal aislada (sandbox), lo que la app escribe en `%APPDATA%` puede no ser visible
  para el usuario. Para probar de verdad, que el usuario abra el exe.
- No mover el mouse ni robar el foco con pruebas mientras el usuario juega: avisar antes de pruebas que hacen click.
- PowerShell 5.1 lee UTF-8 sin BOM como ANSI: no usar `Get-Content | Set-Content` para editar archivos con tildes.
- La guía de bandeja de Microsoft es de Windows 7 pero sigue siendo la única oficial; las de configuración, toggles y Mica
  son de Windows 11.

## Pendiente (issues)
#1 celular sin códigos · #4 README · #5 instalador · #8 firma (SignPath) · #9 restos de accesibilidad y DPI ·
#6 confiabilidad (popups reales, dos monitores).
