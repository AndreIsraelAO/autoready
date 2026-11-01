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
        IntPtr flashedWindow = IntPtr.Zero;   // ultima ventana que titilo en la barra de tareas
        AppState state = AppState.Starting;
        GameProfile activeGame;               // juego abierto que estamos vigilando
        DateTime cooldownUntil = DateTime.MinValue;
        DateTime lastAccept = DateTime.MinValue;
        DateTime gameSeenSince = DateTime.MinValue;
        readonly HashSet<string> gsiHintShown = new HashSet<string>();
        int consecutiveHits;
        bool useToasts;

        public TrayApp()
        {
            cfg = Config.Load();
            Samples.Enabled = cfg.SaveSamples;
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

            gsi = new Gsi(cfg.GsiPort);
            foreach (GameProfile g in GameProfile.All) gsi.InstallConfig(g);
            gsi.Start();
            gsi.StateChanged += OnGameStateChanged;

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
                case AppState.InMatch: text = "En partida de " + game.Name; break;
                case AppState.Searching: text = "Esperando partida en " + game.Name; break;
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
                if (isNew) Notify("Versión " + r.Tag + " disponible", "Se instala en unos segundos.", "Actualizar", InstallUpdate);
            });
        }

        public void InstallUpdate()
        {
            Updater.Release r = pendingUpdate;
            if (r == null) return;
            if (gsi.InAnyMatch())
            {
                updateMessage = "se actualiza cuando termine la partida";
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
                Thread.Sleep(wait);
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
            if (paused) { SetState(AppState.Paused); return 500; }
            if (game == null) { SetState(AppState.GameClosed); return 2000; }
            MaybeShowGsiHint(game);

            if (gsi.InMatch(game)) { SetState(AppState.InMatch); return 1000; }
            SetState(AppState.Searching);
            if (DateTime.Now < cooldownUntil) return 500;

            // Sin GSI no sabemos si ya estas en partida: despues de aceptar seguimos un rato (por si alguien
            // no acepto y volves a la cola) y despues pausamos para no hacer clicks dentro del juego.
            if (!gsi.Connected(game) && lastAccept != DateTime.MinValue &&
                (DateTime.Now - lastAccept).TotalSeconds > cfg.FallbackScanSecAfterAccept)
            {
                lastAccept = DateTime.MinValue;
                SetPaused(true);
                Notify("En pausa", "No puedo saber si ya estás en partida, así que dejé de aceptar.", "Reanudar", () => SetPaused(false));
                return 500;
            }

            if (Win32.IsIconic(hwnd))
            {
                // Juego minimizado: no se puede capturar. Cuando encuentra partida hace titilar la barra de tareas;
                // en ese momento lo restauramos para poder ver el boton.
                if (flashedWindow != hwnd) return cfg.ScanIntervalMs;
                flashedWindow = IntPtr.Zero;
                Log.Write(game.Name + " titilo estando minimizado: lo traigo al frente");
                Win32.Focus(hwnd);
                return 700;
            }

            Detector.Hit hit;
            using (Bitmap shot = Win32.Capture(hwnd))
            {
                if (shot == null) return cfg.ScanIntervalMs;
                hit = Detector.Find(shot, cfg.For(game).Detector);
                Samples.Remember(shot);
                if (hit != null && consecutiveHits + 1 >= 2) Samples.Accepted(shot, game);   // la que dispara el click
            }
            if (hit == null) { consecutiveHits = 0; return cfg.ScanIntervalMs; }

            // Pedimos dos detecciones seguidas para evitar falsos positivos.
            if (++consecutiveHits < 2) return 250;
            consecutiveHits = 0;
            Accept(game, hwnd, hit);
            return 500;
        }

        void Accept(GameProfile game, IntPtr hwnd, Detector.Hit hit)
        {
            Win32.Focus(hwnd);
            Thread.Sleep(250);
            Rectangle rc = Win32.ClientRectOnScreen(hwnd);
            Point p = hit.Center;
            Win32.Click(rc.X + p.X, rc.Y + p.Y);

            lastAccept = DateTime.Now;
            cooldownUntil = DateTime.Now.AddSeconds(cfg.CooldownAfterAcceptSec);
            Log.Write(game.Name + ": partida encontrada, click en ACCEPT (" + (rc.X + p.X) + "," + (rc.Y + p.Y) + ")");
            Notify("Partida aceptada", "Tu partida de " + game.Name + " está por empezar. Volvé a la PC.");
            if (cfg.Sound) Beep(true);
            Notifier.Phone(cfg, "Partida aceptada", "AutoReady aceptó tu partida de " + game.Name + ". Volvé a la PC.", "video_game");
        }

        void OnGameStateChanged(GameProfile game, string oldState, string newState)
        {
            bool recentlyAccepted = lastAccept != DateTime.MinValue && (DateTime.Now - lastAccept).TotalMinutes < 5;
            if (oldState == null && newState != null && cfg.NotifyMatchStarted && recentlyAccepted)
            {
                Notify(game.MatchStartedTitle, game.MatchStartedText);
                if (cfg.Sound) Beep(true);
                Notifier.Phone(cfg, game.MatchStartedTitle, game.MatchStartedText, "crossed_swords");
            }
            // Arranco una partida que no aceptamos nosotros: o no vimos el popup, o el jugador fue mas rapido.
            // Las capturas de ese momento sirven para ajustar el detector.
            if (oldState == null && newState != null && !paused && !recentlyAccepted) Samples.Missed(game);
            if (newState != null) { lastAccept = DateTime.MinValue; Samples.Forget(); }   // ya estamos en partida
        }

        void MaybeShowGsiHint(GameProfile game)
        {
            if (gsiHintShown.Contains(game.Id) || gsi.Connected(game)) return;
            if ((DateTime.Now - gameSeenSince).TotalSeconds < 90) return;
            gsiHintShown.Add(game.Id);
            Log.Write("GSI " + game.Name + ": abierto hace 90s y no llego nada");
            Notify("Activá GSI para más seguridad",
                   game.LaunchOptionHint ?? ("Reiniciá " + game.Name + " para que cargue la integración de AutoReady."));
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
