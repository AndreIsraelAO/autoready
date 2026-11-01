using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutoReady
{
    enum AppState { Starting, GameClosed, Paused, Searching, InMatch }

    class TrayApp : ApplicationContext, IPanelHost
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const int HotkeyId = 1;

        readonly Config cfg;
        readonly Gsi gsi;
        readonly HiddenWindow ui;
        readonly NotifyIcon tray;
        readonly Panel panel;
        readonly ToolStripMenuItem pauseItem;
        readonly Icon iconOn, iconOff;
        System.Threading.Timer updateTimer;
        Updater.Release pendingUpdate;
        string updateMessage;

        volatile bool paused;
        volatile bool running = true;
        volatile IntPtr flashedWindow = IntPtr.Zero;   // ultima ventana que titilo en la barra de tareas
        AppState state = AppState.Starting;
        GameProfile activeGame;               // juego abierto que estamos vigilando
        DateTime cooldownUntil = DateTime.MinValue;
        DateTime lastAccept = DateTime.MinValue;
        DateTime gameSeenSince = DateTime.MinValue;
        readonly HashSet<string> gsiHintShown = new HashSet<string>();
        int consecutiveHits;
        Point lastHit;
        DateTime lastBusySkip = DateTime.MinValue;
        bool useToasts;
        int installing;   // 1 mientras se instala una actualizacion

        public TrayApp()
        {
            cfg = Config.Load();
            Samples.Enabled = cfg.SaveSamples;
            Detector.LoadTemplates();
            iconOn = MakeIcon(false);
            iconOff = MakeIcon(true);

            ui = new HiddenWindow();
            ui.WindowFlashed += h => { flashedWindow = h; };
            ui.HotkeyPressed += () =>
            {
                SetPaused(!paused);
                Beep(!paused);
                if (paused) Notify("En pausa", "No va a aceptar partidas. Reanudá con " + HotkeyText + ".", "Reanudar", () => SetPaused(false));
                else Notify("Activo", "Va a aceptar tus partidas.");
            };

            panel = new Panel(this);

            // Menu chico: lo demas esta en el panel (click izquierdo).
            pauseItem = new ToolStripMenuItem("Pausar", null, (s, e) => SetPaused(!paused));
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Abrir AutoReady", null, (s, e) => OpenPanel()) { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Salir", null, (s, e) => Quit()));

            tray = new NotifyIcon { Icon = iconOn, Text = "AutoReady", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                PushStatus();
                panel.TogglePanel();
            };
            tray.BalloonTipClicked += (s, e) => OpenPanel();

            gsi = new Gsi(cfg.GsiPort, cfg.GsiToken);
            foreach (GameProfile g in GameProfile.All) gsi.InstallConfig(g);
            gsi.Start();
            gsi.StateChanged += OnGameStateChanged;
            Presence.Changed += OnPresenceChanged;

            if (cfg.HotkeyVk != 0 && !RegisterHotkey(cfg.HotkeyVk, cfg.HotkeyMods))
                Log.Write("Atajo: no se pudo registrar (otra app lo usa)");
            Shortcut.EnsureStartMenu();
            using (Stream logo = typeof(TrayApp).Assembly.GetManifestResourceStream("AutoReady.logo-64.png"))
                useToasts = Toast.Init(logo);

            Log.Write("AutoReady v" + Updater.CurrentVersionText + " iniciado");
            Thread scanner = new Thread(ScanLoop) { IsBackground = true, Name = "scanner" };
            scanner.Start();

            // Primer chequeo a los 30 s de arrancar y despues una vez por dia.
            updateTimer = new System.Threading.Timer(_ => { if (cfg.AutoUpdateCheck) CheckUpdates(false); },
                                                     null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(24));

            if (!cfg.Welcomed)
            {
                cfg.Welcomed = true;
                cfg.Save();
                ui.Post(OpenPanel);
            }
        }

        void OpenPanel()
        {
            PushStatus();
            panel.ShowPanel();
        }

        // ---------- IPanelHost: lo que se cambia desde el panel ----------

        public Config Cfg { get { return cfg; } }

        public void SetPaused(bool value)
        {
            paused = value;
            consecutiveHits = 0;
            if (!paused) lastAccept = DateTime.MinValue;
            state = AppState.Starting;   // fuerza a refrescar icono y panel en el proximo tick
            Log.Write(paused ? "Pausado" : "Reanudado");
            PushStatus();
        }

        public void SetPhone(bool on)
        {
            cfg.NotifyPhone = on;
            cfg.Save();
        }

        // Abre las propiedades del juego en Steam; el panel ya copio la opcion para pegarla.
        public void FixProtection(GameProfile game)
        {
            Log.Write("Proteccion: abro las propiedades de " + game.Name + " en Steam para agregar " + game.GsiLaunchOption);
            try { SteamConfig.OpenProperties(game.AppId); }
            catch (Exception e) { Log.Write("No pude abrir Steam: " + e.Message); }
        }

        public string LaunchOptionsWithProtection(GameProfile game)
        {
            string current = (SteamConfig.LaunchOptions(game.AppId) ?? "").Trim();
            return current.Length == 0 ? game.GsiLaunchOption : current + " " + game.GsiLaunchOption;
        }

        // Juego instalado al que le falta la opcion de lanzamiento para GSI (o null si esta todo bien).
        // Cuenta como activo si GSI ya esta mandando datos, aunque no se haya podido leer la config de Steam.
        GameProfile MissingProtection()
        {
            foreach (GameProfile g in GameProfile.All)
            {
                if (g.GsiLaunchOption == null || !cfg.For(g).Enabled || !Gsi.IsInstalled(g)) continue;
                if (gsi.Connected(g)) continue;
                if (g.UsesPresence && cfg.UseSteamStatus && Presence.Working) continue;   // el estado de Steam ya alcanza
                string lo = SteamConfig.LaunchOptions(g.AppId);
                if (lo != null && !SteamConfig.HasOption(g.AppId, g.GsiLaunchOption)) return g;
            }
            return null;
        }

        public void SendTestPush()
        {
            Notifier.Send(cfg, "AutoReady conectado", "Los avisos funcionan. ¡Buena suerte en la cola!", "white_check_mark");
        }

        public bool AutostartEnabled
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("AutoReady") != null;
            }
        }

        public void SetAutostart(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("AutoReady", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("AutoReady", false);
            }
        }

        public void SetAutoUpdate(bool on)
        {
            cfg.AutoUpdateCheck = on;
            cfg.Save();
        }

        public void CheckUpdatesNow() { CheckUpdates(true); }

        public string HotkeyText
        {
            get
            {
                if (cfg.HotkeyVk == 0) return "Elegir tecla";
                string s = "";
                if ((cfg.HotkeyMods & 0x2) != 0) s += "Ctrl+";
                if ((cfg.HotkeyMods & 0x1) != 0) s += "Alt+";
                if ((cfg.HotkeyMods & 0x4) != 0) s += "Shift+";
                return s + ((Keys)cfg.HotkeyVk).ToString();
            }
        }

        public bool SetHotkey(int vk, int mods)
        {
            Win32.UnregisterHotKey(ui.Handle, HotkeyId);
            if (vk != 0 && !RegisterHotkey(vk, mods))
            {
                if (cfg.HotkeyVk != 0) RegisterHotkey(cfg.HotkeyVk, cfg.HotkeyMods);   // vuelve al anterior
                return false;
            }
            cfg.HotkeyVk = vk;
            cfg.HotkeyMods = mods;
            cfg.Save();
            Log.Write("Atajo: " + HotkeyText);
            return true;
        }

        bool RegisterHotkey(int vk, int mods)
        {
            return Win32.RegisterHotKey(ui.Handle, HotkeyId, (uint)mods | Win32.MOD_NOREPEAT, (uint)vk);
        }

        // Estado para el panel y el menu; se llama cuando algo cambia.
        void PushStatus()
        {
            GameProfile game = activeGame;
            AppState s = state;
            string text;
            switch (s)
            {
                case AppState.Paused: text = "En pausa"; break;
                case AppState.InMatch: text = game != null ? "En partida de " + game.Name : "En partida"; break;
                case AppState.Searching: text = game != null ? "Esperando partida en " + game.Name : "Esperando partida"; break;
                default: text = paused ? "En pausa" : "Listo: abrí tu juego y buscá partida"; break;
            }
            PanelStatus st = new PanelStatus
            {
                Text = text,
                Good = !paused,
                Paused = paused,
                UpdateTag = pendingUpdate != null ? pendingUpdate.Tag : null,
                UpdateMessage = updateMessage,
            };
            GameProfile missing = MissingProtection();
            st.ProtectionFix = missing;
            st.Protection = missing != null
                ? "Falta un paso en " + missing.Name + ". Mientras tanto acepta en modo seguro."
                : (activeGame != null && activeGame.UsesPresence && !gsi.Connected(activeGame) && Presence.Working
                    ? "Activa con tu estado de Steam: solo mira la pantalla mientras buscás partida."
                    : "Activa: nunca toca nada durante una partida.");
            ui.Post(() =>
            {
                pauseItem.Text = paused ? "Reanudar" : "Pausar";
                panel.Refresh(st);
            });
        }

        // ---------- actualizaciones ----------

        void CheckUpdates(bool manual)
        {
            if (Updater.IsDevBuild)
            {
                if (manual) { updateMessage = "build de desarrollo"; PushStatus(); }
                return;
            }
            if (manual) { updateMessage = "buscando…"; PushStatus(); }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Updater.Release r = null;
                try { r = Updater.CheckForUpdate(); }
                catch (Exception e)
                {
                    Log.Write("Updater: no pude consultar GitHub: " + e.Message);
                    if (manual) { updateMessage = "no se pudo buscar"; PushStatus(); }
                    return;
                }
                if (r == null)
                {
                    updateMessage = "al día";
                    PushStatus();
                    return;
                }
                bool isNew = pendingUpdate == null || pendingUpdate.Version != r.Version;
                pendingUpdate = r;
                updateMessage = r.Tag + " disponible";
                Log.Write("Updater: hay una version nueva " + r.Tag);
                PushStatus();
                if (isNew) Notify("Versión " + r.Tag + " disponible", "Tocá Actualizar para instalarla.", "Actualizar", InstallUpdate);
            });
        }

        public void InstallUpdate()
        {
            Updater.Release r = pendingUpdate;
            if (r == null) return;
            if (Interlocked.CompareExchange(ref installing, 1, 0) != 0) return;   // ya se esta instalando
            if (gsi.InAnyMatch() || Presence.Current == PresenceState.InMatch)
            {
                updateMessage = "se actualiza cuando termine la partida";
                installing = 0;
                PushStatus();
                return;
            }
            updateMessage = "actualizando…";
            PushStatus();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string exe = Updater.DownloadAndInstall(r);
                    Updater.Relaunch(exe);
                    ui.Post(Quit);
                }
                catch (Exception e)
                {
                    Log.Write("Updater: fallo la actualizacion: " + e.Message);
                    updateMessage = "no se pudo actualizar";
                    installing = 0;
                    PushStatus();
                }
            });
        }

        // ---------- loop de escaneo ----------

        void ScanLoop()
        {
            while (running)
            {
                int wait = cfg.ScanIntervalMs;
                try { wait = Tick(); }
                catch (Exception e) { Log.Write("scanner: " + e); }
                Thread.Sleep(Math.Max(100, Math.Min(5000, wait)));   // acotado: un valor raro en el ini no puede trabar ni romper el loop
            }
        }

        // Primer juego habilitado que este abierto.
        GameProfile FindRunningGame(out IntPtr hwnd)
        {
            foreach (GameProfile g in GameProfile.All)
            {
                if (!cfg.For(g).Enabled) continue;
                hwnd = Win32.FindGameWindow(g.Process);
                if (hwnd != IntPtr.Zero) return g;
            }
            hwnd = IntPtr.Zero;
            return null;
        }

        // Devuelve cuantos ms esperar hasta el proximo tick.
        int Tick()
        {
            IntPtr hwnd;
            GameProfile game = FindRunningGame(out hwnd);
            if (game != activeGame)
            {
                activeGame = game;
                consecutiveHits = 0;
                gameSeenSince = game == null ? DateTime.MinValue : DateTime.Now;
                if (game != null) Log.Write(game.Name + " abierto");
                state = AppState.Starting;
            }
            Presence.Watch(cfg.UseSteamStatus ? game : null);
            if (paused) { SetState(AppState.Paused); return 500; }
            if (game == null) { SetState(AppState.GameClosed); return 2000; }

            if (gsi.InMatch(game)) { SetState(AppState.InMatch); return 1000; }

            // Estado de Steam (Dota sin GSI): en partida no se mira la pantalla. El estado llega con algunos segundos de
            // demora, asi que solo se usa para frenar, nunca para relajar las reglas del modo seguro.
            PresenceState ps = cfg.UseSteamStatus && game.UsesPresence && !gsi.Connected(game) ? Presence.Current : PresenceState.Unknown;
            if (ps == PresenceState.InMatch) { SetState(AppState.InMatch); consecutiveHits = 0; return 1000; }
            SetState(AppState.Searching);
            if (ps == PresenceState.Unknown) MaybeShowGsiHint(game);
            if (DateTime.Now < cooldownUntil) return 500;

            // Sin GSI ni estado de Steam no sabemos si ya estas en partida: despues de aceptar seguimos un rato
            // (por si alguien no acepto y volves a la cola) y despues pausamos para no hacer clicks dentro del juego.
            if (!gsi.Connected(game) && ps == PresenceState.Unknown && lastAccept != DateTime.MinValue &&
                (DateTime.Now - lastAccept).TotalSeconds > cfg.FallbackScanSecAfterAccept)
            {
                lastAccept = DateTime.MinValue;
                SetPaused(true);
                Notify("En pausa", "No puedo saber si ya estás en partida, así que dejé de aceptar.", "Reanudar", () => SetPaused(false));
                return 500;
            }

            if (!Win32.IsIconic(hwnd)) flashedWindow = IntPtr.Zero;   // un titileo viejo no tiene que restaurar el juego despues
            else
            {
                // Juego minimizado: no se puede capturar. Cuando encuentra partida hace titilar la barra de tareas;
                // en ese momento lo restauramos para poder ver el boton.
                if (flashedWindow != hwnd) return cfg.ScanIntervalMs;
                flashedWindow = IntPtr.Zero;
                Log.Write(game.Name + " titilo estando minimizado: lo traigo al frente");
                Win32.Focus(hwnd);
                return 700;
            }

            // Modo seguro: sin GSI no sabemos si ya hay una partida en curso, asi que se exige mas antes de tocar nada.
            bool safe = !gsi.Connected(game);
            int needed = safe ? 3 : 2;   // detecciones seguidas

            Detector.Hit hit;
            Bitmap decisive = null;   // la captura con la que se decide; se guarda solo si de verdad se acepta
            using (Bitmap shot = Win32.Capture(hwnd))
            {
                if (shot == null) return cfg.ScanIntervalMs;
                hit = Detector.FindButton(shot, game, cfg.For(game).Detector);
                Samples.Remember(shot);
                if (hit != null && consecutiveHits + 1 >= needed) decisive = new Bitmap(shot);
            }
            if (hit == null) { consecutiveHits = 0; return cfg.ScanIntervalMs; }
            using (decisive)
                return Decide(game, hwnd, hit, safe, needed, decisive);
        }

        int Decide(GameProfile game, IntPtr hwnd, Detector.Hit hit, bool safe, int needed, Bitmap decisive)
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
                    Log.Write(game.Name + ": posible popup, pero estas usando la PC y no hay GSI: no acepto");
                lastBusySkip = DateTime.Now;
                return 500;
            }
            if (Accept(game, hwnd, hit, safe) && decisive != null)
                Samples.Accepted(decisive, game, Detector.Roi(decisive.Width, decisive.Height, cfg.For(game).Detector));
            return 500;
        }

        // Pausado o en partida: se revisa otra vez justo antes de cada accion, porque el usuario puede pausar (atajo,
        // panel) o la partida empezar en el segundo que tarda aceptar.
        bool MustStop(GameProfile game)
        {
            return paused || gsi.InMatch(game) || (game.UsesPresence && Presence.Current == PresenceState.InMatch);
        }

        bool Accept(GameProfile game, IntPtr hwnd, Detector.Hit hit, bool safe)
        {
            if (MustStop(game)) return false;
            Win32.Focus(hwnd);
            Thread.Sleep(250);
            // Si Windows no dejo traer el juego al frente, no se manda nada: la tecla o el click irian a otra ventana.
            if (Win32.GetForegroundWindow() != hwnd)
            {
                Log.Write(game.Name + ": popup detectado, pero no pude traer el juego al frente; no toco nada");
                return false;
            }
            if (MustStop(game)) return false;
            string how;
            // Enter solo con GSI: si por error no fuera el popup, en Dota abriria el chat en medio de la partida.
            if (game.AcceptWithEnter && !safe)
            {
                Win32.PressEnter();
                Thread.Sleep(900);
                // Si el popup sigue ahi, el Enter no alcanzo (o no tenia el foco): click en el boton.
                Detector.Hit still = null;
                using (Bitmap again = Win32.Capture(hwnd))
                    if (again != null) still = Detector.FindButton(again, game, cfg.For(game).Detector);
                if (still == null) how = "con Enter";
                else how = !MustStop(game) && ClickButton(hwnd, still) ? "con click (el Enter no alcanzo)" : null;
            }
            else how = ClickButton(hwnd, hit) ? "con click" : null;
            if (how == null) return false;   // el click se cancelo: no se cuenta como aceptada

            lastAccept = DateTime.Now;
            cooldownUntil = DateTime.Now.AddSeconds(cfg.CooldownAfterAcceptSec);
            Log.Write(game.Name + ": partida encontrada, aceptada " + how);
            Notify("Partida aceptada", "Tu partida de " + game.Name + " está por empezar. Volvé a la PC.");
            if (cfg.Sound) Beep(true);
            Notifier.Phone(cfg, "Partida aceptada", "AutoReady aceptó tu partida de " + game.Name + ". Volvé a la PC.", "video_game");
            return true;
        }

        static bool ClickButton(IntPtr hwnd, Detector.Hit hit)
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

        void OnGameStateChanged(GameProfile game, string oldState, string newState)
        {
            bool recentlyAccepted = lastAccept != DateTime.MinValue && (DateTime.Now - lastAccept).TotalMinutes < 5;
            if (oldState == null && newState != null && cfg.NotifyMatchStarted && recentlyAccepted)
            {
                Notify("Tu partida arrancó", "Volvé a la PC.");
                if (cfg.Sound) Beep(true);
                Notifier.Phone(cfg, "Tu partida arrancó", "Tu partida de " + game.Name + " arrancó. Volvé a la PC.", "crossed_swords");
            }
            // Arranco una partida que no aceptamos nosotros: o no vimos el popup, o el jugador fue mas rapido.
            // Las capturas de ese momento sirven para ajustar el detector.
            if (oldState == null && newState != null && !paused && !recentlyAccepted) Samples.Missed(game);
            if (newState != null) { lastAccept = DateTime.MinValue; Samples.Forget(); }   // ya estamos en partida
        }

        // Estado de Steam (Dota sin GSI): mismo efecto que GSI al empezar y terminar una partida.
        void OnPresenceChanged(PresenceState old, PresenceState now, string text)
        {
            GameProfile game = activeGame;
            if (game == null || !game.UsesPresence || gsi.Connected(game)) return;
            // Solo cuenta como "empezo una partida" si venia buscando (bots o partidas personalizadas no tienen popup).
            if (now == PresenceState.InMatch && old == PresenceState.Finding) OnGameStateChanged(game, null, text);
            else if (old == PresenceState.InMatch && now != PresenceState.InMatch) OnGameStateChanged(game, text, null);
            PushStatus();
        }

        void MaybeShowGsiHint(GameProfile game)
        {
            if (gsiHintShown.Contains(game.Id) || gsi.Connected(game)) return;
            if ((DateTime.Now - gameSeenSince).TotalSeconds < 90) return;
            gsiHintShown.Add(game.Id);
            Log.Write("GSI " + game.Name + ": abierto hace 90s y no llego nada");
            Notify("Activá GSI para más seguridad",
                   game.GsiLaunchOption != null
                       ? "Abrí AutoReady y tocá Activar en Protección en partida."
                       : "Reiniciá " + game.Name + " para que cargue la integración de AutoReady.");
        }

        // ---------- estado e interfaz ----------

        void SetState(AppState s)
        {
            if (s == state) return;
            state = s;
            ui.Post(() =>
            {
                Icon icon = s == AppState.Paused ? iconOff : iconOn;
                tray.Icon = icon;
                string name = activeGame != null ? activeGame.Name : "";
                string text = s == AppState.Paused ? "en pausa"
                            : s == AppState.GameClosed ? "esperando un juego"
                            : s == AppState.InMatch ? "en partida de " + name
                            : "esperando partida en " + name;
                tray.Text = Truncate("AutoReady: " + text, 63);
            });
            PushStatus();
        }

        static string Truncate(string s, int max) { return s.Length <= max ? s : s.Substring(0, max); }

        // Notificacion nativa de Windows (logo, texto y boton opcional); si no hay toasts, el aviso clasico de la bandeja.
        // Al tocarla se abre el panel. Los clicks llegan en otro hilo, por eso pasan por ui.Post.
        void Notify(string title, string text, string button = null, Action onButton = null)
        {
            if (useToasts && Toast.Show(title, text, button,
                    onButton == null ? (Action)null : () => ui.Post(onButton), () => ui.Post(OpenPanel)))
                return;
            ui.Post(() => tray.ShowBalloonTip(4000, title, string.IsNullOrEmpty(text) ? " " : text, ToolTipIcon.None));
        }

        // Dos tonos subiendo = activo / partida; uno bajo = pausa.
        static void Beep(bool up)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (up) { Console.Beep(1500, 120); Console.Beep(1900, 160); }
                else Console.Beep(700, 200);
            });
        }

        void Quit()
        {
            running = false;
            Win32.UnregisterHotKey(ui.Handle, HotkeyId);
            tray.Visible = false;
            panel.Close();
            Log.Write("AutoReady cerrado");
            ExitThread();
        }

        // Icono de la bandeja a partir del logo embebido: a color = activo, gris apagado = en pausa.
        // (Windows usa este mismo icono en las notificaciones.)
        static Icon MakeIcon(bool gray)
        {
            int size = SystemInformation.SmallIconSize.Width;
            string res = size > 16 ? "AutoReady.tray-32.png" : "AutoReady.tray-16.png";
            using (Stream s = typeof(TrayApp).Assembly.GetManifestResourceStream(res))
            using (Bitmap logo = new Bitmap(s))
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(logo, 0, 0, size, size);
                }
                if (gray)
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (c.A == 0 || Math.Max(c.R, Math.Max(c.G, c.B)) < 40) continue;   // el fondo ink queda igual
                            // El check (blanco y planchas de color) pasa a gris medio: se lee pero se nota apagado.
                            int v = (c.R + c.G + c.B) / 3 * 55 / 100 + 30;
                            bmp.SetPixel(x, y, Color.FromArgb(c.A, v, v, v));
                        }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // Ventana invisible: recibe los avisos de "ventana titilando" del shell y el atajo global,
    // y sirve para volver al hilo de la UI.
    class HiddenWindow : Form
    {
        readonly uint shellHookMsg;
        public event Action<IntPtr> WindowFlashed;
        public event Action HotkeyPressed;

        public HiddenWindow()
        {
            ShowInTaskbar = false;
            IntPtr h = Handle;   // fuerza la creacion de la ventana sin mostrarla
            shellHookMsg = Win32.RegisterWindowMessage("SHELLHOOK");
            Win32.RegisterShellHookWindow(h);
        }

        public void Post(Action a)
        {
            if (IsHandleCreated) BeginInvoke(a);
        }

        protected override void WndProc(ref Message m)
        {
            // Comparacion exacta: HSHELL_FLASH es HSHELL_REDRAW con el bit alto, y un redraw no significa partida.
            if (m.Msg == (int)shellHookMsg && m.WParam.ToInt32() == Win32.HSHELL_FLASH)
            {
                Action<IntPtr> h = WindowFlashed;
                if (h != null) h(m.LParam);
            }
            else if (m.Msg == Win32.WM_HOTKEY)
            {
                Action h = HotkeyPressed;
                if (h != null) h();
            }
            base.WndProc(ref m);
        }
    }
}
