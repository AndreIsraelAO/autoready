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
        readonly StalePresence stalePresence = new StalePresence();
        volatile bool freshSession;   // el juego se abrio hace poco (ver StalePresence)
        bool wasGsiInMatch;

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
            stalePresence.Reset();
            wasGsiInMatch = false;
            freshSession = GameProcessAge() < StalePresence.FreshSession;
        }

        // Hace cuanto arranco el proceso del juego (MaxValue si no se puede saber: nunca cuenta como recien abierto).
        TimeSpan GameProcessAge()
        {
            try
            {
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName(Game.Process))
                    using (p) return DateTime.Now - p.StartTime;
            }
            catch (Exception e) { Log.Write("No pude leer cuando arranco " + Game.Name + ": " + e.Message); }
            return TimeSpan.MaxValue;
        }

        // Que hacer en esta vuelta segun lo que se sabe. GSI manda; el estado de Steam (una pagina web que Steam cachea
        // y llega tarde o pegado) solo aconseja: nunca frena el escaneo por si solo, porque equivocarse para ese lado
        // es perder el popup sin avisar.
        public class ScanPlan
        {
            public bool Stop;        // GSI confirma una partida: no se mira la pantalla
            public bool Safe;        // sin GSI: mas detecciones, nada de Enter, nunca si estas usando el juego
            public int Needed;       // detecciones seguidas antes de actuar
            public int IntervalMs;   // espera hasta la proxima vuelta
        }

        public static ScanPlan Plan(bool gsiInMatch, bool gsiConnected, bool knowsMenu, bool usesSteam, PresenceState steam, int baseIntervalMs)
        {
            ScanPlan p = new ScanPlan { Safe = !gsiConnected, IntervalMs = baseIntervalMs };
            p.Needed = p.Safe ? 3 : 2;
            if (gsiInMatch) { p.Stop = true; p.IntervalMs = 1000; return p; }
            // Con GSI conocido (o conectado) el estado de Steam no entra en la decision.
            if (!usesSteam || gsiConnected || knowsMenu) return p;
            // Steam dice "en partida" sin que GSI lo confirme: se sigue mirando, pero se pide mas evidencia y mas calma.
            if (steam == PresenceState.InMatch) { p.Needed = 4; p.IntervalMs = Math.Max(baseIntervalMs, 1000); }
            // Buscando: es el unico momento en que aparece el popup, y la ventana para aceptar es corta.
            else if (steam == PresenceState.Finding) p.IntervalMs = Math.Min(baseIntervalMs, 300);
            return p;
        }

        // Estado de Steam sin el que quedo de la partida anterior (ver StalePresence).
        PresenceState SteamState
        {
            get { return stalePresence.Filter(Presence.Current, Presence.Text); }
        }

        public override void ResetDetection() { resetRequested = true; }

        // Para quien no debe molestar en partida (actualizador, panel): GSI, y si no lo sabe, el estado de Steam.
        public override bool InMatch
        {
            get
            {
                return Gsi.InMatch(Game) ||
                       (Game.UsesPresence && !Gsi.KnowsMenu(Game, openedAt) && SteamState == PresenceState.InMatch);
            }
        }

        // En el menu segun el estado de Steam (el unico que distingue "menu" de "buscando"). Sin eso no se sabe: false.
        public override bool IdleForUpdate
        {
            get { return Game.UsesPresence && Cfg.UseSteamStatus && Presence.Current == PresenceState.Menu && !Gsi.InMatch(Game); }
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
            bool gsiInMatch = Gsi.InMatch(Game);
            // La partida termino segun GSI: el estado de Steam puede seguir diciendo "en partida" un buen rato.
            if (wasGsiInMatch && !gsiInMatch) stalePresence.MatchEnded();
            if (gsiInMatch) stalePresence.MatchStarted();
            wasGsiInMatch = gsiInMatch;

            // GSI manda; el estado de Steam solo aconseja (ver Plan).
            stalePresence.Observe(Presence.Current, Presence.Text, freshSession);
            bool knowsMenu = Gsi.KnowsMenu(Game, openedAt);
            bool usesSteam = Cfg.UseSteamStatus && Game.UsesPresence;
            PresenceState ps = usesSteam ? SteamState : PresenceState.Unknown;
            ScanPlan plan = Plan(gsiInMatch, Gsi.Connected(Game), knowsMenu, usesSteam, ps, interval);
            interval = plan.IntervalMs;
            if (plan.Stop) { Host.SetState(AppState.InMatch); consecutiveHits = 0; return interval; }
            Host.SetState(AppState.Searching);
            if (ps == PresenceState.Unknown && !knowsMenu) MaybeShowGsiHint();
            if (InCooldown) return 500;

            // Sin GSI ni estado de Steam no sabemos si ya estas en partida: despues de aceptar seguimos un rato
            // (por si alguien no acepto y volves a la cola) y despues pausamos para no hacer clicks dentro del juego.
            if (!Gsi.Connected(Game) && !knowsMenu && ps == PresenceState.Unknown && Host.LastAccept != DateTime.MinValue &&
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
            bool safe = plan.Safe;
            int needed = plan.Needed;   // detecciones seguidas

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
            if (safe && consecutiveHits > 0 && !SameSpot(hit.Center, lastHit))
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

        // Dos detecciones del boton en el mismo lugar (el popup no se mueve).
        public static bool SameSpot(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) <= 12 && Math.Abs(a.Y - b.Y) <= 12;
        }

        // Justo antes de tocar: el popup tiene que seguir ahi. Si la persona lo acepto a mano en el segundo que
        // tardo todo esto, el click caeria sobre la pantalla siguiente (seleccion de heroe, por ejemplo).
        Detector.Hit StillThere(Detector.Hit hit)
        {
            using (Bitmap again = Win32.Capture(hwnd))
            {
                if (again == null) return null;
                Detector.Hit now = Detector.FindButton(again, Game, Cfg.For(Game).Detector);
                return now != null && SameSpot(now.Center, hit.Center) ? now : null;
            }
        }

        // Pausado o en partida: se revisa otra vez justo antes de cada accion, porque el usuario puede pausar (atajo,
        // panel) o la partida empezar en el segundo que tarda aceptar.
        // Solo GSI frena la accion: el estado de Steam llega tarde y puede estar pegado, y frenar de mas es perder el popup.
        bool MustStop() { return Host.Paused || Gsi.InMatch(Game); }

        bool Accept(Detector.Hit hit, bool safe)
        {
            if (MustStop()) return false;
            // Ya al frente (lo normal si estas jugando): no hace falta esperar.
            if (Win32.GetForegroundWindow() != hwnd)
            {
                Win32.Focus(hwnd);
                Thread.Sleep(250);
            }
            // Si Windows no dejo traer el juego al frente, no se manda nada: la tecla o el click irian a otra ventana.
            if (Win32.GetForegroundWindow() != hwnd)
            {
                Log.Write(Game.Name + ": popup detectado, pero no pude traer el juego al frente; no toco nada");
                return false;
            }
            if (MustStop()) return false;
            Detector.Hit confirmed = StillThere(hit);
            if (confirmed == null)
            {
                Log.Write(Game.Name + ": el popup ya no estaba al ir a aceptar (probablemente lo aceptaste vos); no toco nada");
                return false;
            }
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
            else how = ClickButton(confirmed) ? "con click" : null;
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
            if (gsiHintShown || Game.NoGsi || Gsi.Connected(Game)) return;
            if ((DateTime.Now - openedAt).TotalSeconds < 90) return;
            gsiHintShown = true;
            Log.Write("GSI " + Game.Name + ": abierto hace 90s y no llego nada");
            if (Game.GsiLaunchOption != null) Host.Notify(S.ToastSetupTitle(Game.Name), S.ToastSetupText);
            else Host.Notify(S.ToastRestartTitle(Game.Name), S.ToastRestartText);
        }
    }
}
