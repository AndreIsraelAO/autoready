using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace AutoReady
{
    // Estado de Steam (Rich Presence) de la cuenta conectada: "Finding A Match", "Hero Selection", "Main Menu"...
    // Es lo que Steam muestra a los amigos. Se lee de la mini-ficha publica del perfil (steamcommunity.com/miniprofile),
    // sin clave, sin login y sin configurar nada en el juego. Requiere que los detalles de juego del perfil sean
    // visibles; si no, el estado queda Unknown y AutoReady sigue con GSI o el modo seguro.
    //
    // Se usa en los juegos cuyo perfil trae textos de estado (GameProfile.PresenceFinding/PresenceMenu); la pagina se
    // pide en ingles para no depender del idioma del usuario.
    enum PresenceState { Unknown, Menu, Finding, InMatch }

    static class Presence
    {
        static readonly TimeSpan Every = TimeSpan.FromSeconds(6);
        static readonly TimeSpan Stale = TimeSpan.FromSeconds(30);

        static string lastText;
        static DateTime lastRead = DateTime.MinValue;
        static DateTime lastWorking = DateTime.MinValue;   // ultima vez que se pudo leer un estado de juego
        static Thread worker;
        static volatile GameProfile game;   // juego abierto que usa el estado de Steam (null = no consultar)

        public static event Action<PresenceState, PresenceState, string> Changed;   // (anterior, nuevo, texto)
        static PresenceState current = PresenceState.Unknown;

        public static PresenceState Current
        {
            get { return DateTime.Now - lastRead > Stale ? PresenceState.Unknown : current; }
        }

        public static string Text { get { return lastText; } }

        // El estado se pudo leer hace poco: la proteccion por estado de Steam esta funcionando.
        public static bool Working { get { return DateTime.Now - lastWorking < TimeSpan.FromMinutes(10); } }

        // Se consulta solo mientras un juego que lo usa esta abierto.
        public static void Watch(GameProfile g)
        {
            GameProfile use = g != null && g.UsesPresence ? g : null;
            if (use != game) { current = PresenceState.Unknown; lastText = null; }
            game = use;
            if (use == null || worker != null) return;
            worker = new Thread(Loop) { IsBackground = true, Name = "presence" };
            worker.Start();
        }

        static void Loop()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            while (true)
            {
                GameProfile g = game;
                if (g != null)
                {
                    try { Update(Read(), g); }
                    catch (Exception e) { Log.Write("Presence: no pude leer el estado de Steam: " + e.Message); }
                }
                Thread.Sleep(Every);
            }
        }

        static void Update(string text, GameProfile g)
        {
            lastRead = DateTime.Now;
            PresenceState s = Classify(text, g);
            if (s != PresenceState.Unknown) lastWorking = DateTime.Now;
            bool changed = s != current || text != lastText;
            PresenceState old = current;
            current = s;
            lastText = text;
            if (!changed) return;
            Log.Write("Presence: " + (text ?? "(sin estado de juego)") + " -> " + s);
            Action<PresenceState, PresenceState, string> h = Changed;
            if (h != null && old != s) h(old, s, text);
        }

        public static PresenceState Classify(string text, GameProfile g)
        {
            if (string.IsNullOrEmpty(text) || g == null) return PresenceState.Unknown;
            foreach (string f in g.PresenceFinding)
                if (text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return PresenceState.Finding;
            foreach (string m in g.PresenceMenu)
                if (text.StartsWith(m, StringComparison.OrdinalIgnoreCase)) return PresenceState.Menu;
            // Cualquier otro estado del juego (seleccion de heroe, partida, post-partida, espectando, bots...) se
            // trata como "ocupado": ahi no puede aparecer el popup de aceptar.
            return PresenceState.InMatch;
        }

        // Devuelve el texto de Rich Presence de la mini-ficha, o null si no esta en un juego o no es visible.
        static string Read()
        {
            int account;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                account = k == null ? 0 : Convert.ToInt32(k.GetValue("ActiveUser", 0));
            if (account == 0) return null;

            string html;
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "AutoReady/" + Updater.CurrentVersionText;
                wc.Headers[HttpRequestHeader.Cookie] = "Steam_Language=english";
                wc.Headers[HttpRequestHeader.AcceptLanguage] = "en-US,en";
                wc.Encoding = System.Text.Encoding.UTF8;
                html = wc.DownloadString("https://steamcommunity.com/miniprofile/" + account + "?l=english");
            }
            Match m = Regex.Match(html, "class=\"[^\"]*rich_presence[^\"]*\"[^>]*>\\s*([^<]+?)\\s*<", RegexOptions.IgnoreCase);
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }
    }
}
