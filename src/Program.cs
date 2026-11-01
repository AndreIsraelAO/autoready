using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("ReadyUp")]
[assembly: System.Reflection.AssemblyProduct("ReadyUp - auto-accept for Dota 2")]
[assembly: System.Reflection.AssemblyVersion("0.1.0.0")]

namespace ReadyUp
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Coordenadas en pixeles fisicos; si no, con escala 125%/150% el click cae fuera del boton.
            Win32.SetProcessDPIAware();

            // ReadyUp.exe --test captura.png  -> corre el detector sobre una imagen y guarda captura.result.png
            // Codigo de salida: 0 = boton encontrado, 1 = no encontrado, 2 = error.
            if (args.Length == 2 && args[0] == "--test") return TestImage(args[1]);

            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\ReadyUp", out created))
            {
                if (!created)
                {
                    MessageBox.Show("ReadyUp ya esta abierto (mira el icono al lado del reloj).", "ReadyUp");
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
            return 0;
        }

        static int TestImage(string path)
        {
            try
            {
                Config cfg = Config.Load();
                using (Bitmap img = new Bitmap(path))
                {
                    Detector.Hit hit = Detector.Find(img, cfg.Detector);
                    string outPath = Path.ChangeExtension(path, ".result.png");
                    using (Bitmap annotated = Detector.Annotate(img, hit, cfg.Detector))
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
