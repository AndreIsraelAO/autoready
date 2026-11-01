using System.Globalization;

namespace AutoReady
{
    // Idioma de la interfaz: espanol si Windows esta en espanol, ingles para todo lo demas.
    // Uso: Lang.T("Instalar", "Install"). Base para centralizar todos los textos (issue #7).
    static class Lang
    {
        public static readonly bool Spanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";

        public static string T(string es, string en) { return Spanish ? es : en; }
    }
}
