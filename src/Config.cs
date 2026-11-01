using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ReadyUp
{
    // Configuracion en %APPDATA%\ReadyUp\readyup.ini (clave=valor). Se reescribe con todas las claves al guardar.
    class Config
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReadyUp");
        static readonly string FilePath = Path.Combine(Dir, "readyup.ini");

        public int ScanIntervalMs = 700;
        public int CooldownAfterAcceptSec = 10;
        public int GsiPort = 53111;
        public int FallbackScanSecAfterAccept = 90;   // sin GSI: cuanto seguimos escaneando despues de aceptar
        public bool NotifyPhone = false;
        public string NtfyServer = "https://ntfy.sh";
        public string NtfyTopic = "";
        public bool NotifyPickStarted = true;
        public bool Sound = true;
        public bool AutoUpdateCheck = true;

        public Detector.Settings Detector = new Detector.Settings();

        public static Config Load()
        {
            Config c = new Config();
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
                c.NotifyPickStarted = Bool(kv, "NotifyPickStarted", c.NotifyPickStarted);
                c.Sound = Bool(kv, "Sound", c.Sound);
                c.AutoUpdateCheck = Bool(kv, "AutoUpdateCheck", c.AutoUpdateCheck);

                Detector.Settings d = c.Detector;
                d.RoiX0 = Dbl(kv, "RoiX0", d.RoiX0); d.RoiY0 = Dbl(kv, "RoiY0", d.RoiY0);
                d.RoiX1 = Dbl(kv, "RoiX1", d.RoiX1); d.RoiY1 = Dbl(kv, "RoiY1", d.RoiY1);
                d.HueMin = Dbl(kv, "HueMin", d.HueMin); d.HueMax = Dbl(kv, "HueMax", d.HueMax);
                d.SatMin = Dbl(kv, "SatMin", d.SatMin); d.ValMin = Dbl(kv, "ValMin", d.ValMin);
                d.MinWidthFrac = Dbl(kv, "MinWidthFrac", d.MinWidthFrac); d.MaxWidthFrac = Dbl(kv, "MaxWidthFrac", d.MaxWidthFrac);
                d.MinHeightFrac = Dbl(kv, "MinHeightFrac", d.MinHeightFrac); d.MaxHeightFrac = Dbl(kv, "MaxHeightFrac", d.MaxHeightFrac);
                d.MinAspect = Dbl(kv, "MinAspect", d.MinAspect); d.MaxAspect = Dbl(kv, "MaxAspect", d.MaxAspect);
                d.MinFill = Dbl(kv, "MinFill", d.MinFill);
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
            Detector.Settings d = Detector;
            List<string> lines = new List<string>();
            lines.Add("; ReadyUp - configuracion. Reinicia ReadyUp despues de editar a mano.");
            lines.Add("ScanIntervalMs=" + ScanIntervalMs);
            lines.Add("CooldownAfterAcceptSec=" + CooldownAfterAcceptSec);
            lines.Add("GsiPort=" + GsiPort);
            lines.Add("FallbackScanSecAfterAccept=" + FallbackScanSecAfterAccept);
            lines.Add("NotifyPhone=" + NotifyPhone);
            lines.Add("NtfyServer=" + NtfyServer);
            lines.Add("NtfyTopic=" + NtfyTopic);
            lines.Add("NotifyPickStarted=" + NotifyPickStarted);
            lines.Add("Sound=" + Sound);
            lines.Add("AutoUpdateCheck=" + AutoUpdateCheck);
            lines.Add("");
            lines.Add("; Detector: area de busqueda (fracciones de la ventana), color en HSV (H en grados, S/V 0..1) y forma del boton");
            lines.Add("RoiX0=" + F(d.RoiX0)); lines.Add("RoiY0=" + F(d.RoiY0));
            lines.Add("RoiX1=" + F(d.RoiX1)); lines.Add("RoiY1=" + F(d.RoiY1));
            lines.Add("HueMin=" + F(d.HueMin)); lines.Add("HueMax=" + F(d.HueMax));
            lines.Add("SatMin=" + F(d.SatMin)); lines.Add("ValMin=" + F(d.ValMin));
            lines.Add("MinWidthFrac=" + F(d.MinWidthFrac)); lines.Add("MaxWidthFrac=" + F(d.MaxWidthFrac));
            lines.Add("MinHeightFrac=" + F(d.MinHeightFrac)); lines.Add("MaxHeightFrac=" + F(d.MaxHeightFrac));
            lines.Add("MinAspect=" + F(d.MinAspect)); lines.Add("MaxAspect=" + F(d.MaxAspect));
            lines.Add("MinFill=" + F(d.MinFill));
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
