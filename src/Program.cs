using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("AutoReady")]
[assembly: System.Reflection.AssemblyProduct("AutoReady - match auto-accept")]
[assembly: System.Reflection.AssemblyCompany("keis.studio")]
[assembly: System.Reflection.AssemblyCopyright("© 2026 keis.studio")]
// La version la genera build.bat en obj\Version.cs (en los releases sale del tag de git).

namespace AutoReady
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Coordenadas en pixeles fisicos; si no, con escala 125%/150% el click cae fuera del boton.
            Win32.SetProcessDPIAware();

            // Un error inesperado se registra en vez de cerrar la app sin explicacion.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Write("Error no manejado (UI): " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Error no manejado: " + e.ExceptionObject);

            // AutoReady.exe --test captura.png [dota2|cs2]  -> corre el detector sobre una imagen y guarda captura.result.png
            // Codigo de salida: 0 = boton encontrado, 1 = no encontrado, 2 = error.
            if (args.Length >= 2 && args[0] == "--test")
                return TestImage(args[1], GameProfile.ById(args.Length > 2 ? args[2] : "dota2") ?? GameProfile.Dota2);

            // Despues de actualizar: esperamos a que cierre la version vieja (que tiene el mutex).
            int oldPid;
            if (args.Length == 2 && args[0] == "--after-update" && int.TryParse(args[1], out oldPid))
            {
                try { using (Process old = Process.GetProcessById(oldPid)) old.WaitForExit(15000); }
                catch (ArgumentException) { }   // ya cerro
            }
            Updater.CleanupOldExe();

            // Desinstalar (lo llama Configuracion > Aplicaciones) o, si es un exe recien bajado, instalarlo. Tanto si se
            // instala (arranca la copia instalada) como si se cancela, este exe termina aca.
            if (args.Length >= 1 && args[0] == "--uninstall") return Installer.Uninstall();
            if (Installer.ShouldOffer(args))
            {
                Installer.Offer();
                return 0;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\AutoReady", out created))
            {
                if (!created)
                {
                    MessageBox.Show(Lang.T("AutoReady ya está abierto: buscá su ícono al lado del reloj.",
                                           "AutoReady is already running: look for its icon next to the clock."), "AutoReady");
                    return 0;
                }
                if (Installer.IsInstalledCopy) Installer.Register();   // la version mostrada en Aplicaciones se actualiza
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
            return 0;
        }

        static int TestImage(string path, GameProfile game)
        {
            try
            {
                Detector.Settings ds = Config.Load().For(game).Detector;
                using (Bitmap img = new Bitmap(path))
                {
                    Detector.LoadTemplates();
                    Detector.Hit hit = Detector.FindButton(img, game, ds);
                    string outPath = Path.ChangeExtension(path, ".result.png");
                    using (Bitmap annotated = Detector.Annotate(img, hit, ds))
                        annotated.Save(outPath);
                    File.WriteAllText(Path.ChangeExtension(path, ".result.txt"),
                        hit == null ? "no encontrado" : "encontrado " + hit.Box + " centro " + hit.Center);
                    return hit == null ? 1 : 0;
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.ChangeExtension(path, ".result.txt"), "error: " + e);
                return 2;
            }
        }
    }
}
