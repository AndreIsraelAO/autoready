using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ReadyUp
{
    enum AppState { Starting, DotaClosed, Paused, Searching, InMatch }

    class TrayApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly Config cfg;
        readonly Gsi gsi;
        readonly HiddenWindow ui;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem statusItem, gsiItem, pauseItem, phoneItem, soundItem, pickItem, autostartItem;
        readonly Icon iconOn, iconOff, iconWait;

        volatile bool paused;
        volatile bool flashRequested;
        volatile bool running = true;
        AppState state = AppState.Starting;
        DateTime cooldownUntil = DateTime.MinValue;
        DateTime lastAccept = DateTime.MinValue;
        DateTime dotaSeenSince = DateTime.MinValue;
        bool gsiHintShown;
        int consecutiveHits;

        public TrayApp()
        {
            cfg = Config.Load();
            iconOn = MakeIcon(Color.FromArgb(60, 170, 70));
            iconOff = MakeIcon(Color.FromArgb(130, 130, 130));
            iconWait = MakeIcon(Color.FromArgb(215, 160, 40));

            ui = new HiddenWindow();
            ui.DotaFlashed += () => { flashRequested = true; };

            statusItem = new ToolStripMenuItem("Iniciando...") { Enabled = false };
            gsiItem = new ToolStripMenuItem("GSI: esperando a Dota") { Enabled = false };
            pauseItem = new ToolStripMenuItem("Pausar", null, (s, e) => TogglePause());
            phoneItem = new ToolStripMenuItem("Avisos al celular", null, (s, e) => TogglePhone()) { Checked = cfg.NotifyPhone };
            soundItem = new ToolStripMenuItem("Sonido", null, (s, e) => { cfg.Sound = !cfg.Sound; soundItem.Checked = cfg.Sound; cfg.Save(); }) { Checked = cfg.Sound };
            pickItem = new ToolStripMenuItem("Avisar cuando empieza el pick", null, (s, e) => { cfg.NotifyPickStarted = !cfg.NotifyPickStarted; pickItem.Checked = cfg.NotifyPickStarted; cfg.Save(); }) { Checked = cfg.NotifyPickStarted };
            autostartItem = new ToolStripMenuItem("Iniciar con Windows", null, (s, e) => ToggleAutostart()) { Checked = AutostartEnabled() };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(gsiItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(phoneItem);
            menu.Items.Add(new ToolStripMenuItem("Conectar celular...", null, (s, e) => ShowPhoneSetup()));
            menu.Items.Add(soundItem);
            menu.Items.Add(pickItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Guardar captura de prueba", null, (s, e) => SaveDebugCapture()));
            menu.Items.Add(new ToolStripMenuItem("Abrir carpeta de ReadyUp", null, (s, e) => Process.Start("explorer.exe", Config.Dir)));
            menu.Items.Add(autostartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Salir", null, (s, e) => Quit()));

            tray = new NotifyIcon { Icon = iconWait, Text = "ReadyUp", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (s, e) => TogglePause();

            gsi = new Gsi(cfg.GsiPort);
            gsi.InstallConfig();
            gsi.Start();
            gsi.StateChanged += OnGameStateChanged;

            Log.Write("ReadyUp iniciado");
            Thread scanner = new Thread(ScanLoop) { IsBackground = true, Name = "scanner" };
            scanner.Start();
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

        // Devuelve cuantos ms esperar hasta el proximo tick.
        int Tick()
        {
            IntPtr dota = Win32.FindDotaWindow();
            if (dota == IntPtr.Zero)
            {
                dotaSeenSince = DateTime.MinValue;
                SetState(AppState.DotaClosed);
                return 2000;
            }
            if (dotaSeenSince == DateTime.MinValue) dotaSeenSince = DateTime.Now;
            MaybeShowGsiHint();

            if (paused) { SetState(AppState.Paused); return 500; }
            if (gsi.InMatch) { SetState(AppState.InMatch); return 1000; }
            SetState(AppState.Searching);
            if (DateTime.Now < cooldownUntil) return 500;

            // Sin GSI no sabemos si ya estas en partida: despues de aceptar seguimos un rato (por si alguien
            // no acepto y volves a la cola) y despues pausamos para no hacer clicks dentro del juego.
            if (!gsi.Connected && lastAccept != DateTime.MinValue &&
                (DateTime.Now - lastAccept).TotalSeconds > cfg.FallbackScanSecAfterAccept)
            {
                lastAccept = DateTime.MinValue;
                paused = true;
                SetState(AppState.Paused);
                Notify("ReadyUp en pausa", "Sin GSI no se si estas en partida, asi que me pause. Doble click en el icono para rearmar.", false);
                return 500;
            }

            if (Win32.IsIconic(dota))
            {
                // Dota minimizado: no se puede capturar. Cuando encuentra partida hace titilar la barra de tareas;
                // en ese momento lo restauramos para poder ver el boton.
                if (!flashRequested) return cfg.ScanIntervalMs;
                flashRequested = false;
                Log.Write("Dota titilo estando minimizado: lo traigo al frente");
                Win32.Focus(dota);
                return 700;
            }

            Detector.Hit hit;
            using (Bitmap shot = Win32.Capture(dota))
            {
                if (shot == null) return cfg.ScanIntervalMs;
                hit = Detector.Find(shot, cfg.Detector);
            }
            if (hit == null) { consecutiveHits = 0; return cfg.ScanIntervalMs; }

            // Pedimos dos detecciones seguidas para evitar falsos positivos.
            if (++consecutiveHits < 2) return 250;
            consecutiveHits = 0;
            Accept(dota, hit);
            return 500;
        }

        void Accept(IntPtr dota, Detector.Hit hit)
        {
            Win32.Focus(dota);
            Thread.Sleep(250);
            Rectangle rc = Win32.ClientRectOnScreen(dota);
            Point p = hit.Center;
            Win32.Click(rc.X + p.X, rc.Y + p.Y);

            lastAccept = DateTime.Now;
            cooldownUntil = DateTime.Now.AddSeconds(cfg.CooldownAfterAcceptSec);
            Log.Write("Partida encontrada: click en ACCEPT (" + (rc.X + p.X) + "," + (rc.Y + p.Y) + ")");
            Notify("¡Partida aceptada!", "ReadyUp acepto la partida por vos.", true);
            Notifier.Phone(cfg, "¡Partida aceptada!", "ReadyUp acepto tu partida de Dota 2. ¡Volve a la PC!", "video_game");
        }

        void OnGameStateChanged(string oldState, string newState)
        {
            bool recentlyAccepted = lastAccept != DateTime.MinValue && (DateTime.Now - lastAccept).TotalMinutes < 5;
            if (oldState == null && newState != null && cfg.NotifyPickStarted && recentlyAccepted)
            {
                Notify("¡Empezo el pick!", "Volve, que ya estan eligiendo heroes.", true);
                Notifier.Phone(cfg, "¡Empezo el pick!", "Ya estan eligiendo heroes. ¡Volve!", "crossed_swords");
            }
            if (newState != null) lastAccept = DateTime.MinValue;   // ya estamos en partida
        }

        void MaybeShowGsiHint()
        {
            if (gsiHintShown || gsi.Connected) return;
            if ((DateTime.Now - dotaSeenSince).TotalSeconds < 90) return;
            gsiHintShown = true;
            Log.Write("GSI: Dota abierto hace 90s y no llego nada");
            Notify("Activa GSI para mas seguridad",
                   "En Steam: Dota 2 > Propiedades > Opciones de lanzamiento, agrega -gamestateintegration y reinicia Dota.", false);
        }

        // ---------- estado e interfaz ----------

        void SetState(AppState s)
        {
            if (s == state) return;
            state = s;
            ui.Post(() =>
            {
                string text;
                Icon icon;
                switch (s)
                {
                    case AppState.DotaClosed: text = "Dota cerrado"; icon = iconWait; break;
                    case AppState.Paused: text = "En pausa"; icon = iconOff; break;
                    case AppState.InMatch: text = "En partida (no escanea)"; icon = iconOn; break;
                    default: text = "Armado: esperando partida"; icon = iconOn; break;
                }
                statusItem.Text = text;
                pauseItem.Text = paused ? "Armar" : "Pausar";
                gsiItem.Text = gsi.Connected ? "GSI: conectado" : "GSI: no conectado";
                tray.Icon = icon;
                tray.Text = "ReadyUp - " + text;
            });
        }

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
            IntPtr dota = Win32.FindDotaWindow();
            if (dota == IntPtr.Zero) { MessageBox.Show("Dota no esta abierto.", "ReadyUp"); return; }
            using (Bitmap shot = Win32.Capture(dota))
            {
                if (shot == null) { MessageBox.Show("No pude capturar Dota (¿esta minimizado?).", "ReadyUp"); return; }
                Detector.Hit hit = Detector.Find(shot, cfg.Detector);
                string file = Path.Combine(Config.Dir, "captura-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                using (Bitmap annotated = Detector.Annotate(shot, hit, cfg.Detector))
                    annotated.Save(file);
                Log.Write("Captura de prueba: " + file + (hit != null ? " (boton encontrado)" : " (sin boton)"));
                Process.Start("explorer.exe", "/select,\"" + file + "\"");
            }
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

        static Icon MakeIcon(Color color)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush b = new SolidBrush(color)) g.FillEllipse(b, 1, 1, 30, 30);
                    using (Pen p = new Pen(Color.FromArgb(25, 25, 25), 2)) g.DrawEllipse(p, 1, 1, 30, 30);
                    using (Font f = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString("R", f, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // Ventana invisible: recibe los avisos de "ventana titilando" del shell y sirve para volver al hilo de la UI.
    class HiddenWindow : Form
    {
        readonly uint shellHookMsg;
        public event Action DotaFlashed;

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
            if (m.Msg == (int)shellHookMsg && m.WParam.ToInt32() == Win32.HSHELL_FLASH &&
                m.LParam == Win32.FindDotaWindow())
            {
                Action h = DotaFlashed;
                if (h != null) h();
            }
            base.WndProc(ref m);
        }
    }
}
