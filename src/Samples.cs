using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace AutoReady
{
    // Capturas reales para mejorar el detector (beta). Solo de la ventana del juego y solo en esta PC.
    // - Al aceptar: se guarda la imagen exacta con la que se decidio.
    // - Si una partida arranca sin que AutoReady la haya aceptado (fallo, o el jugador acepto antes): se guardan
    //   las imagenes del ultimo minuto y medio, donde tiene que aparecer el popup que no se detecto.
    static class Samples
    {
        public static readonly string Dir = Path.Combine(Config.Dir, "samples");
        const int KeepEntries = 40;                                   // capturas/carpetas que se conservan
        static readonly TimeSpan Every = TimeSpan.FromSeconds(3);
        static readonly TimeSpan Window = TimeSpan.FromSeconds(90);
        const int MaxWidth = 1280;                                    // las del historial van reducidas

        class Frame { public DateTime At; public Bitmap Image; }

        static readonly object Gate = new object();
        static readonly Queue<Frame> recent = new Queue<Frame>();
        static DateTime lastAdded = DateTime.MinValue;
        public static bool Enabled = true;

        // Llamado en cada escaneo mientras se espera partida. Guarda en memoria una version reducida cada 3 s.
        public static void Remember(Bitmap shot)
        {
            if (!Enabled || DateTime.Now - lastAdded < Every) return;
            lastAdded = DateTime.Now;
            Bitmap small = Shrink(shot);
            lock (Gate)
            {
                recent.Enqueue(new Frame { At = DateTime.Now, Image = small });
                while (recent.Count > 0 && DateTime.Now - recent.Peek().At > Window) recent.Dequeue().Image.Dispose();
            }
        }

        public static void Accepted(Bitmap shot, GameProfile game)
        {
            if (!Enabled) return;
            Bitmap copy = new Bitmap(shot);
            Forget();
            SaveAsync(() =>
            {
                using (copy) copy.Save(Path.Combine(Dir, Name("accepted", game) + ".png"), ImageFormat.Png);
            });
        }

        // GSI aviso que arranco una partida que AutoReady no acepto.
        public static void Missed(GameProfile game)
        {
            if (!Enabled) return;
            List<Frame> frames;
            lock (Gate)
            {
                frames = recent.ToList();
                recent.Clear();
            }
            if (frames.Count == 0) { Log.Write("Samples: partida sin aceptar, pero no habia capturas recientes"); return; }
            string folder = Path.Combine(Dir, Name("missed", game));
            Log.Write("Samples: partida de " + game.Name + " arranco sin aceptar; guardo " + frames.Count + " capturas en " + folder);
            SaveAsync(() =>
            {
                Directory.CreateDirectory(folder);
                foreach (Frame f in frames)
                    using (f.Image) f.Image.Save(Path.Combine(folder, f.At.ToString("HHmmss") + ".png"), ImageFormat.Png);
            });
        }

        public static void Forget()
        {
            lock (Gate)
                while (recent.Count > 0) recent.Dequeue().Image.Dispose();
        }

        static string Name(string kind, GameProfile game)
        {
            return kind + "-" + game.Id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }

        // Guardar un PNG grande tarda; se hace fuera del loop de escaneo. Despues se borran las mas viejas.
        static void SaveAsync(Action save)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    save();
                    DirectoryInfo d = new DirectoryInfo(Dir);
                    foreach (FileSystemInfo old in d.GetFileSystemInfos().OrderByDescending(i => i.CreationTime).Skip(KeepEntries))
                    {
                        if (old is DirectoryInfo) ((DirectoryInfo)old).Delete(true);
                        else old.Delete();
                    }
                }
                catch (Exception e) { Log.Write("Samples: no pude guardar: " + e.Message); }
            });
        }

        static Bitmap Shrink(Bitmap src)
        {
            if (src.Width <= MaxWidth) return new Bitmap(src);
            int h = src.Height * MaxWidth / src.Width;
            Bitmap b = new Bitmap(MaxWidth, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(src, 0, 0, MaxWidth, h);
            }
            return b;
        }
    }
}
