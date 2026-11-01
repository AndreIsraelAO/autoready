using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ReadyUp
{
    // Configuracion en %APPDATA%\ReadyUp\readyup.ini (clave=valor). Se reescribe con todas las claves al guardar.
    // Lo de cada juego va con prefijo: "dota2.Enabled", "cs2.HueMin", etc.
    class Config
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReadyUp");
        static readonly string FilePath = Path.Combine(Dir, "readyup.ini");

        public class GameSettings
        {
            public bool Enabled = true;
            public Detector.Settings Detector;
        }

        public int ScanIntervalMs = 700;
        public int CooldownAfterAcceptSec = 10;
        public int GsiPort = 53111;
        public int FallbackScanSecAfterAccept = 90;   // sin GSI: cuanto seguimos escaneando despues de aceptar
        public bool NotifyPhone = false;
        public string NtfyServer = "https://ntfy.sh";
        public string NtfyTopic = "";
        public bool NotifyMatchStarted = true;
        public bool Sound = true;
        public bool AutoUpdateCheck = true;

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
                c.FallbackScanSecAfterAccept = Int(kv, "FallbackScanSecAfterAccept", c.FallbackScanSecAfterAccept);
                c.NotifyPhone = Bool(kv, "NotifyPhone", c.NotifyPhone);
                c.NtfyServer = Str(kv, "NtfyServer", c.NtfyServer);
                c.NtfyTopic = Str(kv, "NtfyTopic", c.NtfyTopic);
                c.NotifyMatchStarted = Bool(kv, "NotifyMatchStarted", c.NotifyMatchStarted);
                c.Sound = Bool(kv, "Sound", c.Sound);
                c.AutoUpdateCheck = Bool(kv, "AutoUpdateCheck", c.AutoUpdateCheck);

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
            if (string.IsNullOrEmpty(c.NtfyTopic))
            {
                // Los topics de ntfy.sh son publicos: quien sabe el nombre lo lee. Por eso uno aleatorio por usuario.
                c.NtfyTopic = "readyup-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            }
            c.Save();
            return c;
        }

        public void Save()
        {
            List<string> lines = new List<string>();
            lines.Add("; ReadyUp - configuracion. Reinicia ReadyUp despues de editar a mano.");
            lines.Add("ScanIntervalMs=" + ScanIntervalMs);
            lines.Add("CooldownAfterAcceptSec=" + CooldownAfterAcceptSec);
            lines.Add("GsiPort=" + GsiPort);
            lines.Add("FallbackScanSecAfterAccept=" + FallbackScanSecAfterAccept);
            lines.Add("NotifyPhone=" + NotifyPhone);
            lines.Add("NtfyServer=" + NtfyServer);
            lines.Add("NtfyTopic=" + NtfyTopic);
            lines.Add("NotifyMatchStarted=" + NotifyMatchStarted);
            lines.Add("Sound=" + Sound);
            lines.Add("AutoUpdateCheck=" + AutoUpdateCheck);
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
        public static readonly string FilePath = Path.Combine(Config.Dir, "readyup.log");

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
