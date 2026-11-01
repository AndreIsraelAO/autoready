// Pruebas automaticas de AutoReady. Corren con tests.bat (localmente) y en GitHub Actions en cada push y antes de cada
// release. No usan la PC del usuario: configuracion propia, cliente de LoL falso y GSI en un puerto libre.
// Uso: AutoReadyTests.exe <carpeta de fixtures>   (exit 0 = todo bien)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AutoReady.Tests
{
    static class Program
    {
        static int passed, failed;
        static string fixtures;

        static int Main(string[] args)
        {
            Log.Enabled = false;
            fixtures = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures");

            Group("Detector", DetectorTests);
            Group("Perfiles de juegos", ProfileTests);
            Group("Estado de Steam", PresenceTests);
            Group("Opciones de lanzamiento de Steam", SteamConfigTests);
            Group("Codigo del celular", TopicTests);
            Group("Textos EN/ES", StringTests);
            Group("GSI", GsiTests);
            Group("League of Legends (API del cliente)", LeagueTests);
            Group("QR", QrTests);
            Group("Instalador y desinstalador", SetupTests);
            Group("Tecla de pausa (panel)", HotkeyTests);
            Group("Accesos directos", ShortcutTests);

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "OK: " + passed + " pruebas" : "FALLARON " + failed + " de " + (passed + failed));
            return failed == 0 ? 0 : 1;
        }

        // ---------- mini framework ----------

        static void Group(string name, Action body)
        {
            Console.WriteLine("== " + name);
            try { body(); }
            catch (Exception e) { Fail(name + ": excepcion " + e); }
        }

        static void Check(bool ok, string what)
        {
            if (ok) { passed++; return; }
            Fail(what);
        }

        static void Fail(string what)
        {
            failed++;
            Console.WriteLine("  FALLA: " + what);
        }

        static void Near(Point p, int x, int y, int tol, string what)
        {
            Check(Math.Abs(p.X - x) <= tol && Math.Abs(p.Y - y) <= tol, what + " (esperado ~" + x + "," + y + ", vino " + p.X + "," + p.Y + ")");
        }

        static Config NewConfig()
        {
            return Config.Defaults();
        }

        // ---------- detector ----------

        // Capturas sinteticas (generadas, sin contenido de terceros) con el popup de cada juego y pantallas sin popup.
        static void DetectorTests()
        {
            Config cfg = NewConfig();
            Expect("popup-1080p.png", GameProfile.Dota2, cfg, 961, 538);
            Expect("popup-ultrawide.png", GameProfile.Dota2, cfg, 1720, 717);
            Expect("cs2-1080p.png", GameProfile.Cs2, cfg, 960, 589);
            Expect("cs2-ultrawide.png", GameProfile.Cs2, cfg, 1720, 785);
            // Pantallas sin popup: nunca tiene que encontrar nada (un falso positivo seria un click en cualquier lado).
            foreach (string f in new[] { "grass.png", "menu.png" })
                foreach (GameProfile g in new[] { GameProfile.Dota2, GameProfile.Cs2 })
                    using (Bitmap b = new Bitmap(Path.Combine(fixtures, f)))
                        Check(Detector.FindButton(b, g, cfg.For(g).Detector) == null, f + " no tiene popup de " + g.Name);
        }

        static void Expect(string file, GameProfile g, Config cfg, int x, int y)
        {
            using (Bitmap b = new Bitmap(Path.Combine(fixtures, file)))
            {
                Detector.Hit hit = Detector.FindButton(b, g, cfg.For(g).Detector);
                Check(hit != null, file + ": encuentra el boton de " + g.Name);
                if (hit != null) Near(hit.Center, x, y, 8, file + ": click en el centro del boton");
            }
        }

        // ---------- perfiles ----------

        static void ProfileTests()
        {
            HashSet<string> ids = new HashSet<string>();
            foreach (GameProfile g in GameProfile.All)
            {
                Check(ids.Add(g.Id), "id unico: " + g.Id);
                Check(!string.IsNullOrEmpty(g.Name) && !string.IsNullOrEmpty(g.Process), g.Id + ": nombre y proceso");
                Check(g.NewStrategy != null && g.DefaultDetector != null, g.Id + ": estrategia y detector por defecto");
                GameStrategy s = g.NewStrategy(g, new FakeHost());
                Check(s != null && s.Game == g, g.Id + ": crea su estrategia");
                // Juegos por pantalla: necesitan GSI (CfgDir + StateKey + AppId) para la proteccion en partida.
                // Un juego sin GSI tiene que declararlo (NoGsi): corre siempre en modo seguro y nunca acepta con Enter.
                if (s is ScreenStrategy && !g.NoGsi) Check(g.CfgDir != null && g.StateKey != null && g.AppId > 0, g.Id + ": tiene GSI");
                if (g.NoGsi) Check(g.CfgDir == null && !g.AcceptWithEnter && !g.EnabledByDefault, g.Id + ": sin GSI, sin Enter y apagado hasta medirlo");
            }
            Check(!Config.Defaults().For(GameProfile.Deadlock).Enabled && Config.Defaults().For(GameProfile.Dota2).Enabled, "Deadlock arranca apagado, Dota 2 prendido");
            Check(GameProfile.ById("deadlock") == GameProfile.Deadlock && GameProfile.ByAppId(1422450) == GameProfile.Deadlock, "Deadlock: perfil por id y por AppId");
            Check(Installer.IsQuietInstall(new[] { "--install", "--quiet" }) && !Installer.IsQuietInstall(new[] { "--install" })
                  && !Installer.IsQuietInstall(new[] { "--quiet" }) && !Installer.IsQuietInstall(new string[0]), "instalacion silenciosa solo con --install --quiet");
            // Sin estado de Steam (o sin GSI) no se sabe si esta buscando: nunca se considera en reposo.
            Check(!GameProfile.Dota2.NewStrategy(GameProfile.Dota2, new FakeHost()).IdleForUpdate, "Dota 2 sin estado de Steam: no hay reposo confirmado");
            Check(!GameProfile.Cs2.NewStrategy(GameProfile.Cs2, new FakeHost()).IdleForUpdate, "CS2: no hay reposo confirmado");
            Check(GameProfile.League.NewStrategy(GameProfile.League, new FakeHost()) is ClientApiStrategy, "LoL usa la API del cliente");
            Check(GameProfile.ByAppId(570) == GameProfile.Dota2 && GameProfile.ByAppId(730) == GameProfile.Cs2, "ByAppId");
        }

        // ---------- estado de Steam ----------

        static void PresenceTests()
        {
            GameProfile d = GameProfile.Dota2;
            Check(Presence.Classify("Finding A Match", d) == PresenceState.Finding, "buscando partida");
            Check(Presence.Classify("Main Menu", d) == PresenceState.Menu, "menu");
            Check(Presence.Classify("Playing Ranked: Pudge", d) == PresenceState.InMatch, "cualquier otro estado = en partida");
            Check(Presence.Classify("", d) == PresenceState.Unknown && Presence.Classify(null, d) == PresenceState.Unknown, "sin estado = desconocido");

            // Bug real: tras reabrir Dota, Steam siguio mostrando la partida anterior ("Turbo: Lvl 30 ...") y AutoReady
            // la tomo por una partida en curso: no miro la pantalla y no acepto la siguiente.
            StalePresence sp = new StalePresence();
            sp.Observe(PresenceState.InMatch, "Turbo: Lvl 30 Bloodseeker", true);
            Check(sp.Filter(PresenceState.InMatch, "Turbo: Lvl 30 Bloodseeker") == PresenceState.Unknown, "sesion nueva: el estado de la partida anterior se ignora");
            Check(sp.Filter(PresenceState.Finding, "Finding A Match") == PresenceState.Finding, "un estado nuevo cuenta");
            Check(sp.Filter(PresenceState.InMatch, "Hero Selection") == PresenceState.InMatch, "una partida nueva (texto distinto) cuenta");
            sp.Reset();
            sp.Observe(PresenceState.Unknown, null, true);
            sp.Observe(PresenceState.Menu, "Main Menu", true);
            sp.Observe(PresenceState.InMatch, "Hero Selection", true);   // no es la primera lectura: no se descarta
            Check(sp.Filter(PresenceState.InMatch, "Hero Selection") == PresenceState.InMatch, "empezando en el menu, la partida cuenta");
            StalePresence old = new StalePresence();
            old.Observe(PresenceState.InMatch, "Playing Ranked", false);   // el juego ya llevaba rato abierto: se confia
            Check(old.Filter(PresenceState.InMatch, "Playing Ranked") == PresenceState.InMatch, "sesion vieja: el estado de partida se respeta");
        }

        // ---------- opciones de lanzamiento ----------

        static void SteamConfigTests()
        {
            string vdf = "\"UserLocalConfigStore\"\n{\n \"Software\"\n {\n  \"Valve\"\n  {\n   \"Steam\"\n   {\n    \"apps\"\n    {\n" +
                         "     \"730\"\n     {\n      \"LaunchOptions\"\t\t\"+fps_max 0 -novid\"\n     }\n" +
                         "     \"570\"\n     {\n      \"LastPlayed\"\t\t\"123\"\n     }\n    }\n   }\n  }\n }\n}\n";
            Check(SteamConfig.LaunchOptionsFrom(vdf, 730) == "+fps_max 0 -novid", "lee las opciones de CS2 sin perder las del jugador");
            Check(SteamConfig.LaunchOptionsFrom(vdf, 570) == "", "juego sin opciones = vacio");
            Check(SteamConfig.LaunchOptionsFrom(vdf, 999) == "", "juego que no esta = vacio");
        }

        // ---------- codigo del celular ----------

        static void TopicTests()
        {
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < 2000; i++)
            {
                string t = Config.NewTopic();
                if (!Regex.IsMatch(t, "^ar-[a-z0-9]{5}$")) { Fail("formato del codigo: " + t); return; }
                if (Regex.IsMatch(t.Substring(3), "[01ilo]")) { Fail("letras que se confunden: " + t); return; }
                seen.Add(t);
            }
            Check(seen.Count > 1990, "codigos aleatorios (" + seen.Count + " distintos de 2000)");
        }

        // ---------- textos ----------

        // Cada texto de la interfaz existe en los dos idiomas y no quedo vacio. Los que tienen parametro se prueban con
        // un nombre de juego.
        static void StringTests()
        {
            bool original = Lang.Spanish;
            try
            {
                foreach (MemberInfo m in typeof(S).GetMembers(BindingFlags.Public | BindingFlags.Static))
                {
                    string es = Text(m, true), en = Text(m, false);
                    if (es == null && en == null) continue;   // no es un texto
                    Check(!string.IsNullOrEmpty(es) && !string.IsNullOrEmpty(en), "texto vacio: S." + m.Name);
                    if (!string.IsNullOrEmpty(es) && es == en && !IsSameInBothLanguages(m.Name))
                        Fail("S." + m.Name + " no esta traducido: \"" + en + "\"");
                    else passed++;
                }
            }
            finally { Lang.Spanish = original; }
        }

        // Las ventanas se arman en los dos idiomas sin errores. Desinstalar no borra la configuracion salvo que se pida,
        // y el foco arranca en Cancelar (lo destructivo no queda a un Enter).
        static void SetupTests()
        {
            bool original = Lang.Spanish;
            Exception error = null;
            System.Threading.Thread t = new System.Threading.Thread(() =>
            {
                try
                {
                    foreach (bool spanish in new[] { true, false })
                    {
                        Lang.Spanish = spanish;
                        System.Windows.Window install = new InstallWindow(), uninstall = new UninstallWindow();
                        Check(install.Title == (spanish ? "Instalar AutoReady" : "Install AutoReady"), "titulo del instalador");
                        Check(uninstall.Title == (spanish ? "Desinstalar AutoReady" : "Uninstall AutoReady"), "titulo del desinstalador");
                        System.Collections.Generic.List<Toggle> toggles = new System.Collections.Generic.List<Toggle>();
                        FindToggles(uninstall.Content as System.Windows.DependencyObject, toggles);
                        Check(toggles.Count == 1 && !toggles[0].IsOn, "borrar la configuracion arranca apagado");
                        Check(!((UninstallWindow)uninstall).Removed, "nada se borra sin tocar Desinstalar");
                        install.Close(); uninstall.Close();
                    }
                }
                catch (Exception e) { error = e; }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            Lang.Spanish = original;
            if (error != null) Fail("ventanas: " + error.Message);
        }

        class FakePanelHost : IPanelHost
        {
            public Config C = new Config();
            public int SetCalls, Vk = 0x79, Mods;
            public Config Cfg { get { return C; } }
            public void SetPaused(bool paused) { }
            public void SetPhone(bool on) { }
            public void FixProtection(GameProfile game) { }
            public string LaunchOptionsWithProtection(GameProfile game) { return ""; }
            public void SendTestPush(Action<bool> done) { }
            public void SetAutostart(bool on) { }
            public bool AutostartEnabled { get { return false; } }
            public void SetAutoUpdate(bool on) { }
            public void CheckUpdatesNow() { }
            public void InstallUpdate() { }
            public string HotkeyText { get { return Vk == 0 ? "-" : "VK" + Vk + "+" + Mods; } }
            public bool SetHotkey(int vk, int mods) { SetCalls++; Vk = vk; Mods = mods; return true; }
        }

        // Cambiar la tecla de pausa: Esc cancela y deja la anterior (sin tocar el atajo registrado); otra tecla la cambia.
        static void HotkeyTests()
        {
            Exception error = null;
            System.Threading.Thread t = new System.Threading.Thread(() =>
            {
                try
                {
                    FakePanelHost h = new FakePanelHost();
                    Panel p = new Panel(h) { Left = -5000, Top = 0, ShowActivated = false };
                    p.Show();
                    Func<string> label = () => ((System.Windows.Controls.TextBlock)Field(p, "hotkeyLabel")).Text;
                    string before = label();
                    Call(p, "StartHotkeyCapture");
                    Check(label() != before, "al tocar la tecla pide una nueva");
                    Press(p, System.Windows.Input.Key.Escape);
                    Check(label() == before && h.SetCalls == 0 && h.Vk == 0x79, "Esc cancela y deja la tecla anterior");
                    Check(!(bool)Field(p, "capturingHotkey"), "Esc sale del modo de captura");
                    Check(p.IsVisible, "Esc durante la captura no cierra el panel");
                    Call(p, "StartHotkeyCapture");
                    Press(p, System.Windows.Input.Key.F8);
                    Check(h.SetCalls == 1 && h.Vk == 0x77 && label() == h.HotkeyText, "otra tecla la cambia");
                    Call(p, "StartHotkeyCapture");
                    Press(p, System.Windows.Input.Key.A);
                    Check(h.SetCalls == 1 && h.Vk == 0x77, "una letra sola no se acepta");
                    Call(p, "StopHotkeyCapture");
                    Press(p, System.Windows.Input.Key.Escape);
                    Check(!p.IsVisible, "Esc fuera de la captura cierra el panel");
                    p.Close();
                }
                catch (Exception e) { error = e; }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) Fail("panel: " + error);
        }

        static object Field(object o, string name)
        {
            return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
        }

        static void Call(object o, string name)
        {
            o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, null);
        }

        static void Press(System.Windows.Window w, System.Windows.Input.Key key)
        {
            System.Windows.Input.KeyEventArgs e = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                System.Windows.PresentationSource.FromVisual(w), 0, key) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            w.RaiseEvent(e);
        }

        static void FindToggles(System.Windows.DependencyObject o, System.Collections.Generic.List<Toggle> found)
        {
            if (o == null) return;
            if (o is Toggle) found.Add((Toggle)o);
            foreach (object c in System.Windows.LogicalTreeHelper.GetChildren(o)) FindToggles(c as System.Windows.DependencyObject, found);
        }

        static bool IsSameInBothLanguages(string name) { return name == "SectionGeneral" || name == "Version"; }

        static string Text(MemberInfo m, bool spanish)
        {
            Lang.Spanish = spanish;
            PropertyInfo p = m as PropertyInfo;
            if (p != null && p.PropertyType == typeof(string)) return (string)p.GetValue(null, null);
            MethodInfo mi = m as MethodInfo;
            if (mi == null || mi.ReturnType != typeof(string) || mi.IsSpecialName) return null;
            ParameterInfo[] ps = mi.GetParameters();
            if (ps.Any(x => x.ParameterType != typeof(string))) return null;
            object[] args = ps.Select(x => (object)(x.Name == "tag" ? "v1.2.3" : x.Name == "v" ? "1.2.3" : "Dota 2")).ToArray();
            return (string)mi.Invoke(null, args);
        }

        // ---------- GSI ----------

        static void GsiTests()
        {
            int port = FreePort();
            Gsi gsi = new Gsi(port, "secreto");
            List<string> changes = new List<string>();
            gsi.StateChanged += (g, old, now) => { lock (changes) changes.Add(g.Id + ":" + (old ?? "-") + ">" + (now ?? "-")); };
            Check(gsi.Start(), "abre el puerto");

            string menu = "{\"provider\":{\"appid\":570},\"auth\":{\"token\":\"secreto\"}}";
            string match = "{\"provider\":{\"appid\":570},\"map\":{\"game_state\":\"DOTA_GAMERULES_STATE_GAME_IN_PROGRESS\"},\"auth\":{\"token\":\"secreto\"}}";
            string wrongToken = match.Replace("secreto", "otro");

            Post(port, menu, null);
            Thread.Sleep(200);
            Check(gsi.Connected(GameProfile.Dota2) && !gsi.InMatch(GameProfile.Dota2), "en el menu: conectado y fuera de partida");

            Post(port, wrongToken, null);
            Thread.Sleep(200);
            Check(!gsi.InMatch(GameProfile.Dota2), "ignora un POST con otro token");

            Post(port, match, "https://evil.example");
            Thread.Sleep(200);
            Check(!gsi.InMatch(GameProfile.Dota2), "ignora un POST de una pagina web (Origin)");

            Post(port, match, null);
            Thread.Sleep(200);
            Check(gsi.InMatch(GameProfile.Dota2), "en partida con el token correcto");
            Check(!gsi.InMatch(GameProfile.Cs2), "solo el juego que mando el estado");

            Post(port, menu, null);
            Thread.Sleep(200);
            Check(!gsi.InMatch(GameProfile.Dota2), "vuelve al menu");
            lock (changes) Check(changes.Contains("dota2:->DOTA_GAMERULES_STATE_GAME_IN_PROGRESS") && changes.Contains("dota2:DOTA_GAMERULES_STATE_GAME_IN_PROGRESS>-"),
                "avisa cuando empieza y termina la partida (" + string.Join(", ", changes) + ")");
        }

        static void Post(int port, string json, string origin)
        {
            using (TcpClient c = new TcpClient("127.0.0.1", port))
            using (NetworkStream s = c.GetStream())
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                string head = "POST / HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\n" +
                              (origin != null ? "Origin: " + origin + "\r\n" : "") +
                              "Content-Length: " + body.Length + "\r\n\r\n";
                byte[] h = Encoding.ASCII.GetBytes(head);
                s.Write(h, 0, h.Length);
                s.Write(body, 0, body.Length);
                s.ReadTimeout = 2000;
                try { s.Read(new byte[256], 0, 256); } catch (IOException) { }
            }
        }

        static int FreePort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        // ---------- accesos directos ----------

        // El acceso directo fija el ícono del exe (si no, Windows sigue mostrando el de una version vieja).
        static void ShortcutTests()
        {
            string dir = Path.Combine(Path.GetTempPath(), "autoready-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string exe = Path.Combine(dir, "AutoReady.exe"), lnk = Path.Combine(dir, "AutoReady.lnk");
                File.WriteAllText(exe, "x");
                Shortcut.Create(lnk, exe);
                Check(string.Equals(Shortcut.Target(lnk), exe, StringComparison.OrdinalIgnoreCase), "el acceso directo apunta al exe");
                Check(string.Equals(Shortcut.IconLocation(lnk), exe + ",0", StringComparison.OrdinalIgnoreCase), "el acceso directo fija el icono del exe");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---------- League of Legends ----------

        // La estrategia de LoL contra un cliente falso: acepta una sola vez, nunca en pausa ni en seleccion de campeones,
        // y avisa cuando arranca la seleccion.
        static void LeagueTests()
        {
            FakeLeague fake = new FakeLeague();
            FakeHost host = new FakeHost();
            Lcu lcu = new Lcu(() => fake.Lockfile);
            ClientApiStrategy s = new ClientApiStrategy(GameProfile.League, host, lcu);
            s.Reset();

            Check(Lcu.IsInMatch("ChampSelect") && Lcu.IsInMatch("InProgress") && !Lcu.IsInMatch("Matchmaking") && !Lcu.IsInMatch(null), "fases de partida");

            fake.Phase = "Matchmaking";
            s.Tick();
            Check(fake.Accepts == 0 && host.State == AppState.Searching, "buscando: no acepta nada");

            // Actualizar sin cerrar el juego: solo con el cliente en reposo (nunca buscando ni en partida).
            Check(!s.IdleForUpdate, "buscando: no se actualiza");
            fake.Phase = "Lobby";
            s.Tick();
            Check(s.IdleForUpdate, "en el lobby: se puede actualizar");
            fake.Phase = "Matchmaking";
            s.Tick();
            Check(!s.IdleForUpdate, "volvio a buscar: ya no se puede actualizar");
            Check(Lcu.IsIdle("None") && Lcu.IsIdle("Lobby") && !Lcu.IsIdle("ReadyCheck") && !Lcu.IsIdle("ChampSelect") && !Lcu.IsIdle(null), "fases en reposo");

            fake.Phase = "ReadyCheck";
            host.Paused = true;
            s.Tick();
            Check(fake.Accepts == 0, "en pausa: no acepta");

            host.Paused = false;
            s.Tick();
            Check(fake.Accepts == 1 && host.Accepted == 1, "partida encontrada: acepta una vez");
            s.Tick();
            Check(fake.Accepts == 1, "no acepta dos veces el mismo popup");

            fake.Phase = "ChampSelect";
            s.Tick();
            Check(s.InMatch && host.State == AppState.InMatch, "seleccion de campeones = en partida");
            Check(host.Changes.Contains("->ChampSelect"), "avisa que arranco la seleccion de campeones");
            fake.Phase = "InProgress";
            s.Tick();
            Check(fake.Accepts == 1, "en partida: no manda nada");

            fake.Phase = null;   // cliente cerrado o sin responder
            s.Tick();
            Check(fake.Accepts == 1, "sin cliente: no hace nada");

            // Si ya estaba en partida cuando se empezo a vigilar (por ejemplo, se cerro otro juego), no es una partida
            // nueva: no se avisa "arranco tu partida".
            FakeHost host2 = new FakeHost();
            ClientApiStrategy s2 = new ClientApiStrategy(GameProfile.League, host2, lcu);
            s2.Reset();
            fake.Phase = "InProgress";
            s2.Tick();
            Check(host2.Changes.Count == 0 && s2.InMatch, "ya en partida al empezar a vigilar: no avisa partida nueva");
            fake.Stop();
        }

        // ---------- QR ----------

        static void QrTests()
        {
            string url = "https://hercase.github.io/autoready/phone/?lang=es#ar-k7m2q";
            QrCode q = QrCode.Encode(url);
            Check(q.Size >= 21 && q.Size <= 41, "el link del celular entra en un QR chico (version " + ((q.Size - 17) / 4) + ")");
            // Patrones de esquina: los tres cuadrados de 7x7 con borde oscuro.
            Check(q[0, 0] && q[6, 6] && q[q.Size - 1, 0] && q[0, q.Size - 1], "tiene los patrones de las esquinas");
        }
    }

    // Lo minimo que una estrategia necesita de la app, para mirar que hace.
    class FakeHost : IGameHost
    {
        public Config Cfg { get; private set; }
        public Gsi Gsi { get; private set; }
        public bool Paused { get; set; }
        public AppState State;
        public int Accepted;
        public List<string> Changes = new List<string>();
        DateTime lastAccept = DateTime.MinValue;

        public FakeHost()
        {
            Config c = new Config();
            foreach (GameProfile g in GameProfile.All) c.Games[g.Id] = new Config.GameSettings { Detector = g.DefaultDetector() };
            c.CooldownAfterAcceptSec = 0;
            Cfg = c;
            Gsi = new Gsi(0, "x");
        }

        public void SetPaused(bool paused) { Paused = paused; }
        public void SetState(AppState state) { State = state; }
        public void Notify(string title, string text, string button = null, Action onButton = null, Action onClick = null) { }
        public void MatchAccepted(GameProfile game, string how, Action onClick = null) { Accepted++; lastAccept = DateTime.Now; }
        public void MatchStateChanged(GameProfile game, string oldState, string newState) { Changes.Add((oldState ?? "") + "->" + (newState ?? "")); }
        public DateTime LastAccept { get { return lastAccept; } }
        public void ForgetLastAccept() { lastAccept = DateTime.MinValue; }
    }

    // Cliente de LoL falso: lockfile temporal y una API HTTP minima en 127.0.0.1.
    class FakeLeague
    {
        public volatile string Phase = "None";
        public int Accepts;
        public readonly string Lockfile;
        readonly TcpListener listener;
        const string Secret = "clave";

        public FakeLeague()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Lockfile = Path.Combine(Path.GetTempPath(), "autoready-test-lockfile-" + port);
            File.WriteAllText(Lockfile, "LeagueClient:1:" + port + ":" + Secret + ":http");
            new Thread(Serve) { IsBackground = true }.Start();
        }

        public void Stop() { listener.Stop(); try { File.Delete(Lockfile); } catch { } }

        void Serve()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch { return; }
                using (c)
                using (NetworkStream s = c.GetStream())
                {
                    StreamReader r = new StreamReader(s, Encoding.ASCII);
                    string first = r.ReadLine(), line, auth = null;
                    if (first == null) continue;
                    while (!string.IsNullOrEmpty(line = r.ReadLine()))
                        if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) auth = line.Substring(14).Trim();
                    string[] parts = first.Split(' ');
                    string method = parts[0], path = parts[1], body = "{}";
                    int code = 200;
                    string phase = Phase;
                    if (auth != "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + Secret))) code = 401;
                    else if (phase == null) code = 503;
                    else if (path == "/lol-gameflow/v1/gameflow-phase") body = "\"" + phase + "\"";
                    else if (path == "/lol-matchmaking/v1/ready-check")
                    {
                        if (phase != "ReadyCheck") code = 404;
                        else body = "{\"state\":\"InProgress\",\"playerResponse\":\"" + (Accepts > 0 ? "Accepted" : "None") + "\"}";
                    }
                    else if (path == "/lol-matchmaking/v1/ready-check/accept" && method == "POST") { Accepts++; code = 204; body = ""; }
                    else code = 404;
                    byte[] b = Encoding.UTF8.GetBytes(body);
                    byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " X\r\nContent-Length: " + b.Length + "\r\nConnection: close\r\n\r\n");
                    s.Write(h, 0, h.Length);
                    s.Write(b, 0, b.Length);
                }
            }
        }
    }
}
