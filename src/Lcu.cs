using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoReady
{
    // League of Legends: el cliente expone una API local (LCU) solo en 127.0.0.1, la misma que usan las herramientas de
    // la comunidad. Con ella se sabe la fase (buscando, partida encontrada, seleccion de campeones, en partida) y se
    // acepta la partida sin tocar el mouse ni traer la ventana al frente. No lee memoria ni toca archivos del juego.
    //
    // Credenciales: el archivo "lockfile" que el cliente escribe en su carpeta mientras esta abierto:
    //   LeagueClient:<pid>:<puerto>:<clave>:<protocolo>
    class Lcu
    {
        string baseUrl, auth, lockPath;
        DateTime lockTime;
        readonly Func<string> findLockfile;

        // findLockfile: de donde sale el lockfile (las pruebas usan uno falso); por defecto, el del cliente abierto.
        public Lcu(Func<string> findLockfile = null)
        {
            this.findLockfile = findLockfile ?? FindLockfile;
        }

        // Fase del cliente ("None", "Lobby", "Matchmaking", "ReadyCheck", "ChampSelect", "InProgress", ...) o null si no
        // se pudo leer (cliente cerrado o todavia arrancando).
        public string Phase()
        {
            string body = Request("GET", "/lol-gameflow/v1/gameflow-phase");
            return body == null ? null : body.Trim().Trim('"');
        }

        // Hay una partida encontrada que el jugador todavia no acepto ni rechazo.
        public bool ReadyCheckPending()
        {
            string body = Request("GET", "/lol-matchmaking/v1/ready-check");
            if (body == null) return false;
            return Regex.IsMatch(body, "\"state\"\\s*:\\s*\"InProgress\"") && Regex.IsMatch(body, "\"playerResponse\"\\s*:\\s*\"None\"");
        }

        public bool Accept()
        {
            return Request("POST", "/lol-matchmaking/v1/ready-check/accept") != null;
        }

        // Fases en las que no se hace nada: ya hay partida (o se esta armando) y el jugador tiene que estar.
        public static bool IsInMatch(string phase)
        {
            return phase == "ChampSelect" || phase == "GameStart" || phase == "InProgress" || phase == "Reconnect" ||
                   phase == "WaitingForStats" || phase == "PreEndOfGame" || phase == "EndOfGame";
        }

        string Request(string method, string path)
        {
            if (!LoadCredentials()) return null;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(baseUrl + path);
                req.Method = method;
                req.Timeout = 2000;
                req.ReadWriteTimeout = 2000;
                req.Accept = "application/json";
                req.Headers[HttpRequestHeader.Authorization] = auth;
                req.Proxy = null;   // es local: nunca pasar por un proxy del sistema
                // El certificado del cliente es autofirmado por Riot. Se acepta solo para esta conexion a 127.0.0.1.
                req.ServerCertificateValidationCallback = (s, cert, chain, errors) => true;
                if (method == "POST") { req.ContentLength = 0; }
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch (WebException e)
            {
                // 404 en ready-check = no hay partida encontrada: es normal, no se loguea.
                HttpWebResponse res = e.Response as HttpWebResponse;
                if (res == null) baseUrl = null;   // no responde: el cliente se cerro o cambio de puerto; se relee el lockfile
                return null;
            }
            catch (Exception) { return null; }
        }

        bool LoadCredentials()
        {
            string path = findLockfile();
            if (path == null) { baseUrl = null; return false; }
            DateTime t;
            try { t = File.GetLastWriteTimeUtc(path); } catch { return false; }
            if (baseUrl != null && path == lockPath && t == lockTime) return true;
            try
            {
                string text;
                // El cliente lo tiene abierto: se lee compartido.
                using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader r = new StreamReader(f)) text = r.ReadToEnd();
                string[] p = text.Trim().Split(':');
                if (p.Length < 5) return false;
                int port;
                if (!int.TryParse(p[2], out port) || port <= 0 || port > 65535) return false;
                string scheme = p[4] == "http" ? "http" : "https";
                baseUrl = scheme + "://127.0.0.1:" + port;
                auth = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + p[3]));
                lockPath = path; lockTime = t;
                return true;
            }
            catch (Exception) { baseUrl = null; return false; }
        }

        // El lockfile esta en la carpeta del cliente: se busca junto al proceso abierto y, si no se puede leer su ruta,
        // en la instalacion por defecto.
        static string FindLockfile()
        {
            foreach (Process p in Process.GetProcessesByName("LeagueClientUx"))
            {
                try
                {
                    string dir = Path.GetDirectoryName(p.MainModule.FileName);
                    string lf = Path.Combine(dir, "lockfile");
                    if (File.Exists(lf)) return lf;
                }
                catch { }
                finally { p.Dispose(); }
            }
            foreach (string dir in new[] { @"C:\Riot Games\League of Legends", @"D:\Riot Games\League of Legends" })
            {
                string lf = Path.Combine(dir, "lockfile");
                if (File.Exists(lf)) return lf;
            }
            return null;
        }
    }
}
