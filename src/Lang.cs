using System;
using System.Globalization;
using System.IO;

namespace AutoReady
{
    // Idioma de la interfaz: espanol si Windows esta en espanol, ingles para todo lo demas.
    // Se puede forzar con Language=es o Language=en en autoready.ini. Los textos estan en Strings.cs.
    static class Lang
    {
        public static bool Spanish = Detect();   // no readonly: las pruebas revisan los textos en los dos idiomas

        public static string T(string es, string en) { return Spanish ? es : en; }

        static bool Detect()
        {
            try
            {
                string ini = Path.Combine(Config.Dir, "autoready.ini");
                if (File.Exists(ini))
                    foreach (string line in File.ReadAllLines(ini))
                    {
                        string l = line.Trim();
                        if (l.StartsWith("Language=", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = l.Substring(9).Trim().ToLowerInvariant();
                            if (v == "es") return true;
                            if (v == "en") return false;
                        }
                    }
            }
            catch { }
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
        }
    }
}
