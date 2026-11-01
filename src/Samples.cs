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
    // Capturas reales para mejorar el detector (solo durante la beta; ver issue #6). Solo de la ventana del juego,
    // solo en esta PC, y pocas: JPG chicos y como mucho 15 entradas (unos 10-15 MB en total).
    // - Al aceptar: el recorte de la zona del popup con la que se decidio el click.
    // - Si una partida arranca sin que AutoReady la haya aceptado (fallo, o el jugador acepto antes): las imagenes
    //   reducidas del ultimo minuto, donde tiene que aparecer el popup que no se detecto.
    static class Samples
    {
        public static readonly string Dir = Path.Combine(Config.Dir, "samples");
        const int KeepEntries = 15;
        static readonly TimeSpan Every = TimeSpan.FromSeconds(5);
        static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
        const int MaxWidth = 960;                  // las del historial van reducidas
        const long AcceptedQuality = 90, HistoryQuality = 75;

        class Frame { public DateTime At; public Bitmap Image; }

        static readonly object Gate = new object();
        static readonly Queue<Frame> recent = new Queue<Frame>();
        static DateTime lastAdded = DateTime.MinValue;
        public static bool Enabled = true;

        // Llamado en cada escaneo mientras se espera partida. Guarda en memoria una version reducida cada 5 s.
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

        // area: la zona del popup (la misma en la que busca el detector).
        public static void Accepted(Bitmap shot, GameProfile game, Rectangle area)
        {
            if (!Enabled) return;
            area.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
            Bitmap crop = shot.Clone(area, PixelFormat.Format24bppRgb);
            Forget();
            SaveAsync(() =>
            {
                using (crop) SaveJpeg(crop, Path.Combine(Dir, Name("accepted", game) + ".jpg"), AcceptedQuality);
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
                    using (f.Image) SaveJpeg(f.Image, Path.Combine(folder, f.At.ToString("HHmmss") + ".jpg"), HistoryQuality);
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

        // Guardar tarda; se hace fuera del loop de escaneo. Despues se borran las mas viejas.
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

        static void SaveJpeg(Bitmap b, string path, long quality)
        {
            ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using (EncoderParameters p = new EncoderParameters(1))
            {
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                b.Save(path, jpeg, p);
            }
        }

        static Bitmap Shrink(Bitmap src)
        {
            int w = Math.Min(MaxWidth, src.Width), h = src.Height * w / src.Width;
            Bitmap b = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(src, 0, 0, w, h);
            }
            return b;
        }
    }
}
