using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace AutoReady
{
    // Lee (nunca escribe) la configuracion de Steam de la cuenta conectada, para saber si un juego ya tiene una
    // opcion de lanzamiento (ej. -gamestateintegration en Dota). Escribirla no es seguro: Steam pisa el archivo si
    // esta abierto, y en una PC puede haber cuentas de otras personas. Para agregarla se abre la ventana de
    // propiedades del juego en Steam y el usuario pega el texto.
    static class SteamConfig
    {
        // Se vuelve a leer solo si el archivo cambio (pesa unos 300 KB y el estado se consulta seguido).
        static readonly Dictionary<int, KeyValuePair<DateTime, string>> cache = new Dictionary<int, KeyValuePair<DateTime, string>>();

        public static bool HasOption(int appId, string option)
        {
            string lo = LaunchOptions(appId);
            return lo != null && (" " + lo + " ").IndexOf(" " + option + " ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Opciones de lanzamiento del juego en la cuenta conectada; null si no se pudo saber.
        public static string LaunchOptions(int appId)
        {
            try
            {
                string file = ConfigFile();
                if (file == null || !File.Exists(file)) return null;
                DateTime stamp = File.GetLastWriteTimeUtc(file);
                lock (cache)
                {
                    KeyValuePair<DateTime, string> hit;
                    if (cache.TryGetValue(appId, out hit) && hit.Key == stamp) return hit.Value;
                }
                string text;
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader r = new StreamReader(fs, Encoding.UTF8))
                    text = r.ReadToEnd();
                string lo = LaunchOptionsFrom(text, appId);
                lock (cache) cache[appId] = new KeyValuePair<DateTime, string>(stamp, lo);
                return lo;
            }
            catch (Exception e)
            {
                Log.Write("SteamConfig: no pude leer las opciones de lanzamiento: " + e.Message);
                return null;
            }
        }

        // Opciones de lanzamiento de un juego dentro del texto de localconfig.vdf ("" si no tiene). Separado para probarlo.
        internal static string LaunchOptionsFrom(string vdf, int appId)
        {
            string[] path = { "UserLocalConfigStore", "Software", "Valve", "Steam", "apps", appId.ToString() };
            return Find(Tokens(vdf), path, "LaunchOptions") ?? "";
        }

        // Abre la ventana de propiedades del juego en Steam (donde estan las opciones de lanzamiento).
        public static void OpenProperties(int appId)
        {
            Process.Start("steam://gameproperties/" + appId);
        }

        // userdata\<cuenta conectada>\config\localconfig.vdf
        static string ConfigFile()
        {
            string steam;
            int user;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                steam = k == null ? null : k.GetValue("SteamPath") as string;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                user = k == null ? 0 : Convert.ToInt32(k.GetValue("ActiveUser", 0));
            if (steam == null || user == 0) return null;
            return Path.Combine(steam, @"userdata\" + user + @"\config\localconfig.vdf");
        }

        // VDF de Valve: "clave" "valor" o "clave" { ... }. Tokens: strings entre comillas, "{" y "}".
        static List<string> Tokens(string s)
        {
            List<string> t = new List<string>();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '{' || c == '}') t.Add(c.ToString());
                else if (c == '"')
                {
                    StringBuilder sb = new StringBuilder();
                    for (i++; i < s.Length && s[i] != '"'; i++)
                    {
                        if (s[i] == '\\' && i + 1 < s.Length) i++;
                        sb.Append(s[i]);
                    }
                    t.Add("\"" + sb);   // el prefijo " distingue strings de llaves
                }
            }
            return t;
        }

        // Recorre la ruta de bloques (sin distinguir mayusculas) y devuelve el valor de la clave final.
        static string Find(List<string> t, string[] path, string key)
        {
            int depth = 0, matched = 0;
            for (int i = 0; i < t.Count; i++)
            {
                if (t[i] == "{") { depth++; continue; }
                if (t[i] == "}")
                {
                    depth--;
                    if (depth < matched) matched = depth;   // salimos de un bloque de la ruta
                    continue;
                }
                string name = t[i].Substring(1);
                bool isBlock = i + 1 < t.Count && t[i + 1] == "{";
                if (isBlock)
                {
                    if (depth == matched && matched < path.Length && string.Equals(name, path[matched], StringComparison.OrdinalIgnoreCase))
                        matched++;
                    continue;   // la llave se cuenta en la proxima vuelta
                }
                if (i + 1 < t.Count && t[i + 1].StartsWith("\""))   // par clave/valor
                {
                    if (matched == path.Length && depth == matched && string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                        return t[i + 1].Substring(1);
                    i++;
                }
            }
            return null;
        }
    }
}
