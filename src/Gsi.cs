using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace AutoReady
{
    // Game State Integration: la forma oficial de Valve de saber en que estado esta el juego (Dota 2 y CS2).
    // El juego manda POSTs con JSON a un servidor local; cada POST trae el appid del juego que lo envia.
    // Lo usamos solo para saber si estas dentro de una partida (y ahi no escanear nada).
    class Gsi
    {
        const string CfgName = "gamestate_integration_autoready.cfg";

        class GameState
        {
            public DateTime LastUpdate = DateTime.MinValue;
            public string Phase;   // null = menu principal
        }

        readonly int port;
        readonly Dictionary<int, GameState> states = new Dictionary<int, GameState>();
        readonly object gate = new object();

        public event Action<GameProfile, string, string> StateChanged;   // (juego, anterior, nuevo)

        public Gsi(int port) { this.port = port; }

        public bool Connected(GameProfile g)
        {
            lock (gate)
            {
                GameState s;
                return states.TryGetValue(g.AppId, out s) && (DateTime.Now - s.LastUpdate).TotalSeconds < 30;
            }
        }

        public bool InMatch(GameProfile g)
        {
            lock (gate)
            {
                GameState s;
                return Connected(g) && states.TryGetValue(g.AppId, out s) && s.Phase != null;
            }
        }

        public bool InAnyMatch()
        {
            foreach (GameProfile g in GameProfile.All)
                if (InMatch(g)) return true;
            return false;
        }

        static List<string> SteamLibraries()
        {
            List<string> libs = new List<string>();
            string steam = null;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (k != null) steam = k.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(steam)) steam = @"C:\Program Files (x86)\Steam";
            libs.Add(Path.GetFullPath(steam));

            string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string lib = Path.GetFullPath(m.Groups[1].Value.Replace(@"\\", @"\"));
                    if (!libs.Exists(x => string.Equals(x, lib, StringComparison.OrdinalIgnoreCase))) libs.Add(lib);
                }
            return libs;
        }

        public static bool IsInstalled(GameProfile g)
        {
            foreach (string lib in SteamLibraries())
                if (Directory.Exists(Path.GetDirectoryName(Path.Combine(lib, g.CfgDir)))) return true;
            return false;
        }

        // Escribe el .cfg de GSI en cada instalacion encontrada del juego. Devuelve la cantidad escrita.
        public int InstallConfig(GameProfile g)
        {
            string content =
                "\"AutoReady\"\r\n{\r\n" +
                "    \"uri\"           \"http://127.0.0.1:" + port + "/\"\r\n" +
                "    \"timeout\"       \"5.0\"\r\n" +
                "    \"buffer\"        \"0.1\"\r\n" +
                "    \"throttle\"      \"0.5\"\r\n" +
                "    \"heartbeat\"     \"10.0\"\r\n" +
                "    \"data\"\r\n    {\r\n" +
                "        \"provider\"  \"1\"\r\n" +
                "        \"map\"       \"1\"\r\n" +
                "    }\r\n}\r\n";
            int written = 0;
            foreach (string lib in SteamLibraries())
            {
                string dir = Path.Combine(lib, g.CfgDir);
                // La carpeta del juego tiene que existir; la subcarpeta gamestate_integration de Dota la creamos.
                if (!Directory.Exists(Path.GetDirectoryName(dir)) && !Directory.Exists(dir)) continue;
                string target = Path.Combine(dir, CfgName);
                try
                {
                    if (!File.Exists(target) || File.ReadAllText(target) != content)
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(target, content);
                        Log.Write("GSI " + g.Name + ": config escrita en " + target);
                    }
                    written++;
                }
                catch (Exception e) { Log.Write("GSI " + g.Name + ": no se pudo escribir " + target + ": " + e.Message); }
            }
            if (written == 0) Log.Write("GSI " + g.Name + ": juego no encontrado en las bibliotecas de Steam");
            return written;
        }

        public bool Start()
        {
            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (Exception e)
            {
                Log.Write("GSI: no pude abrir el puerto " + port + ": " + e.Message);
                return false;
            }
            Thread t = new Thread(() => Serve(listener)) { IsBackground = true, Name = "gsi" };
            t.Start();
            Log.Write("GSI: escuchando en 127.0.0.1:" + port);
            return true;
        }

        void Serve(TcpListener listener)
        {
            while (true)
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = 3000;
                        NetworkStream ns = client.GetStream();
                        string body = ReadHttpBody(ns);
                        byte[] ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        ns.Write(ok, 0, ok.Length);
                        if (body != null) Handle(body);
                    }
                }
                catch (Exception e) { Log.Write("GSI: error en request: " + e.Message); }
            }
        }

        static string ReadHttpBody(NetworkStream ns)
        {
            MemoryStream buf = new MemoryStream();
            byte[] chunk = new byte[8192];
            int headerEnd = -1, contentLength = 0;
            while (true)
            {
                int n = ns.Read(chunk, 0, chunk.Length);
                if (n <= 0) return null;
                buf.Write(chunk, 0, n);
                if (headerEnd < 0)
                {
                    string soFar = Encoding.ASCII.GetString(buf.GetBuffer(), 0, (int)buf.Length);
                    int i = soFar.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (i < 0) continue;
                    headerEnd = i + 4;
                    Match m = Regex.Match(soFar.Substring(0, i), @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (m.Success) contentLength = int.Parse(m.Groups[1].Value);
                }
                if (buf.Length - headerEnd >= contentLength)
                    return Encoding.UTF8.GetString(buf.GetBuffer(), headerEnd, contentLength);
            }
        }

        void Handle(string json)
        {
            Match app = Regex.Match(json, "\"appid\"\\s*:\\s*(\\d+)");
            GameProfile g = app.Success ? GameProfile.ByAppId(int.Parse(app.Groups[1].Value)) : null;
            if (g == null) return;

            // En el menu el juego no manda la seccion "map"; dentro de una partida trae la fase.
            Match m = Regex.Match(json, "\"" + g.StateKey + "\"\\s*:\\s*\"(\\w+)\"");
            string phase = m.Success ? m.Groups[1].Value : null;

            string old;
            lock (gate)
            {
                GameState s;
                if (!states.TryGetValue(g.AppId, out s)) states[g.AppId] = s = new GameState();
                s.LastUpdate = DateTime.Now;
                if (phase == s.Phase) return;
                old = s.Phase;
                s.Phase = phase;
            }
            Log.Write("GSI " + g.Name + ": " + (old ?? "menu") + " -> " + (phase ?? "menu"));
            Action<GameProfile, string, string> h = StateChanged;
            if (h != null) h(g, old, phase);
        }
    }
}
