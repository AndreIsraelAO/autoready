namespace AutoReady
{
    // Todos los textos que ve el usuario, en espanol (voseo) e ingles. El idioma lo decide Lang.
    // Las frases con datos usan string.Format para que cada idioma ordene las palabras a su manera.
    // Los logs quedan en un solo idioma (no se traducen).
    static class S
    {
        static string T(string es, string en) { return Lang.T(es, en); }

        // Estado (encabezado del panel y tooltip)
        public static string Starting { get { return T("Iniciando…", "Starting…"); } }
        public static string StatusIdle { get { return T("Listo: abrí tu juego y buscá partida", "Ready: open your game and queue up"); } }
        public static string StatusPaused { get { return T("En pausa", "Paused"); } }
        public static string StatusPausedKey(string key) { return string.Format(T("En pausa · {0} para reanudar", "Paused · {0} to resume"), key); }
        public static string StatusSearching(string game) { return string.Format(T("Esperando partida en {0}", "Waiting for a match in {0}"), game); }
        public static string StatusInMatch(string game) { return string.Format(T("En partida de {0}", "In a {0} match"), game); }
        public static string TooltipIdle { get { return T("AutoReady: activo, esperando un juego", "AutoReady: on, waiting for a game"); } }
        public static string TooltipPaused(string key) { return key == null ? T("AutoReady: en pausa", "AutoReady: paused") : string.Format(T("AutoReady: en pausa ({0} para reanudar)", "AutoReady: paused ({0} to resume)"), key); }
        public static string TooltipSearching(string game) { return string.Format(T("AutoReady: activo, esperando partida en {0}", "AutoReady: on, waiting for a match in {0}"), game); }
        public static string TooltipInMatch(string game) { return string.Format(T("AutoReady: en partida de {0}", "AutoReady: in a {0} match"), game); }

        // Bienvenida (primer uso)
        public static string WelcomeTitle { get { return T("Así funciona", "How it works"); } }
        public static string WelcomeText { get { return T("Buscá partida y alejate: AutoReady la acepta y te avisa. Su ícono está junto al reloj (si no lo ves, buscalo en ^).",
                                                         "Queue up and step away: AutoReady accepts the match and lets you know. Its icon lives next to the clock (under ^ if you don't see it)."); } }
        public static string Close { get { return T("Cerrar", "Close"); } }

        // Panel
        public static string AcceptTitle { get { return T("Aceptar partidas automáticamente", "Accept matches automatically"); } }
        public static string HotkeyTitle { get { return T("Tecla de pausa", "Pause hotkey"); } }
        public static string HotkeyNone { get { return T("Elegir", "Choose"); } }
        public static string HotkeyPress { get { return T("Apretá una tecla…", "Press a key…"); } }
        public static string HotkeyPressHint { get { return T("Esc cancela · Supr la quita", "Esc to cancel · Delete to clear"); } }
        public static string HotkeyTaken { get { return T("Otra app usa esa tecla. Probá con otra.", "Another app uses that key. Try another."); } }
        public static string HotkeyNeedsModifier { get { return T("Usá una tecla F o combiná con Ctrl, Alt o Shift.", "Use an F key or combine with Ctrl, Alt or Shift."); } }
        public static string ProtectionMissing(string game) { return string.Format(T("{0} necesita un paso, una sola vez, para que AutoReady nunca haga click en partida.", "{0} needs a one-time setup so AutoReady never clicks during a match."), game); }
        public static string ProtectionButton { get { return T("Configurar", "Set up"); } }
        public static string ProtectionCopied(string game) { return string.Format(T("Copiado. En la ventana de Steam que se abrió, tocá el campo Opciones de lanzamiento, apretá Ctrl+A y Ctrl+V, y reiniciá {0}.", "Copied. In the Steam window that opened, click the Launch Options box, press Ctrl+A, then Ctrl+V, and restart {0}."), game); }

        public static string Sound { get { return T("Sonido", "Sound"); } }
        public static string PhoneTitle { get { return T("Avisos al celular", "Phone notifications"); } }
        public static string PhoneShowCode { get { return T("Mostrar código", "Show code"); } }
        public static string PhoneScan { get { return T("Escaneá con la cámara", "Scan with your camera"); } }
        public static string PhoneOrCode { get { return T("o sumá este código en ntfy:", "or add this code in ntfy:"); } }
        public static string CopyCode { get { return T("Copiar código", "Copy code"); } }
        public static string PhoneSecret { get { return T("No muestres este código en stream.", "Don't show this code on stream."); } }
        public static string PhoneTest { get { return T("Enviar prueba", "Send test"); } }
        public static string PhoneSending { get { return T("Enviando…", "Sending…"); } }
        public static string PhoneSent { get { return T("Enviada. Mirá el celular.", "Sent. Check your phone."); } }
        public static string PhoneFailed { get { return T("No se pudo enviar. Revisá tu conexión y probá de nuevo.", "Couldn't send. Check your internet and try again."); } }

        public static string MoreSettings { get { return T("Más opciones", "More options"); } }
        public static string StartWithWindows { get { return T("Iniciar con Windows", "Start with Windows"); } }
        public static string AutoUpdate { get { return T("Actualizar automáticamente", "Update automatically"); } }
        public static string CheckUpdates { get { return T("Buscar", "Check"); } }
        public static string CheckUpdatesName { get { return T("Buscar actualizaciones", "Check for updates"); } }
        public static string Version(string v, string state) { return string.Format(T("Versión {0}", "Version {0}"), v) + (state != null ? " · " + state : ""); }
        public static string UpdateAvailable(string tag) { return string.Format(T("Hay una versión nueva ({0})", "Version {0} is available"), tag.TrimStart('v')); }
        public static string UpdateButton { get { return T("Actualizar", "Update"); } }
        public static string UpToDate { get { return T("Al día", "Up to date"); } }
        public static string Checking { get { return T("Buscando…", "Checking…"); } }
        public static string CheckFailed { get { return T("No se pudo buscar. Probá más tarde.", "Couldn't check. Try again later."); } }
        public static string Available(string tag) { return string.Format(T("{0} disponible", "{0} available"), tag); }
        public static string UpdateWhenGameCloses(string tag) { return string.Format(T("{0} se instala al cerrar el juego", "{0} installs when you close the game"), tag); }
        public static string Updating { get { return T("Actualizando…", "Updating…"); } }
        public static string UpdateFailed { get { return T("No se pudo actualizar. Probá más tarde.", "Couldn't update. Try again later."); } }
        public static string UpdateAfterMatch { get { return T("Se actualiza al terminar la partida", "Updates after this match"); } }
        public static string DevBuild { get { return T("Versión de desarrollo", "Development build"); } }

        // Menu de la bandeja
        public static string MenuOpen { get { return T("Abrir AutoReady", "Open AutoReady"); } }
        public static string MenuPause { get { return T("Pausar", "Pause"); } }
        public static string MenuResume { get { return T("Reanudar", "Resume"); } }
        public static string MenuExit { get { return T("Salir", "Exit"); } }

        // Notificaciones (tambien son lo que llega al celular: cortas y al grano)
        public static string ToastPaused { get { return T("En pausa", "Paused"); } }
        public static string ToastPausedText(string key) { return string.Format(T("AutoReady no acepta partidas hasta que vuelvas a apretar {0} o cierres el juego.", "AutoReady won't accept matches until you press {0} again or close the game."), key); }
        public static string ToastResume { get { return T("Reanudar", "Resume"); } }
        public static string ToastAccepted { get { return T("Partida aceptada", "Match accepted"); } }
        public static string ToastAcceptedText(string game) { return string.Format(T("Volvé a la PC. Tu partida de {0} arranca cuando todos acepten.", "Come back to your PC. Your {0} match starts once everyone accepts."), game); }
        public static string ToastStarted(string game) { return string.Format(T("Arrancó tu partida de {0}", "Your {0} match started"), game); }
        public static string ToastStartedText { get { return T("Volvé a la PC ya.", "Get back to your PC now."); } }
        public static string ToastUnsureTitle { get { return T("En pausa por seguridad", "Paused to be safe"); } }
        public static string ToastUnsure(string game) { return string.Format(T("AutoReady no puede saber si arrancó tu partida. Tocá Reanudar cuando vuelvas a buscar. Configurá {0} para que no vuelva a pasar.", "AutoReady can't tell if your match started. Click Resume when you queue again. Set up {0} so this doesn't happen."), game); }
        public static string ToastSetupTitle(string game) { return string.Format(T("{0} necesita un paso, una sola vez", "{0} needs a one-time setup"), game); }
        public static string ToastSetupText { get { return T("Abrí AutoReady y tocá Configurar.", "Open AutoReady and click Set up."); } }
        public static string ToastRestartTitle(string game) { return string.Format(T("Reiniciá {0}", "Restart {0}"), game); }
        public static string ToastRestartText { get { return T("Reinicialo una vez para que AutoReady sepa cuándo estás en partida.", "Restart it once so AutoReady knows when you're in a match."); } }
        public static string ToastUpdate(string tag) { return string.Format(T("Versión {0} disponible", "Version {0} available"), tag); }
        public static string ToastUpdateText { get { return T("Tarda unos segundos. Tu configuración se mantiene.", "Takes a few seconds. Your settings stay."); } }
        public static string ToastUpdateButton { get { return T("Actualizar", "Update"); } }
        public static string ToastStillRunning { get { return T("AutoReady sigue activo", "AutoReady keeps running"); } }
        public static string ToastStillRunningText { get { return T("Tocá su ícono junto al reloj para volver a abrirlo.", "Click its icon next to the clock to open it again."); } }
        public static string PushTestTitle { get { return T("AutoReady conectado", "AutoReady connected"); } }
        public static string PushTestText { get { return T("Los avisos funcionan.", "Notifications work."); } }

        // Mensajes sueltos
        public static string AlreadyRunning { get { return T("AutoReady ya está abierto: buscá su ícono al lado del reloj.", "AutoReady is already running: look for its icon next to the clock."); } }
    }
}
