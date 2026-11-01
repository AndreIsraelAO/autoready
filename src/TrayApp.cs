using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutoReady
{
    enum AppState { Starting, GameClosed, Paused, Searching, InMatch }

    class TrayApp : ApplicationContext, IPanelHost, IGameHost
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
        volatile bool installWhenGameCloses;   // actualizacion automatica esperando que se cierre el juego
        volatile bool running = true;
        AppState state = AppState.Starting;
        readonly List<GameStrategy> strategies;   // una por juego, en el orden de GameProfile.All
        volatile GameStrategy active;             // la del juego abierto que estamos vigilando
        DateTime lastAccept = DateTime.MinValue;
        bool useToasts;
        int installing;   // 1 mientras se instala una actualizacion

        public TrayApp()
        {
            cfg = Config.Load();
            strategies = GameProfile.All.ConvertAll(g => g.NewStrategy(g, this));
            Samples.Enabled = cfg.SaveSamples;
            Detector.LoadTemplates();
            iconOn = MakeIcon(false);
            iconOff = MakeIcon(true);

            ui = new HiddenWindow();
            // Solo al juego que se esta vigilando: un titileo de otro juego no tiene que traerlo al frente despues.
            ui.WindowFlashed += h => { GameStrategy a = active; if (a != null) a.OnWindowFlashed(h); };
            ui.HotkeyPressed += () =>
            {
                SetPaused(!paused);
                Beep(!paused);
                // Al pausar se avisa (con boton para reanudar); al reanudar alcanza con el sonido.
                if (paused) Notify(S.ToastPaused, S.ToastPausedText(HotkeyText), S.ToastResume, () => SetPaused(false));
            };

            panel = new Panel(this);

            // Menu chico: lo demas esta en el panel (click izquierdo).
            pauseItem = new ToolStripMenuItem(S.MenuPause, null, (s, e) => SetPaused(!paused));
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem(S.MenuOpen, null, (s, e) => OpenPanel()) { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(S.MenuExit, null, (s, e) => Quit()));

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
            gsi.StateChanged += MatchStateChanged;
            Presence.Changed += (old, now, text) =>
            {
                GameStrategy a = active;
                if (a != null) a.OnPresenceChanged(old, now, text);
                PushStatus();
            };

            if (cfg.HotkeyVk != 0 && !RegisterHotkey(cfg.HotkeyVk, cfg.HotkeyMods))
                Log.Write("Atajo: no se pudo registrar (otra app lo usa)");
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
                // Primer uso: el panel se abre con la explicacion, y al cerrarlo se avisa que sigue en la bandeja.
                cfg.Welcomed = true;
                cfg.Save();
                panel.ShowWelcome();
                Action once = null;
                once = () => { panel.Hidden -= once; Notify(S.ToastStillRunning, S.ToastStillRunningText); };
                panel.Hidden += once;
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
            GameStrategy current = active;
            if (current != null) current.ResetDetection();
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

        // Juego al que le falta un paso de configuracion (lo decide su estrategia), o null si esta todo bien.
        GameProfile MissingProtection()
        {
            foreach (GameStrategy st in strategies)
                if (st.NeedsSetup()) return st.Game;
            return null;
        }

        public void SendTestPush(Action<bool> done)
        {
            Notifier.Send(cfg, S.PushTestTitle, S.PushTestText, "white_check_mark", done);
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
                if (cfg.HotkeyVk == 0) return S.HotkeyNone;
                string s = "";
                if ((cfg.HotkeyMods & 0x2) != 0) s += "Ctrl+";
                if ((cfg.HotkeyMods & 0x1) != 0) s += "Alt+";
                if ((cfg.HotkeyMods & 0x4) != 0) s += "Shift+";
                return s + Win32.KeyName(cfg.HotkeyVk);
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
            GameProfile game = ActiveGame;
            AppState s = state;
            string text;
            StatusKind kind;
            if (paused || s == AppState.Paused) { text = cfg.HotkeyVk != 0 ? S.StatusPausedKey(HotkeyText) : S.StatusPaused; kind = StatusKind.Paused; }
            else if (game != null && s == AppState.InMatch) { text = S.StatusInMatch(game.Name); kind = StatusKind.Watching; }
            else if (game != null && s == AppState.Searching) { text = S.StatusSearching(game.Name); kind = StatusKind.Watching; }
            else { text = S.StatusIdle; kind = StatusKind.Idle; }
            PanelStatus st = new PanelStatus
            {
                Text = text,
                Kind = kind,
                Paused = paused,
                UpdateTag = pendingUpdate != null && !cfg.AutoUpdateCheck ? pendingUpdate.Tag : null,
                UpdateMessage = updateMessage,
            };
            st.ProtectionFix = MissingProtection();
            ui.Post(() =>
            {
                pauseItem.Text = paused ? S.MenuResume : S.MenuPause;
                panel.Refresh(st);
            });
        }

        // ---------- actualizaciones ----------

        void CheckUpdates(bool manual)
        {
            if (Updater.IsDevBuild)
            {
                if (manual) { updateMessage = S.DevBuild; PushStatus(); }
                return;
            }
            if (manual) { updateMessage = S.Checking; PushStatus(); }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Updater.Release r = null;
                try { r = Updater.CheckForUpdate(); }
                catch (Exception e)
                {
                    Log.Write("Updater: no pude consultar GitHub: " + e.Message);
                    if (manual) { updateMessage = S.CheckFailed; PushStatus(); }
                    return;
                }
                if (r == null)
                {
                    updateMessage = S.UpToDate;
                    PushStatus();
                    return;
                }
                bool isNew = pendingUpdate == null || pendingUpdate.Version != r.Version;
                pendingUpdate = r;
                Log.Write("Updater: hay una version nueva " + r.Tag);
                if (cfg.AutoUpdateCheck)
                {
                    // "Actualizar automaticamente": se instala sola, pero nunca con un juego abierto (podria estar
                    // buscando partida); en ese caso, al cerrarlo (ver Tick).
                    if (ActiveGame == null) { ui.Post(InstallUpdate); return; }
                    installWhenGameCloses = true;
                    updateMessage = S.UpdateWhenGameCloses(r.Tag);
                    PushStatus();
                    return;
                }
                updateMessage = S.Available(r.Tag);
                PushStatus();
                if (isNew) Notify(S.ToastUpdate(r.Tag), S.ToastUpdateText, S.ToastUpdateButton, InstallUpdate);
            });
        }

        public void InstallUpdate()
        {
            Updater.Release r = pendingUpdate;
            if (r == null) return;
            if (Interlocked.CompareExchange(ref installing, 1, 0) != 0) return;   // ya se esta instalando
            // GSI y el estado de Steam se miran para todos los juegos (no solo el vigilado): cualquier partida en curso frena.
            GameStrategy playing = active;
            if (gsi.InAnyMatch() || Presence.Current == PresenceState.InMatch || (playing != null && playing.InMatch))
            {
                updateMessage = S.UpdateAfterMatch;
                installing = 0;
                PushStatus();
                return;
            }
            updateMessage = S.Updating;
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
                    updateMessage = S.UpdateFailed;
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

        GameProfile ActiveGame { get { GameStrategy a = active; return a == null ? null : a.Game; } }

        // Primer juego habilitado que este abierto (en el orden de GameProfile.All).
        GameStrategy FindRunning()
        {
            foreach (GameStrategy s in strategies)
                if (cfg.For(s.Game).Enabled && s.IsRunning()) return s;
            return null;
        }

        // Devuelve cuantos ms esperar hasta el proximo tick. Lo propio de cada juego esta en su estrategia.
        int Tick()
        {
            GameStrategy now = FindRunning();
            if (now != active)
            {
                // La pausa dura hasta cerrar el juego: asi nunca queda apagado sin querer para la proxima vez.
                if (now == null && active != null && paused)
                {
                    Log.Write(active.Game.Name + " cerrado: se reanuda solo");
                    SetPaused(false);
                }
                if (now == null && installWhenGameCloses)
                {
                    installWhenGameCloses = false;
                    ui.Post(InstallUpdate);
                }
                if (active != null) active.Deactivated();
                active = now;
                if (now != null) { now.Reset(); Log.Write(now.Game.Name + " abierto"); }
                state = AppState.Starting;
            }
            Presence.Watch(cfg.UseSteamStatus && active != null ? active.Game : null);
            if (paused) { SetState(AppState.Paused); return 500; }
            if (active == null) { SetState(AppState.GameClosed); return 2000; }
            return active.Tick();
        }

        // ---------- IGameHost: lo que las estrategias le piden a la app ----------

        public Gsi Gsi { get { return gsi; } }
        public bool Paused { get { return paused; } }
        public DateTime LastAccept { get { return lastAccept; } }
        public void ForgetLastAccept() { lastAccept = DateTime.MinValue; }

        public void MatchAccepted(GameProfile game, string how, Action onClick = null)
        {
            lastAccept = DateTime.Now;
            Log.Write(game.Name + ": partida encontrada, aceptada " + how);
            Notify(S.ToastAccepted, S.ToastAcceptedText(game.Name), null, null, onClick);
            if (cfg.Sound) Beep(true);
            Notifier.Phone(cfg, S.ToastAccepted, S.ToastAcceptedText(game.Name), "video_game");
        }

        public void MatchStateChanged(GameProfile game, string oldState, string newState)
        {
            bool recentlyAccepted = lastAccept != DateTime.MinValue && (DateTime.Now - lastAccept).TotalMinutes < 5;
            if (oldState == null && newState != null && cfg.NotifyMatchStarted && recentlyAccepted)
            {
                Notify(S.ToastStarted(game.Name), S.ToastStartedText);
                if (cfg.Sound) Beep(true);
                Notifier.Phone(cfg, S.ToastStarted(game.Name), S.ToastStartedText, "crossed_swords");
            }
            // Arranco una partida que no aceptamos nosotros: o no vimos el popup, o el jugador fue mas rapido.
            // Las capturas de ese momento sirven para ajustar el detector.
            if (oldState == null && newState != null && !paused && !recentlyAccepted) Samples.Missed(game);
            if (newState != null) { lastAccept = DateTime.MinValue; Samples.Forget(); }   // ya estamos en partida
        }

        // ---------- estado e interfaz ----------

        public void SetState(AppState s)
        {
            if (s == state) return;
            state = s;
            ui.Post(() =>
            {
                Icon icon = s == AppState.Paused ? iconOff : iconOn;
                tray.Icon = icon;
                string name = ActiveGame != null ? ActiveGame.Name : "";
                string text = s == AppState.Paused ? S.TooltipPaused(cfg.HotkeyVk != 0 ? HotkeyText : null)
                            : s == AppState.GameClosed || ActiveGame == null ? S.TooltipIdle
                            : s == AppState.InMatch ? S.TooltipInMatch(name)
                            : S.TooltipSearching(name);
                tray.Text = Truncate(text, 63);
            });
            PushStatus();
        }

        static string Truncate(string s, int max) { return s.Length <= max ? s : s.Substring(0, max); }

        // Notificacion nativa de Windows (logo, texto y boton opcional); si no hay toasts, el aviso clasico de la bandeja.
        // Al tocarla se abre el panel (o lo que diga onClick). Los clicks llegan en otro hilo, por eso pasan por ui.Post.
        public void Notify(string title, string text, string button = null, Action onButton = null, Action onClick = null)
        {
            Action click = onClick ?? OpenPanel;
            // Siempre desde el hilo de la interfaz: el notificador de WinRT se creo ahi y desde el hilo del escaneo falla
            // (RPC_E_WRONG_THREAD), y Windows terminaba mostrando el globo viejo.
            ui.Post(() =>
            {
                if (useToasts && Toast.Show(title, text, button,
                        onButton == null ? (Action)null : () => ui.Post(onButton), () => ui.Post(click)))
                    return;
                tray.ShowBalloonTip(4000, title, string.IsNullOrEmpty(text) ? " " : text, ToolTipIcon.None);
            });
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
