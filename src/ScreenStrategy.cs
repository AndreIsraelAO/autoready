using System;
using System.Drawing;
using System.Threading;

namespace AutoReady
{
    // Juegos que se aceptan mirando la pantalla (Dota 2, CS2): captura de la ventana, detector del boton y click o Enter.
    // Para no tocar nada en partida usa, en este orden, GSI (oficial de Valve) y el estado de Steam; sin ninguno de los
    // dos entra en modo seguro (mas detecciones, nada de Enter, y nunca si estas usando el mouse o el teclado).
    class ScreenStrategy : GameStrategy
    {
        IntPtr hwnd;
        volatile IntPtr flashedWindow = IntPtr.Zero;   // ultima ventana que titilo en la barra de tareas
        int consecutiveHits;
        volatile bool resetRequested;   // pedido desde otro hilo (pausa): Tick lo aplica
        Point lastHit;
        DateTime lastBusySkip = DateTime.MinValue;
        DateTime openedAt = DateTime.MinValue;
        bool gsiHintShown;

        public ScreenStrategy(GameProfile game, IGameHost host) : base(game, host) { }

        Gsi Gsi { get { return Host.Gsi; } }

        public override bool IsRunning()
        {
            hwnd = Win32.FindGameWindow(Game.Process);
            return hwnd != IntPtr.Zero;
        }

        public override void Reset()
        {
            base.Reset();
            openedAt = DateTime.Now;
            flashedWindow = IntPtr.Zero;   // un titileo de antes de vigilarlo no cuenta
        }

        public override void ResetDetection() { resetRequested = true; }

        public override bool InMatch
        {
            get { return Gsi.InMatch(Game) || (Game.UsesPresence && Presence.Current == PresenceState.InMatch); }
        }

        // Juego instalado al que le falta la opcion de lanzamiento para GSI. Cuenta como resuelto si GSI ya manda
        // datos o si alcanza con el estado de Steam.
        public override bool NeedsSetup()
        {
            if (Game.GsiLaunchOption == null || !Cfg.For(Game).Enabled || !Gsi.IsInstalled(Game)) return false;
            if (Gsi.Connected(Game)) return false;
            if (Game.UsesPresence && Cfg.UseSteamStatus && Presence.Working) return false;
            string lo = SteamConfig.LaunchOptions(Game.AppId);
            return lo != null && !SteamConfig.HasOption(Game.AppId, Game.GsiLaunchOption);
        }

        public override void OnWindowFlashed(IntPtr h) { flashedWindow = h; }

        // Estado de Steam (Dota sin GSI): mismo efecto que GSI al empezar y terminar una partida.
        public override void OnPresenceChanged(PresenceState old, PresenceState now, string text)
        {
            if (!Game.UsesPresence || Gsi.Connected(Game)) return;
            // Solo cuenta como "empezo una partida" si venia buscando (bots o partidas personalizadas no tienen popup).
            if (now == PresenceState.InMatch && old == PresenceState.Finding) Host.MatchStateChanged(Game, null, text);
            else if (old == PresenceState.InMatch && now != PresenceState.InMatch) Host.MatchStateChanged(Game, text, null);
        }

        public override int Tick()
        {
            int interval = Cfg.ScanIntervalMs;
            if (resetRequested) { resetRequested = false; consecutiveHits = 0; }
            if (Gsi.InMatch(Game)) { Host.SetState(AppState.InMatch); return 1000; }

            // Estado de Steam (Dota sin GSI): en partida no se mira la pantalla. El estado llega con algunos segundos de
            // demora, asi que solo se usa para frenar, nunca para relajar las reglas del modo seguro.
            PresenceState ps = Cfg.UseSteamStatus && Game.UsesPresence && !Gsi.Connected(Game) ? Presence.Current : PresenceState.Unknown;
            if (ps == PresenceState.InMatch) { Host.SetState(AppState.InMatch); consecutiveHits = 0; return 1000; }
            Host.SetState(AppState.Searching);
            if (ps == PresenceState.Unknown) MaybeShowGsiHint();
            if (InCooldown) return 500;

            // Sin GSI ni estado de Steam no sabemos si ya estas en partida: despues de aceptar seguimos un rato
            // (por si alguien no acepto y volves a la cola) y despues pausamos para no hacer clicks dentro del juego.
            if (!Gsi.Connected(Game) && ps == PresenceState.Unknown && Host.LastAccept != DateTime.MinValue &&
                (DateTime.Now - Host.LastAccept).TotalSeconds > Cfg.FallbackScanSecAfterAccept)
            {
                Host.ForgetLastAccept();
                Host.SetPaused(true);
                Host.Notify(S.ToastUnsureTitle, S.ToastUnsure(Game.Name), S.ToastResume, () => Host.SetPaused(false));
                return 500;
            }

            if (!Win32.IsIconic(hwnd)) flashedWindow = IntPtr.Zero;   // un titileo viejo no tiene que restaurar el juego despues
            else
            {
                // Juego minimizado: no se puede capturar. Cuando encuentra partida hace titilar la barra de tareas;
                // en ese momento lo restauramos para poder ver el boton.
                if (flashedWindow != hwnd) return interval;
                flashedWindow = IntPtr.Zero;
                Log.Write(Game.Name + " titilo estando minimizado: lo traigo al frente");
                Win32.Focus(hwnd);
                return 700;
            }

            // Modo seguro: sin GSI no sabemos si ya hay una partida en curso, asi que se exige mas antes de tocar nada.
            bool safe = !Gsi.Connected(Game);
            int needed = safe ? 3 : 2;   // detecciones seguidas

            Detector.Hit hit;
            Bitmap decisive = null;   // la captura con la que se decide; se guarda solo si de verdad se acepta
            using (Bitmap shot = Win32.Capture(hwnd))
            {
                if (shot == null) return interval;
                hit = Detector.FindButton(shot, Game, Cfg.For(Game).Detector);
                Samples.Remember(shot);
                if (hit != null && consecutiveHits + 1 >= needed) decisive = new Bitmap(shot);
            }
            if (hit == null) { consecutiveHits = 0; return interval; }
            using (decisive)
                return Decide(hit, safe, needed, decisive);
        }

        int Decide(Detector.Hit hit, bool safe, int needed, Bitmap decisive)
        {
            // El popup no se mueve: en modo seguro la deteccion tiene que repetirse en el mismo lugar.
            if (safe && consecutiveHits > 0 &&
                (Math.Abs(hit.Center.X - lastHit.X) > 12 || Math.Abs(hit.Center.Y - lastHit.Y) > 12))
                consecutiveHits = 0;
            lastHit = hit.Center;
            if (++consecutiveHits < needed) return 250;
            consecutiveHits = 0;

            // Sin GSI, con el juego al frente y alguien usando el mouse o el teclado: puede estar jugando.
            // No se toca nada; si es el popup, lo acepta la persona.
            if (safe && Win32.GetForegroundWindow() == hwnd && Win32.IdleTime() < TimeSpan.FromSeconds(3))
            {
                if ((DateTime.Now - lastBusySkip).TotalSeconds > 30)
                    Log.Write(Game.Name + ": posible popup, pero estas usando la PC y no hay GSI: no acepto");
                lastBusySkip = DateTime.Now;
                return 500;
            }
            if (Accept(hit, safe) && decisive != null)
                Samples.Accepted(decisive, Game, Detector.Roi(decisive.Width, decisive.Height, Cfg.For(Game).Detector));
            return 500;
        }

        // Pausado o en partida: se revisa otra vez justo antes de cada accion, porque el usuario puede pausar (atajo,
        // panel) o la partida empezar en el segundo que tarda aceptar.
        bool MustStop() { return Host.Paused || InMatch; }

        bool Accept(Detector.Hit hit, bool safe)
        {
            if (MustStop()) return false;
            Win32.Focus(hwnd);
            Thread.Sleep(250);
            // Si Windows no dejo traer el juego al frente, no se manda nada: la tecla o el click irian a otra ventana.
            if (Win32.GetForegroundWindow() != hwnd)
            {
                Log.Write(Game.Name + ": popup detectado, pero no pude traer el juego al frente; no toco nada");
                return false;
            }
            if (MustStop()) return false;
            string how;
            // Enter solo con GSI: si por error no fuera el popup, en Dota abriria el chat en medio de la partida.
            if (Game.AcceptWithEnter && !safe)
            {
                Win32.PressEnter();
                Thread.Sleep(900);
                // Si el popup sigue ahi, el Enter no alcanzo (o no tenia el foco): click en el boton.
                Detector.Hit still = null;
                using (Bitmap again = Win32.Capture(hwnd))
                    if (again != null) still = Detector.FindButton(again, Game, Cfg.For(Game).Detector);
                if (still == null) how = "con Enter";
                else how = !MustStop() && ClickButton(still) ? "con click (el Enter no alcanzo)" : null;
            }
            else how = ClickButton(hit) ? "con click" : null;
            if (how == null) return false;   // el click se cancelo: no se cuenta como aceptada

            StartCooldown();
            IntPtr window = hwnd;
            // Al tocar el aviso se trae el juego al frente (es lo que la persona quiere ver), no el panel.
            Host.MatchAccepted(Game, how, () => Win32.Focus(window));
            return true;
        }

        bool ClickButton(Detector.Hit hit)
        {
            Rectangle rc = Win32.ClientRectOnScreen(hwnd);
            int x = rc.X + hit.Center.X, y = rc.Y + hit.Center.Y;
            // El punto tiene que caer en la ventana del juego (no en algo que quedo encima).
            if (Win32.GetForegroundWindow() != hwnd || !Win32.PointBelongsTo(hwnd, x, y))
            {
                Log.Write("Click cancelado: el punto no esta sobre la ventana del juego");
                return false;
            }
            Win32.Click(x, y);
            return true;
        }

        // Abierto hace 90 s y GSI no mando nada: falta el paso de configuracion o reiniciar el juego.
        void MaybeShowGsiHint()
        {
            if (gsiHintShown || Gsi.Connected(Game)) return;
            if ((DateTime.Now - openedAt).TotalSeconds < 90) return;
            gsiHintShown = true;
            Log.Write("GSI " + Game.Name + ": abierto hace 90s y no llego nada");
            if (Game.GsiLaunchOption != null) Host.Notify(S.ToastSetupTitle(Game.Name), S.ToastSetupText);
            else Host.Notify(S.ToastRestartTitle(Game.Name), S.ToastRestartText);
        }
    }
}
