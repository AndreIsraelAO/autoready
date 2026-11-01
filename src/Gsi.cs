using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ReadyUp
{
    // Dota 2 Game State Integration: la forma oficial de Valve de saber en que estado esta el juego.
    // Dota manda POSTs con JSON a un servidor local. Requiere la opcion de lanzamiento -gamestateintegration.
    // La usamos solo para saber si estas dentro de una partida (y ahi no escanear nada).
    class Gsi
    {
        const string CfgName = "gamestate_integration_readyup.cfg";

        readonly int port;
        DateTime lastUpdate = DateTime.MinValue;
        string gameState;   // null = menu principal; si no, DOTA_GAMERULES_STATE_*

        public event Action<string, string> StateChanged;   // (anterior, nuevo)

        public Gsi(int port) { this.port = port; }

        public bool Connected { get { return (DateTime.Now - lastUpdate).TotalSeconds < 30; } }
        public bool InMatch { get { return Connected && gameState != null; } }
        public string GameState { get { return gameState; } }

        public static List<string> FindDotaDirs()
        {
            List<string> libs = new List<string>();
            string steam = null;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (k != null) steam = k.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(steam)) steam = @"C:\Program Files (x86)\Steam";
            libs.Add(steam);

            string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

            List<string> dirs = new List<string>();
            foreach (string lib in libs)
            {
                string d = Path.GetFullPath(Path.Combine(lib, @"steamapps\common\dota 2 beta\game\dota"));
                if (Directory.Exists(d) && !dirs.Exists(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase)))
                    dirs.Add(d);
            }
            return dirs;
        }

        // Escribe el .cfg de GSI en cada instalacion de Dota encontrada. Devuelve la cantidad escrita.
        public int InstallConfig()
        {
            string content =
                "\"ReadyUp\"\r\n{\r\n" +
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
            foreach (string dir in FindDotaDirs())
            {
                string target = Path.Combine(dir, @"cfg\gamestate_integration\" + CfgName);
                try
                {
                    if (!File.Exists(target) || File.ReadAllText(target) != content)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.WriteAllText(target, content);
                        Log.Write("GSI: config escrita en " + target);
                    }
                    written++;
                }
                catch (Exception e) { Log.Write("GSI: no se pudo escribir " + target + ": " + e.Message); }
            }
            if (written == 0) Log.Write("GSI: no encontre la carpeta de Dota 2");
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
                byte[] all = buf.GetBuffer();
                if (headerEnd < 0)
                {
                    string soFar = Encoding.ASCII.GetString(all, 0, (int)buf.Length);
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
            lastUpdate = DateTime.Now;
            // En el menu Dota no manda la seccion "map"; dentro de una partida trae "game_state".
            Match m = Regex.Match(json, "\"game_state\"\\s*:\\s*\"(\\w+)\"");
            string state = m.Success ? m.Groups[1].Value : null;
            if (state == gameState) return;
            string old = gameState;
            gameState = state;
            Log.Write("GSI: " + (old ?? "menu") + " -> " + (state ?? "menu"));
            Action<string, string> h = StateChanged;
            if (h != null) h(old, state);
        }
    }
}
