using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace AutoReady
{
    // Acceso directo en el menu Inicio para que AutoReady aparezca al buscarlo en Windows.
    // Como es un exe suelto (sin instalador), lo crea la propia app y lo corrige si el exe cambio de lugar.
    static class Shortcut
    {
        public static void EnsureStartMenu()
        {
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string lnk = Path.Combine(dir, "AutoReady.lnk");
                string exe = Application.ExecutablePath;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type t = sc.GetType();
                string current = (string)t.InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
                if (File.Exists(lnk) && string.Equals(current, exe, StringComparison.OrdinalIgnoreCase)) return;

                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Acepta tus partidas automáticamente" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Log.Write("Acceso directo del menu Inicio: " + lnk);
            }
            catch (Exception e) { Log.Write("No pude crear el acceso directo del menu Inicio: " + e.Message); }
        }
    }
}
