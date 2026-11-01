# AutoReady — estilo de marca

Cómo se ve y cómo habla AutoReady. Vale para la web (`docs/`), la app, las capturas y la imagen para redes.
Es un producto de **keis.studio**: comparte su base (ink, paper y el corrimiento de planchas del ícono) y le suma dos
colores propios: **verde** y **celeste**.

## La idea
"Listo para jugar." El **verde** es **listo / aceptado**: el botón de descarga, el Aceptar del popup, el interruptor
prendido. El **celeste** es **la señal**: la partida encontrada, el aviso, la información, el foco del teclado. Todo lo
demás es negro tinta y texto claro. Juntos aparecen en el ícono y en el degradé del titular.

## Ícono
Check con flecha en **paper** sobre ink. Detrás de la cola, dos bloques corridos a los costados (celeste y verde): el
corrimiento de planchas de keis.studio convertido en estela. La flecha va siempre en blanco y entera; los colores solo
en los bloques, que son sólidos y por eso se siguen viendo a 16 px (con planchas finas pegadas al blanco no se veían).

Tres archivos fuente:
- `assets/autoready-icon.svg`: el ícono (app, web, imagen para redes, `.ico` desde 40 px).
- `assets/autoready-icon-small.svg`: el mismo con caja pero el dibujo más grande, para 32 px o menos (favicon, `.ico`
  chico, logo del header y del footer).
- `assets/autoready-tray.svg`: la bandeja. Sin caja, como los demás íconos de la barra, y con contorno ink para que la
  flecha blanca se vea también con la barra de tareas clara. En pausa la app la pasa a gris.

Los PNG y el `.ico` salen de esos SVG: se renderizan a 1024 px (Edge headless) y se achican con bicúbico de alta calidad.
Renderizar directo a tamaños chicos con Edge recorta la imagen (tiene un ancho mínimo de ventana).

## Color
Solo modo oscuro en la web. La app sigue a Windows; en claro usa los mismos tonos un paso más oscuros.

| Token | Hex | Para qué |
|---|---|---|
| `ink` / `--bg` | `#09090B` | Fondo de todo (el de keis.studio) |
| `--bg-2` | `#0F0F12` | Fondo del panel de la app |
| `--card` | `#141418` | Tarjetas |
| `--stroke` / `--stroke-2` | `#26262C` / `#34343C` | Bordes y divisores |
| `paper` / `--text` | `#F5F4F0` | Texto principal y la flecha del ícono |
| `--muted` | `#9A9AA3` | Texto secundario (7:1 sobre ink) |
| `--dim` | `#7C7C86` | Meta y rótulos (4.8:1, no bajar de acá) |
| **Verde** `--ac` | `#10B981` | Rellenos: botón principal, Aceptar, interruptores. Texto encima en `#022C1E` (6.6:1) |
| Verde claro `--ac-text` | `#34D399` | Verde como texto o ícono sobre ink |
| Verde profundo `--ac-2` | `#059669` | Borde inferior del botón, presionado. En tema claro el relleno es `#047857` con blanco (5.5:1) |
| **Celeste** `--sky` | `#38BDF8` | Partida encontrada, avisos, foco del teclado, etiqueta beta |
| Celeste claro `--sky-text` | `#7DD3FC` | Celeste como texto sobre ink |

Reglas:
- Verde = acción y listo. Celeste = señal. No se cambian de rol.
- Nada de lima, naranja ni CMY completo: se probaron y la marca se vuelve ruidosa o pierde coherencia.
- El verde nunca lleva texto blanco en oscuro: siempre el verde muy oscuro `#022C1E`.
- Los neutros llevan un toque frío, como los de la tabla. Nada de grises puros.

## Tipografía
Sin fuentes externas: la del sistema, que en Windows es la familia de la app.
- **Titulares:** `Segoe UI Variable Display`, 700, interletra -0.04em, interlineado 1. Grandes: el hero va de 40 a 88 px.
  La segunda mitad del titular lleva el degradé "flow": manchas verdes y celestes que flotan dentro del texto (ciclo de
  3 s, brillo detrás al 25 %). Se mueven por dentro, así que el ancho del texto nunca cambia.
- **Texto:** `Segoe UI Variable Text`, 16 px, interlineado 1.55. Bajadas de 18 a 20 px en `--muted`.
- **Mono** (rótulos, números de pasos, el reloj y el popup de la escena): `Cascadia Mono`, 12 px, mayúsculas con +0.08em
  en los rótulos ("SEGURO POR DISEÑO").
- Titulares de sección en dos tonos: la frase en `--text` y el remate en `--dim` ("Hace lo que harías vos. *Nada más.*").

## Forma
- Radios: 12 px controles, 18 px tarjetas, 24 px bloques grandes (escena, cierre).
- Bordes de 1 px en vez de sombras para separar; las sombras grandes solo para lo que "flota" (escena, panel, celular).
- Fondo del hero: halo verde difuso más una grilla de 56 px que se desvanece.

## Movimiento
- La pieza central es la **escena**: un radar busca partida, detecta un contacto (celeste), salta el popup y "PARTIDA
  ENCONTRADA" se descifra (fuente mono: el ancho no cambia), se acepta (verde) y llegan los avisos a la PC y al celular.
- Nada de efectos que cambien el ancho del texto en titulares: el descifrado ahí hacía saltar las líneas.
- Tarjetas: un brillo verde sigue al mouse (spotlight) y el borde se enciende.
- Entradas al hacer scroll: subir 24 px y aparecer, 0.7 s, curva `cubic-bezier(.2,.8,.2,1)`.
- Botón: sube 2 px al pasar el mouse y se achica al apretarlo.
- Con "reducir movimiento" no se anima nada: la escena queda en el final (aceptada y avisada).

## Voz
- Corta, directa y tranquila. Como un amigo que te cubre mientras vas a buscar algo.
- Castellano rioplatense con voseo ("Andá tranquilo. La partida se acepta sola.") e inglés simple. Siempre los dos.
- Se habla de lo que te pasa a vos (te perdés la partida) y de seguridad (nunca toca la memoria del juego, nunca hace click
  en partida), no de cómo funciona por dentro.
- Nada de exageraciones de marketing ni de "100% indetectable". Es seguro porque hace poco, y eso se dice así.

## Estructura de una página
Encabezado fijo (logo, links, botón de descarga que aparece al bajar, EN/ES) → hero centrado (titular, bajada, una descarga,
meta, logos de los juegos, aviso de SmartScreen) → escena del producto (radar → popup → aceptada → avisos) →
"Seguro por diseño" (4 tarjetas con spotlight) → "Lista en un minuto" (3 pasos + captura del panel) → FAQ de 5 preguntas en
dos columnas → cierre con otra descarga → pie corto (logo, 3 links, aviso legal en una línea, "Un producto de keis.studio").

Cada sección responde una duda de quien está por descargar; si no responde ninguna, no va. Nada de info repetida.

## Pendiente
- Volver a sacar las capturas del panel (`docs/assets/panel-*.png`, `assets/panel-*.png`) con el tema nuevo, y la imagen
  para redes (`docs/assets/og-image.png`).
- `faq/` y `phone/` todavía usan el estilo anterior (con tema claro).
