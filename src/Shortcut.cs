using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace AutoReady
{
    // Accesos directos (menu Inicio y escritorio) con WScript.Shell, que viene con Windows.
    static class Shortcut
    {
        public static string StartMenuPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "AutoReady.lnk"); } }
        public static string DesktopPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AutoReady.lnk"); } }

        // El de Inicio se mantiene apuntando al exe instalado (por si se borro); asi AutoReady aparece al buscarlo.
        public static void EnsureStartMenu()
        {
            try
            {
                if (string.Equals(Target(StartMenuPath), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return;
                Create(StartMenuPath, Application.ExecutablePath);
                Log.Write("Acceso directo del menu Inicio: " + StartMenuPath);
            }
            catch (Exception e) { Log.Write("No pude crear el acceso directo del menu Inicio: " + e.Message); }
        }

        public static void Create(string lnk, string exe)
        {
            object sc = Load(lnk);
            Type t = sc.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, sc,
                           new object[] { Lang.T("Acepta tus partidas automáticamente", "Accepts your matches automatically") });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        // Ruta a la que apunta un acceso directo, o null si no existe.
        public static string Target(string lnk)
        {
            if (!File.Exists(lnk)) return null;
            object sc = Load(lnk);
            return (string)sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
        }

        static object Load(string lnk)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            return shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        }
    }
}
