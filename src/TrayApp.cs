using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ReadyUp
{
    enum AppState { Starting, GameClosed, Paused, Searching, InMatch }

    class TrayApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly Config cfg;
        readonly Gsi gsi;
        readonly HiddenWindow ui;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem statusItem, gsiItem, pauseItem, phoneItem, soundItem, matchItem, autostartItem;
        readonly ToolStripMenuItem updateItem, autoUpdateItem;
        System.Threading.Timer updateTimer;
        Updater.Release pendingUpdate;
        readonly Icon iconOn, iconOff, iconWait;

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

        public TrayApp()
        {
            cfg = Config.Load();
            iconOn = MakeIcon(TrayTint.Normal);
            iconOff = MakeIcon(TrayTint.Gray);
            iconWait = MakeIcon(TrayTint.Amber);

            ui = new HiddenWindow();
            ui.WindowFlashed += h => { flashedWindow = h; };

            statusItem = new ToolStripMenuItem("Iniciando...") { Enabled = false };
            gsiItem = new ToolStripMenuItem("GSI: esperando al juego") { Enabled = false };
            pauseItem = new ToolStripMenuItem("Pausar", null, (s, e) => TogglePause());
            phoneItem = new ToolStripMenuItem("Avisos al celular", null, (s, e) => TogglePhone()) { Checked = cfg.NotifyPhone };
            soundItem = new ToolStripMenuItem("Sonido", null, (s, e) => { cfg.Sound = !cfg.Sound; soundItem.Checked = cfg.Sound; cfg.Save(); }) { Checked = cfg.Sound };
            matchItem = new ToolStripMenuItem("Avisar cuando arranca la partida", null, (s, e) => { cfg.NotifyMatchStarted = !cfg.NotifyMatchStarted; matchItem.Checked = cfg.NotifyMatchStarted; cfg.Save(); }) { Checked = cfg.NotifyMatchStarted };
            autostartItem = new ToolStripMenuItem("Iniciar con Windows", null, (s, e) => ToggleAutostart()) { Checked = AutostartEnabled() };
            updateItem = new ToolStripMenuItem("Actualizar", null, (s, e) => InstallUpdate()) { Visible = false, Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) };
            autoUpdateItem = new ToolStripMenuItem("Buscar actualizaciones automaticamente", null, (s, e) => ToggleAutoUpdate()) { Checked = cfg.AutoUpdateCheck };

            ToolStripMenuItem gamesItem = new ToolStripMenuItem("Juegos");
            foreach (GameProfile g in GameProfile.All)
            {
                GameProfile game = g;
                ToolStripMenuItem item = new ToolStripMenuItem(g.Name) { Checked = cfg.For(g).Enabled };
                item.Click += (s, e) =>
                {
                    cfg.For(game).Enabled = !cfg.For(game).Enabled;
                    item.Checked = cfg.For(game).Enabled;
                    cfg.Save();
                };
                gamesItem.DropDownItems.Add(item);
            }

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("ReadyUp v" + Updater.CurrentVersionText) { Enabled = false });
            menu.Items.Add(updateItem);
            menu.Items.Add(statusItem);
            menu.Items.Add(gsiItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(pauseItem);
            menu.Items.Add(gamesItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(phoneItem);
            menu.Items.Add(new ToolStripMenuItem("Conectar celular...", null, (s, e) => ShowPhoneSetup()));
            menu.Items.Add(soundItem);
            menu.Items.Add(matchItem);
            menu.Items.Add(new ToolStripSeparator());
            // Herramientas para cuando algo no anda: no hacen falta en el uso normal.
            ToolStripMenuItem diagItem = new ToolStripMenuItem("Diagnostico");
            diagItem.DropDownItems.Add(new ToolStripMenuItem("Guardar captura (con el popup en pantalla)", null, (s, e) => SaveDebugCapture()));
            diagItem.DropDownItems.Add(new ToolStripMenuItem("Abrir carpeta de configuracion y logs", null, (s, e) => OpenDataFolder()));
            menu.Items.Add(autostartItem);
            menu.Items.Add(autoUpdateItem);
            menu.Items.Add(new ToolStripMenuItem("Buscar actualizaciones ahora", null, (s, e) => CheckUpdates(true)));
            menu.Items.Add(diagItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Hecho por keis.studio", null, (s, e) => Process.Start("https://keis.studio")));
            menu.Items.Add(new ToolStripMenuItem("Salir", null, (s, e) => Quit()));

            tray = new NotifyIcon { Icon = iconWait, Text = "ReadyUp", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (s, e) => TogglePause();

            gsi = new Gsi(cfg.GsiPort);
            foreach (GameProfile g in GameProfile.All) gsi.InstallConfig(g);
            gsi.Start();
            gsi.StateChanged += OnGameStateChanged;

            Log.Write("ReadyUp v" + Updater.CurrentVersionText + " iniciado");
            Thread scanner = new Thread(ScanLoop) { IsBackground = true, Name = "scanner" };
            scanner.Start();

            // Primer chequeo a los 30 s de arrancar y despues una vez por dia.
            updateTimer = new System.Threading.Timer(_ => { if (cfg.AutoUpdateCheck) CheckUpdates(false); },
                                                     null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(24));
        }

        // ---------- actualizaciones ----------

        void CheckUpdates(bool manual)
        {
            if (Updater.IsDevBuild)
            {
                if (manual) ui.Post(() => MessageBox.Show("Esta es una build de desarrollo (v0.0.0): no se actualiza sola.", "ReadyUp"));
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Updater.Release r = null;
                try { r = Updater.CheckForUpdate(); }
                catch (Exception e)
                {
                    Log.Write("Updater: no pude consultar GitHub: " + e.Message);
                    if (manual) ui.Post(() => MessageBox.Show("No pude buscar actualizaciones:\n" + e.Message, "ReadyUp"));
                    return;
                }
                if (r == null)
                {
                    if (manual) ui.Post(() => MessageBox.Show("Ya tenes la ultima version (v" + Updater.CurrentVersionText + ").", "ReadyUp"));
                    return;
                }
                bool isNew = pendingUpdate == null || pendingUpdate.Version != r.Version;
                pendingUpdate = r;
                Log.Write("Updater: hay una version nueva " + r.Tag);
                ui.Post(() =>
                {
                    updateItem.Text = "Actualizar a " + r.Tag;
                    updateItem.Visible = true;
                    if (isNew || manual)
                        tray.ShowBalloonTip(8000, "ReadyUp " + r.Tag + " disponible", "Click derecho en el icono > Actualizar.", ToolTipIcon.Info);
                });
            });
        }

        void InstallUpdate()
        {
            Updater.Release r = pendingUpdate;
            if (r == null) return;
            if (gsi.InAnyMatch())
            {
                MessageBox.Show("Estas en partida: actualiza cuando termine.", "ReadyUp");
                return;
            }
            updateItem.Enabled = false;
            updateItem.Text = "Actualizando a " + r.Tag + "...";
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
                    ui.Post(() =>
                    {
                        updateItem.Enabled = true;
                        updateItem.Text = "Actualizar a " + r.Tag;
                        MessageBox.Show("No se pudo actualizar:\n" + e.Message, "ReadyUp");
                    });
                }
            });
        }

        void ToggleAutoUpdate()
        {
            cfg.AutoUpdateCheck = !cfg.AutoUpdateCheck;
            autoUpdateItem.Checked = cfg.AutoUpdateCheck;
            cfg.Save();
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
            if (game == null) { SetState(AppState.GameClosed); return 2000; }
            MaybeShowGsiHint(game);

            if (paused) { SetState(AppState.Paused); return 500; }
            if (gsi.InMatch(game)) { SetState(AppState.InMatch); return 1000; }
            SetState(AppState.Searching);
            if (DateTime.Now < cooldownUntil) return 500;

            // Sin GSI no sabemos si ya estas en partida: despues de aceptar seguimos un rato (por si alguien
            // no acepto y volves a la cola) y despues pausamos para no hacer clicks dentro del juego.
            if (!gsi.Connected(game) && lastAccept != DateTime.MinValue &&
                (DateTime.Now - lastAccept).TotalSeconds > cfg.FallbackScanSecAfterAccept)
            {
                lastAccept = DateTime.MinValue;
                paused = true;
                SetState(AppState.Paused);
                Notify("ReadyUp en pausa", "Sin GSI no se si estas en partida, asi que me pause. Doble click en el icono para rearmar.", false);
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
            Notify("¡Partida aceptada!", "ReadyUp acepto tu partida de " + game.Name + ".", true);
            Notifier.Phone(cfg, "¡Partida aceptada!", "ReadyUp acepto tu partida de " + game.Name + ". ¡Volve a la PC!", "video_game");
        }

        void OnGameStateChanged(GameProfile game, string oldState, string newState)
        {
            bool recentlyAccepted = lastAccept != DateTime.MinValue && (DateTime.Now - lastAccept).TotalMinutes < 5;
            if (oldState == null && newState != null && cfg.NotifyMatchStarted && recentlyAccepted)
            {
                Notify(game.MatchStartedTitle, game.MatchStartedText, true);
                Notifier.Phone(cfg, game.MatchStartedTitle, game.MatchStartedText, "crossed_swords");
            }
            if (newState != null) lastAccept = DateTime.MinValue;   // ya estamos en partida
        }

        void MaybeShowGsiHint(GameProfile game)
        {
            if (gsiHintShown.Contains(game.Id) || gsi.Connected(game)) return;
            if ((DateTime.Now - gameSeenSince).TotalSeconds < 90) return;
            gsiHintShown.Add(game.Id);
            Log.Write("GSI " + game.Name + ": abierto hace 90s y no llego nada");
            Notify("Activa GSI para mas seguridad",
                   game.LaunchOptionHint ?? ("Reinicia " + game.Name + " para que cargue la integracion de ReadyUp."), false);
        }

        // ---------- estado e interfaz ----------

        void SetState(AppState s)
        {
            if (s == state) return;
            state = s;
            GameProfile game = activeGame;
            ui.Post(() =>
            {
                string name = game != null ? game.Name : "";
                string text;
                Icon icon;
                switch (s)
                {
                    case AppState.GameClosed: text = "Esperando a que abras un juego"; icon = iconWait; break;
                    case AppState.Paused: text = "En pausa"; icon = iconOff; break;
                    case AppState.InMatch: text = name + ": en partida (no escanea)"; icon = iconOn; break;
                    default: text = name + ": esperando partida"; icon = iconOn; break;
                }
                statusItem.Text = text;
                pauseItem.Text = paused ? "Armar" : "Pausar";
                gsiItem.Text = game == null ? "GSI: esperando al juego" : (gsi.Connected(game) ? "GSI: conectado" : "GSI: no conectado");
                tray.Icon = icon;
                tray.Text = Truncate("ReadyUp - " + text, 63);
            });
        }

        static string Truncate(string s, int max) { return s.Length <= max ? s : s.Substring(0, max); }

        void TogglePause()
        {
            paused = !paused;
            consecutiveHits = 0;
            if (!paused) lastAccept = DateTime.MinValue;
            pauseItem.Text = paused ? "Armar" : "Pausar";
            state = AppState.Starting;   // fuerza a refrescar icono en el proximo tick
            Log.Write(paused ? "Pausado por el usuario" : "Armado por el usuario");
        }

        void TogglePhone()
        {
            if (!cfg.NotifyPhone) { ShowPhoneSetup(); return; }
            cfg.NotifyPhone = false;
            phoneItem.Checked = false;
            cfg.Save();
        }

        void ShowPhoneSetup()
        {
            Clipboard.SetText(cfg.NtfyTopic);
            DialogResult r = MessageBox.Show(
                "Para recibir avisos en el celular:\n\n" +
                "1. Instala la app gratuita \"ntfy\" (Android / iPhone).\n" +
                "2. Toca \"+\" y pega este topic (ya esta copiado):\n\n" +
                "        " + cfg.NtfyTopic + "\n\n" +
                "3. Servidor: dejalo en ntfy.sh y toca Subscribe.\n\n" +
                "No compartas el topic: quien lo sepa puede leer tus avisos.\n\n" +
                "¿Activo los avisos y te mando uno de prueba?",
                "ReadyUp - Conectar celular", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r != DialogResult.Yes) return;
            cfg.NotifyPhone = true;
            phoneItem.Checked = true;
            cfg.Save();
            Notifier.Send(cfg, "ReadyUp conectado", "Los avisos funcionan. ¡Buena suerte en la cola!", "white_check_mark");
        }

        void SaveDebugCapture()
        {
            IntPtr hwnd;
            GameProfile game = FindRunningGame(out hwnd);
            if (game == null)
            {
                MessageBox.Show("Sirve para revisar la deteccion: abri Dota 2 o CS2, busca partida y usalo cuando aparezca el popup de ACCEPT.", "ReadyUp");
                return;
            }
            using (Bitmap shot = Win32.Capture(hwnd))
            {
                if (shot == null) { MessageBox.Show("No pude capturar " + game.Name + " (¿esta minimizado?).", "ReadyUp"); return; }
                Detector.Settings ds = cfg.For(game).Detector;
                Detector.Hit hit = Detector.Find(shot, ds);
                string file = Path.Combine(Config.Dir, "captura-" + game.Id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                using (Bitmap annotated = Detector.Annotate(shot, hit, ds))
                    annotated.Save(file);
                Log.Write("Captura de prueba: " + file + (hit != null ? " (boton encontrado)" : " (sin boton)"));
                Process.Start("explorer.exe", "/select,\"" + file + "\"");
            }
        }

        static void OpenDataFolder()
        {
            Directory.CreateDirectory(Config.Dir);
            Process.Start("explorer.exe", "\"" + Config.Dir + "\"");
        }

        static bool AutostartEnabled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("ReadyUp") != null;
        }

        void ToggleAutostart()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (AutostartEnabled()) k.DeleteValue("ReadyUp", false);
                else k.SetValue("ReadyUp", "\"" + Application.ExecutablePath + "\"");
            }
            autostartItem.Checked = AutostartEnabled();
        }

        void Notify(string title, string text, bool sound)
        {
            ui.Post(() => tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info));
            if (sound && cfg.Sound)
                ThreadPool.QueueUserWorkItem(_ => { Console.Beep(1500, 150); Console.Beep(1900, 200); });
        }

        void Quit()
        {
            running = false;
            tray.Visible = false;
            Log.Write("ReadyUp cerrado");
            ExitThread();
        }

        enum TrayTint { Normal, Gray, Amber }

        // Icono de la bandeja a partir del logo embebido: verde = armado, gris = pausa, ambar = esperando el juego.
        static Icon MakeIcon(TrayTint tint)
        {
            int size = SystemInformation.SmallIconSize.Width;
            string res = size > 16 ? "ReadyUp.tray-32.png" : "ReadyUp.tray-16.png";
            using (Stream s = typeof(TrayApp).Assembly.GetManifestResourceStream(res))
            using (Bitmap logo = new Bitmap(s))
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(logo, 0, 0, size, size);
                }
                if (tint != TrayTint.Normal)
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (c.A == 0 || c.G - c.R < 40) continue;   // solo la flecha verde
                            // Gris claro: tiene que leerse sobre la barra de tareas oscura.
                            int gray = Math.Min(255, (c.R + c.G + c.B) / 3 + 75);
                            Color t = tint == TrayTint.Gray
                                ? Color.FromArgb(c.A, gray, gray, gray)
                                : Color.FromArgb(c.A, Math.Min(255, c.G + 40), Math.Min(255, c.G * 3 / 4), 40);
                            bmp.SetPixel(x, y, t);
                        }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // Ventana invisible: recibe los avisos de "ventana titilando" del shell y sirve para volver al hilo de la UI.
    class HiddenWindow : Form
    {
        readonly uint shellHookMsg;
        public event Action<IntPtr> WindowFlashed;

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
            base.WndProc(ref m);
        }
    }
}
