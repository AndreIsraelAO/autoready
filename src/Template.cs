using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace AutoReady
{
    // Deteccion por recorte: busca en la captura un recorte real del boton ACCEPT de cada juego.
    // Es lo que hace facil sumar juegos: alcanza con una captura del popup, sin ajustar colores a mano.
    //
    // Los recortes van en %APPDATA%\AutoReady\templates\<juego>@<alto>.png, donde <alto> es la altura de la ventana del
    // juego en la que se tomo (ej. dota2@1440.png). La interfaz de estos juegos escala con la altura de la ventana,
    // asi que el recorte se reescala a la ventana actual. Se compara por correlacion normalizada (NCC) en color,
    // sobre imagenes reducidas, primero gruesa y despues fina alrededor del mejor punto.
    class Template
    {
        public static readonly string Dir = Path.Combine(Config.Dir, "templates");
        const int WorkHeight = 360;          // altura de trabajo: rapido y suficiente para un boton
        const double MinScore = 0.80;        // parecido minimo para considerarlo el boton
        static readonly double[] Scales = { 0.9, 1.0, 1.1 };

        readonly Bitmap image;
        readonly int refHeight;

        Template(Bitmap image, int refHeight) { this.image = image; this.refHeight = refHeight; }

        public static Template Load(GameProfile game)
        {
            try
            {
                if (!Directory.Exists(Dir)) return null;
                foreach (string f in Directory.GetFiles(Dir, game.Id + "@*.png"))
                {
                    int h;
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (!int.TryParse(name.Substring(name.IndexOf('@') + 1), out h) || h < 100) continue;
                    using (Bitmap b = new Bitmap(f)) return new Template(new Bitmap(b), h);
                }
            }
            catch (Exception e) { Log.Write("Template " + game.Name + ": no pude cargar: " + e.Message); }
            return null;
        }

        public static Template FromBitmap(Bitmap crop, int refHeight) { return new Template(new Bitmap(crop), refHeight); }

        // Devuelve el boton en coordenadas de la imagen, o null. roi: zona donde buscar (la del popup).
        public Detector.Hit Find(Bitmap shot, Rectangle roi)
        {
            double f = (double)WorkHeight / shot.Height;
            Rectangle r = Rectangle.Intersect(roi, new Rectangle(0, 0, shot.Width, shot.Height));
            float[] hay; int hw, hh;
            using (Bitmap part = shot.Clone(r, PixelFormat.Format32bppArgb))
                hay = Pixels(part, Math.Max(1, (int)(r.Width * f)), Math.Max(1, (int)(r.Height * f)), out hw, out hh);

            double bestScore = -1; int bestX = 0, bestY = 0, bestW = 0, bestH = 0;
            foreach (double s in Scales)
            {
                // El recorte se tomo en una ventana de refHeight: a la altura de trabajo mide image * (WorkHeight / refHeight).
                double k = (double)WorkHeight / refHeight * s;
                int tw = (int)Math.Round(image.Width * k), th = (int)Math.Round(image.Height * k);
                if (tw < 6 || th < 4 || tw > hw || th > hh) continue;
                int dummyW, dummyH;
                float[] needle = Pixels(image, tw, th, out dummyW, out dummyH);
                Normalize(needle);

                // Pasada gruesa cada 2 px y despues fina alrededor del mejor.
                int cx = 0, cy = 0; double cs = -1;
                for (int y = 0; y + th <= hh; y += 2)
                    for (int x = 0; x + tw <= hw; x += 2)
                    {
                        double v = Ncc(hay, hw, needle, tw, th, x, y);
                        if (v > cs) { cs = v; cx = x; cy = y; }
                    }
                for (int y = Math.Max(0, cy - 2); y <= Math.Min(hh - th, cy + 2); y++)
                    for (int x = Math.Max(0, cx - 2); x <= Math.Min(hw - tw, cx + 2); x++)
                    {
                        double v = Ncc(hay, hw, needle, tw, th, x, y);
                        if (v > cs) { cs = v; cx = x; cy = y; }
                    }
                if (cs > bestScore) { bestScore = cs; bestX = cx; bestY = cy; bestW = tw; bestH = th; }
            }
            if (bestScore < MinScore) return null;
            return new Detector.Hit
            {
                Box = new Rectangle(r.X + (int)(bestX / f), r.Y + (int)(bestY / f), (int)(bestW / f), (int)(bestH / f)),
                Score = bestScore,
            };
        }

        // Imagen reescalada como RGB en floats (r,g,b,r,g,b,...).
        static float[] Pixels(Bitmap src, int w, int h, out int ow, out int oh)
        {
            ow = w; oh = h;
            using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, w, h);
                }
                BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int stride = d.Stride / 4;
                int[] px = new int[stride * h];
                Marshal.Copy(d.Scan0, px, 0, px.Length);
                b.UnlockBits(d);
                float[] o = new float[w * h * 3];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int c = px[y * stride + x], i = (y * w + x) * 3;
                        o[i] = (c >> 16) & 0xFF; o[i + 1] = (c >> 8) & 0xFF; o[i + 2] = c & 0xFF;
                    }
                return o;
            }
        }

        // Resta la media y divide por la norma: asi el NCC queda como un producto punto.
        static void Normalize(float[] v)
        {
            double mean = 0;
            foreach (float x in v) mean += x;
            mean /= v.Length;
            double norm = 0;
            for (int i = 0; i < v.Length; i++) { v[i] = (float)(v[i] - mean); norm += v[i] * v[i]; }
            norm = Math.Sqrt(norm);
            if (norm < 1e-6) return;
            for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
        }

        static double Ncc(float[] hay, int hw, float[] needle, int tw, int th, int ox, int oy)
        {
            double sum = 0, sum2 = 0, dot = 0;
            int n = tw * th * 3;
            for (int y = 0; y < th; y++)
            {
                int row = ((oy + y) * hw + ox) * 3, trow = y * tw * 3;
                for (int x = 0; x < tw * 3; x++)
                {
                    double v = hay[row + x];
                    sum += v; sum2 += v * v; dot += v * needle[trow + x];
                }
            }
            double var = sum2 - sum * sum / n;
            return var < 1e-6 ? 0 : dot / Math.Sqrt(var);   // needle ya tiene media 0 y norma 1
        }
    }
}
