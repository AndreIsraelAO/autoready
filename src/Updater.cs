using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AutoReady
{
    // Busca versiones nuevas en los Releases de GitHub y se reemplaza a si mismo.
    static class Updater
    {
        // La lista incluye los pre-releases (las 0.x lo son); /releases/latest no. Viene ordenada del mas nuevo al mas viejo.
        const string ReleasesApi = "https://api.github.com/repos/hercase/autoready/releases?per_page=5";

        public class Release
        {
            public Version Version;
            public string Tag, ExeUrl, ShaUrl;
        }

        public static string CurrentVersionText
        {
            get
            {
                object[] a = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                return a.Length > 0 ? ((AssemblyInformationalVersionAttribute)a[0]).InformationalVersion : "0.0.0";
            }
        }

        static Version Current { get { return new Version(CurrentVersionText); } }

        // Las builds locales (build.bat sin version) son 0.0.0 y no se actualizan solas.
        public static bool IsDevBuild { get { return Current == new Version(0, 0, 0); } }

        static Updater()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
        }

        static WebClient Client()
        {
            WebClient wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "AutoReady/" + CurrentVersionText;
            return wc;
        }

        // Devuelve el release si es mas nuevo que esta version; si no, null.
        public static Release CheckForUpdate()
        {
            string json;
            using (WebClient wc = Client()) json = wc.DownloadString(ReleasesApi);

            Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
            if (!tag.Success) return null;
            // Los assets tienen que ser de ese mismo tag (si el release mas nuevo quedo sin exe, no tomamos el de otro).
            string dl = "[^\"]+/download/v" + Regex.Escape(tag.Groups[1].Value) + "/";
            Match exe = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"(" + dl + "AutoReady\\.exe)\"");
            Match sha = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"(" + dl + "AutoReady\\.exe\\.sha256)\"");
            if (!exe.Success || !sha.Success) return null;

            Version v = new Version(tag.Groups[1].Value);
            if (v <= Current) return null;
            return new Release { Version = v, Tag = "v" + tag.Groups[1].Value, ExeUrl = exe.Groups[1].Value, ShaUrl = sha.Groups[1].Value };
        }

        // Baja el exe nuevo, verifica el SHA-256 y lo pone en lugar del actual. Windows deja renombrar un exe
        // en ejecucion, asi que el actual pasa a AutoReady.old.exe y se borra en el proximo arranque.
        // Devuelve la ruta del exe nuevo listo para lanzar.
        public static string DownloadAndInstall(Release r)
        {
            string exePath = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exePath);
            string newPath = Path.Combine(dir, "AutoReady.new.exe");
            string oldPath = Path.Combine(dir, "AutoReady.old.exe");

            string expected;
            using (WebClient wc = Client())
            {
                expected = wc.DownloadString(r.ShaUrl).Trim().Split(' ')[0].ToLowerInvariant();
                wc.DownloadFile(r.ExeUrl, newPath);
            }

            string actual;
            using (FileStream fs = File.OpenRead(newPath))
            using (SHA256 sha = SHA256.Create())
                actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (actual != expected)
            {
                File.Delete(newPath);
                throw new Exception("el SHA-256 del exe descargado no coincide; no se instalo nada");
            }

            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exePath, oldPath);
            try { File.Move(newPath, exePath); }
            catch
            {
                File.Move(oldPath, exePath);   // volvemos a dejar todo como estaba
                throw;
            }
            Log.Write("Actualizado a " + r.Tag);
            return exePath;
        }

        public static void CleanupOldExe()
        {
            try
            {
                string old = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "AutoReady.old.exe");
                if (File.Exists(old)) File.Delete(old);
            }
            catch { }
        }

        // Lanza la version nueva; esta espera a que cierre la vieja antes de arrancar.
        public static void Relaunch(string exePath)
        {
            Process.Start(exePath, "--after-update " + Process.GetCurrentProcess().Id);
        }
    }
}
