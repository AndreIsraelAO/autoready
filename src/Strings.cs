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
        public static string StatusIdle { get { return T("Listo. Abrí tu juego y buscá partida.", "Ready. Open your game and queue up."); } }
        public static string StatusPaused { get { return T("En pausa", "Paused"); } }
        public static string StatusSearching(string game) { return string.Format(T("Esperando partida en {0}", "Waiting for a match in {0}"), game); }
        public static string StatusInMatch(string game) { return string.Format(T("En partida de {0}", "In a {0} match"), game); }
        public static string TooltipIdle { get { return T("AutoReady: esperando un juego", "AutoReady: waiting for a game"); } }
        public static string TooltipPaused(string key) { return key == null ? T("AutoReady: en pausa", "AutoReady: paused") : string.Format(T("AutoReady: en pausa ({0} para reanudar)", "AutoReady: paused ({0} to resume)"), key); }
        public static string TooltipSearching(string game) { return string.Format(T("AutoReady: esperando partida en {0}", "AutoReady: waiting for a match in {0}"), game); }
        public static string TooltipInMatch(string game) { return string.Format(T("AutoReady: en partida de {0}", "AutoReady: in a {0} match"), game); }

        // Bienvenida (primer uso)
        public static string WelcomeTitle { get { return T("Así funciona", "How it works"); } }
        public static string WelcomeText { get { return T("Buscá partida en tu juego y alejate: AutoReady la acepta y te avisa. Queda junto al reloj (puede estar en ^); fijá el ícono para tenerlo a mano.",
                                                         "Queue up in your game and step away: AutoReady accepts the match and lets you know. It lives next to the clock (it may be under ^); pin the icon to keep it handy."); } }
        public static string WelcomeDismiss { get { return T("Entendido", "Got it"); } }

        // Panel
        public static string AcceptTitle { get { return T("Aceptar partidas automáticamente", "Accept matches automatically"); } }
        public static string AcceptOn { get { return T("Activado: acepta cuando aparece la partida", "On: accepts when the match pops"); } }
        public static string AcceptOff { get { return T("En pausa: no acepta nada", "Paused: nothing is accepted"); } }
        public static string HotkeyTitle { get { return T("Tecla de pausa", "Pause hotkey"); } }
        public static string HotkeyHint { get { return T("Funciona en cualquier lado, también jugando", "Works anywhere, even in game"); } }
        public static string HotkeyNone { get { return T("Elegir tecla", "Choose key"); } }
        public static string HotkeyPress { get { return T("Apretá una tecla…", "Press a key…"); } }
        public static string HotkeyPressHint { get { return T("Esc cancela · Supr la quita", "Esc to cancel · Delete to clear"); } }
        public static string HotkeyTaken { get { return T("Otra app usa esa tecla. Probá con otra.", "Another app uses that key. Try another."); } }
        public static string HotkeyNeedsModifier { get { return T("Usá una tecla F o combiná con Ctrl, Alt o Shift.", "Use an F key or combine with Ctrl, Alt or Shift."); } }
        public static string ProtectionTitle { get { return T("Protección en partida", "In-match protection"); } }
        public static string ProtectionChecking { get { return T("Revisando…", "Checking…"); } }
        public static string ProtectionOn { get { return T("Activada. No hace nada durante la partida.", "On. Never acts during a match."); } }
        public static string ProtectionOnSteam { get { return T("Activada con tu estado de Steam: solo mira mientras buscás partida.", "On, using your Steam status: only watches while you queue."); } }
        public static string ProtectionMissing(string game) { return string.Format(T("A {0} le falta un paso. Mientras tanto, modo seguro.", "{0} needs one step. Until then, safe mode is on."), game); }
        public static string ProtectionButton { get { return T("Configurar", "Set up"); } }
        public static string ProtectionCopiedOwn(string game) { return string.Format(T("Opciones copiadas. En Steam, en Opciones de lanzamiento: Ctrl+A, Ctrl+V y reiniciá {0}.", "Launch options copied. In Steam, in Launch Options: Ctrl+A, Ctrl+V, then restart {0}."), game); }
        public static string ProtectionCopiedNew(string game) { return string.Format(T("Opción copiada. En Steam, en Opciones de lanzamiento: Ctrl+V y reiniciá {0}.", "Option copied. In Steam, in Launch Options: Ctrl+V, then restart {0}."), game); }

        public static string SectionAlerts { get { return T("Avisos", "Notifications"); } }
        public static string Sound { get { return T("Sonido", "Sound"); } }
        public static string SoundHint { get { return T("Un pitido al aceptar y al pausar", "A beep when accepting and pausing"); } }
        public static string MatchStartTitle { get { return T("Cuando arranca la partida", "When the match starts"); } }
        public static string MatchStartHint { get { return T("Un segundo aviso para que vuelvas a tiempo", "A second reminder to come back in time"); } }
        public static string PhoneTitle { get { return T("Celular", "Phone"); } }
        public static string PhoneHint { get { return T("Con la app gratis ntfy", "Through the free ntfy app"); } }
        public static string PhoneScan { get { return T("Escaneá con la cámara", "Scan with your camera"); } }
        public static string PhoneOrCode { get { return T("o sumá este código en ntfy:", "or add this code in ntfy:"); } }
        public static string PhoneSecret { get { return T("No lo muestres en streams: con el código se pueden leer y mandar avisos.", "Don't show it on stream: the code lets anyone read and send notifications."); } }
        public static string PhoneTest { get { return T("Enviar prueba", "Send test"); } }
        public static string PhoneSending { get { return T("Enviando…", "Sending…"); } }
        public static string PhoneSent { get { return T("Enviada", "Sent"); } }
        public static string PhoneFailed { get { return T("No se pudo enviar", "Couldn't send"); } }

        public static string SectionGeneral { get { return "General"; } }
        public static string StartWithWindows { get { return T("Iniciar con Windows", "Start with Windows"); } }
        public static string AutoUpdate { get { return T("Buscar actualizaciones automáticamente", "Check for updates automatically"); } }
        public static string SteamStatus { get { return T("Usar mi estado de Steam", "Use my Steam status"); } }
        public static string SteamStatusHint { get { return T("Para saber si estás buscando partida o jugando", "To know whether you're queuing or playing"); } }
        public static string CheckUpdates { get { return T("Buscar actualizaciones", "Check for updates"); } }
        public static string Version(string v, string state) { return string.Format(T("Versión {0}", "Version {0}"), v) + (state != null ? " · " + state : ""); }
        public static string UpdateTo(string tag) { return string.Format(T("Actualizar a {0}", "Update to {0}"), tag); }
        public static string UpToDate { get { return T("Al día", "Up to date"); } }
        public static string Checking { get { return T("Buscando…", "Checking…"); } }
        public static string CheckFailed { get { return T("No se pudo buscar", "Couldn't check"); } }
        public static string Available(string tag) { return string.Format(T("{0} disponible", "{0} available"), tag); }
        public static string Updating { get { return T("Actualizando…", "Updating…"); } }
        public static string UpdateFailed { get { return T("No se pudo actualizar", "Couldn't update"); } }
        public static string UpdateAfterMatch { get { return T("Se actualiza al terminar la partida", "Updates after this match"); } }
        public static string DevBuild { get { return T("Versión de desarrollo", "Development build"); } }

        // Menu de la bandeja
        public static string MenuOpen { get { return T("Abrir AutoReady", "Open AutoReady"); } }
        public static string MenuPause { get { return T("Pausar", "Pause"); } }
        public static string MenuResume { get { return T("Reanudar", "Resume"); } }
        public static string MenuExit { get { return T("Salir", "Exit"); } }

        // Notificaciones
        public static string ToastPaused { get { return T("En pausa", "Paused"); } }
        public static string ToastPausedText(string key) { return string.Format(T("No va a aceptar partidas. Reanudá con {0}.", "Matches won't be accepted. Resume with {0}."), key); }
        public static string ToastResume { get { return T("Reanudar", "Resume"); } }
        public static string ToastAccepted { get { return T("Partida aceptada", "Match accepted"); } }
        public static string ToastAcceptedText(string game) { return string.Format(T("Si todos aceptan, tu partida de {0} arranca en segundos. Volvé a la PC.", "If everyone accepts, your {0} match starts in seconds. Come back to your PC."), game); }
        public static string ToastStarted { get { return T("Arrancó tu partida", "Your match started"); } }
        public static string ToastStartedText(string game) { return string.Format(T("Tu partida de {0} ya empezó. Es hora de volver.", "Your {0} match has started. Time to come back."), game); }
        public static string ToastUnsure { get { return T("AutoReady no puede saber si estás en partida y dejó de aceptar.", "AutoReady can't tell if you're in a match, so it stopped accepting."); } }
        public static string ToastSetupTitle { get { return T("Terminá de configurar la protección en partida", "Finish setting up in-match protection"); } }
        public static string ToastSetupText { get { return T("Abrí AutoReady y tocá Configurar en Protección en partida.", "Open AutoReady and click Set up next to In-match protection."); } }
        public static string ToastRestartGame(string game) { return string.Format(T("Reiniciá {0} una vez para que cargue la protección en partida.", "Restart {0} once so it loads in-match protection."), game); }
        public static string ToastUpdate(string tag) { return string.Format(T("Versión {0} disponible", "Version {0} available"), tag); }
        public static string ToastUpdateText { get { return T("Tocá Actualizar para instalarla.", "Click Update to install it."); } }
        public static string ToastUpdateButton { get { return T("Actualizar", "Update"); } }
        public static string ToastStillRunning { get { return T("AutoReady sigue funcionando", "AutoReady is still running"); } }
        public static string ToastStillRunningText { get { return T("Está junto al reloj. Tocá su ícono para abrir el panel.", "It's next to the clock. Click its icon to open the panel."); } }
        public static string PushTestTitle { get { return T("AutoReady conectado", "AutoReady connected"); } }
        public static string PushTestText { get { return T("Los avisos funcionan.", "Notifications work."); } }

        // Mensajes sueltos
        public static string AlreadyRunning { get { return T("AutoReady ya está abierto: buscá su ícono al lado del reloj.", "AutoReady is already running: look for its icon next to the clock."); } }
    }
}
