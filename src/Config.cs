using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AutoReady
{
    // Configuracion en %APPDATA%\AutoReady\autoready.ini (clave=valor). Se reescribe con todas las claves al guardar.
    // Lo de cada juego va con prefijo: "dota2.Enabled", "cs2.HueMin", etc.
    class Config
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoReady");
        static readonly string FilePath = Path.Combine(Dir, "autoready.ini");

        public class GameSettings
        {
            public bool Enabled = true;
            public Detector.Settings Detector;
        }

        public int ScanIntervalMs = 700;
        public int CooldownAfterAcceptSec = 10;
        public int GsiPort = 53111;
        public string GsiToken = "";   // codigo secreto que el juego manda en cada POST de GSI
        public int FallbackScanSecAfterAccept = 90;   // sin GSI: cuanto seguimos escaneando despues de aceptar
        public bool NotifyPhone = false;
        public bool PhoneTested = false;   // la prueba llego una vez: el QR queda colapsado
        public string NtfyServer = "https://ntfy.sh";
        public string NtfyTopic = "";
        public bool NotifyMatchStarted = true;
        public bool Sound = true;
        public bool AutoUpdateCheck = true;
        public bool UseSteamStatus = true;   // leer el estado de Steam (ver Presence.cs y PRIVACY.md)
        public int HotkeyVk = 0;       // 0 = sin atajo
        public int HotkeyMods = 0;     // 1 Alt, 2 Ctrl, 4 Shift
        public string Language = "auto";   // auto (idioma de Windows), es o en; lo lee Lang al arrancar
        public bool Welcomed = false;  // la primera vez se abre el panel solo
        public bool SaveSamples = true; // beta: guarda capturas reales para ajustar el detector (ver Samples.cs)

        public readonly Dictionary<string, GameSettings> Games = new Dictionary<string, GameSettings>();

        public GameSettings For(GameProfile g) { return Games[g.Id]; }

        public static Config Load()
        {
            Config c = new Config();
            foreach (GameProfile g in GameProfile.All)
                c.Games[g.Id] = new GameSettings { Detector = g.DefaultDetector() };

            Directory.CreateDirectory(Dir);
            if (File.Exists(FilePath))
            {
                Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0 && !line.TrimStart().StartsWith(";")) kv[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                c.ScanIntervalMs = Int(kv, "ScanIntervalMs", c.ScanIntervalMs);
                c.CooldownAfterAcceptSec = Int(kv, "CooldownAfterAcceptSec", c.CooldownAfterAcceptSec);
                c.GsiPort = Int(kv, "GsiPort", c.GsiPort);
                c.GsiToken = Str(kv, "GsiToken", c.GsiToken);
                c.FallbackScanSecAfterAccept = Int(kv, "FallbackScanSecAfterAccept", c.FallbackScanSecAfterAccept);
                c.NotifyPhone = Bool(kv, "NotifyPhone", c.NotifyPhone);
                c.PhoneTested = Bool(kv, "PhoneTested", c.PhoneTested);
                c.NtfyServer = Str(kv, "NtfyServer", c.NtfyServer);
                c.NtfyTopic = Str(kv, "NtfyTopic", c.NtfyTopic);
                c.NotifyMatchStarted = Bool(kv, "NotifyMatchStarted", c.NotifyMatchStarted);
                c.Sound = Bool(kv, "Sound", c.Sound);
                c.AutoUpdateCheck = Bool(kv, "AutoUpdateCheck", c.AutoUpdateCheck);
                c.UseSteamStatus = Bool(kv, "UseSteamStatus", c.UseSteamStatus);
                c.HotkeyVk = Int(kv, "HotkeyVk", c.HotkeyVk);
                c.HotkeyMods = Int(kv, "HotkeyMods", c.HotkeyMods);
                c.Language = Str(kv, "Language", c.Language);
                c.Welcomed = Bool(kv, "Welcomed", c.Welcomed);
                c.SaveSamples = Bool(kv, "SaveSamples", c.SaveSamples);

                foreach (GameProfile g in GameProfile.All)
                {
                    string p = g.Id + ".";
                    GameSettings gs = c.Games[g.Id];
                    Detector.Settings d = gs.Detector;
                    gs.Enabled = Bool(kv, p + "Enabled", gs.Enabled);
                    d.RoiX0 = Dbl(kv, p + "RoiX0", d.RoiX0); d.RoiY0 = Dbl(kv, p + "RoiY0", d.RoiY0);
                    d.RoiX1 = Dbl(kv, p + "RoiX1", d.RoiX1); d.RoiY1 = Dbl(kv, p + "RoiY1", d.RoiY1);
                    d.HueMin = Dbl(kv, p + "HueMin", d.HueMin); d.HueMax = Dbl(kv, p + "HueMax", d.HueMax);
                    d.SatMin = Dbl(kv, p + "SatMin", d.SatMin); d.ValMin = Dbl(kv, p + "ValMin", d.ValMin);
                    d.MinWidthFrac = Dbl(kv, p + "MinWidthFrac", d.MinWidthFrac); d.MaxWidthFrac = Dbl(kv, p + "MaxWidthFrac", d.MaxWidthFrac);
                    d.MinHeightFrac = Dbl(kv, p + "MinHeightFrac", d.MinHeightFrac); d.MaxHeightFrac = Dbl(kv, p + "MaxHeightFrac", d.MaxHeightFrac);
                    d.MinAspect = Dbl(kv, p + "MinAspect", d.MinAspect); d.MaxAspect = Dbl(kv, p + "MaxAspect", d.MaxAspect);
                    d.MinFill = Dbl(kv, p + "MinFill", d.MinFill);
                }
            }
            if (string.IsNullOrEmpty(c.GsiToken)) c.GsiToken = Guid.NewGuid().ToString("N");   // 128 bits aleatorios
            if (string.IsNullOrEmpty(c.NtfyTopic) || !c.NtfyTopic.StartsWith("ar-"))
                c.NtfyTopic = NewTopic();
            c.Save();
            return c;
        }

        // Los topics de ntfy.sh son publicos: quien sabe el nombre lo lee. Por eso uno aleatorio por usuario, lo mas corto
        // posible para escribirlo o pegarlo facil (ar-k7m2q9) y sin letras que se confunden (0/o, 1/l/i).
        // 6 caracteres de 31 = ~890 millones: aun con 100 mil usuarios, la chance de que a alguien le toque el canal de otro
        // es de ~0,01 %. Con 5 seria ~0,35 %. Los codigos viejos (ar- y 8 caracteres) siguen valiendo.
        static string NewTopic()
        {
            const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
            byte[] rnd = new byte[6];
            using (System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(rnd);
            char[] chars = new char[6];
            for (int i = 0; i < 6; i++) chars[i] = alphabet[rnd[i] % alphabet.Length];
            return "ar-" + new string(chars);
        }

        public void Save()
        {
            List<string> lines = new List<string>();
            lines.Add("; AutoReady - configuracion. Reinicia AutoReady despues de editar a mano.");
            lines.Add("ScanIntervalMs=" + ScanIntervalMs);
            lines.Add("CooldownAfterAcceptSec=" + CooldownAfterAcceptSec);
            lines.Add("GsiPort=" + GsiPort);
            lines.Add("GsiToken=" + GsiToken);
            lines.Add("FallbackScanSecAfterAccept=" + FallbackScanSecAfterAccept);
            lines.Add("NotifyPhone=" + NotifyPhone);
            lines.Add("PhoneTested=" + PhoneTested);
            lines.Add("NtfyServer=" + NtfyServer);
            lines.Add("NtfyTopic=" + NtfyTopic);
            lines.Add("NotifyMatchStarted=" + NotifyMatchStarted);
            lines.Add("Sound=" + Sound);
            lines.Add("AutoUpdateCheck=" + AutoUpdateCheck);
            lines.Add("UseSteamStatus=" + UseSteamStatus);
            lines.Add("HotkeyVk=" + HotkeyVk);
            lines.Add("HotkeyMods=" + HotkeyMods);
            lines.Add("Language=" + Language);
            lines.Add("Welcomed=" + Welcomed);
            lines.Add("SaveSamples=" + SaveSamples);
            foreach (GameProfile g in GameProfile.All)
            {
                string p = g.Id + ".";
                GameSettings gs = Games[g.Id];
                Detector.Settings d = gs.Detector;
                lines.Add("");
                lines.Add("; " + g.Name + ": area de busqueda (fracciones de la ventana), color en HSV (H en grados, S/V 0..1) y forma del boton");
                lines.Add(p + "Enabled=" + gs.Enabled);
                lines.Add(p + "RoiX0=" + F(d.RoiX0)); lines.Add(p + "RoiY0=" + F(d.RoiY0));
                lines.Add(p + "RoiX1=" + F(d.RoiX1)); lines.Add(p + "RoiY1=" + F(d.RoiY1));
                lines.Add(p + "HueMin=" + F(d.HueMin)); lines.Add(p + "HueMax=" + F(d.HueMax));
                lines.Add(p + "SatMin=" + F(d.SatMin)); lines.Add(p + "ValMin=" + F(d.ValMin));
                lines.Add(p + "MinWidthFrac=" + F(d.MinWidthFrac)); lines.Add(p + "MaxWidthFrac=" + F(d.MaxWidthFrac));
                lines.Add(p + "MinHeightFrac=" + F(d.MinHeightFrac)); lines.Add(p + "MaxHeightFrac=" + F(d.MaxHeightFrac));
                lines.Add(p + "MinAspect=" + F(d.MinAspect)); lines.Add(p + "MaxAspect=" + F(d.MaxAspect));
                lines.Add(p + "MinFill=" + F(d.MinFill));
            }
            File.WriteAllLines(FilePath, lines.ToArray());
        }

        public static string FileLocation { get { return FilePath; } }

        static string F(double v) { return v.ToString(CultureInfo.InvariantCulture); }
        static string Str(Dictionary<string, string> kv, string k, string def) { string v; return kv.TryGetValue(k, out v) ? v : def; }
        static int Int(Dictionary<string, string> kv, string k, int def) { string v; int r; return kv.TryGetValue(k, out v) && int.TryParse(v, out r) ? r : def; }
        static bool Bool(Dictionary<string, string> kv, string k, bool def) { string v; bool r; return kv.TryGetValue(k, out v) && bool.TryParse(v, out r) ? r : def; }
        static double Dbl(Dictionary<string, string> kv, string k, double def)
        {
            string v; double r;
            return kv.TryGetValue(k, out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
        }
    }

    static class Log
    {
        static readonly object Gate = new object();
        public static readonly string FilePath = Path.Combine(Config.Dir, "autoready.log");

        public static void Write(string msg)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Config.Dir);
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 1000000) File.Delete(FilePath);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
