using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AutoReady
{
    // Busca el boton verde "ACCEPT" en una captura de la ventana de Dota.
    // Idea: mascara de pixeles verde-turquesa -> componentes conectados -> quedarse con el que tenga forma de boton.
    static class Detector
    {
        public class Settings
        {
            // Area de busqueda en fracciones de la ventana: el dialogo siempre aparece centrado.
            public double RoiX0 = 0.2, RoiY0 = 0.2, RoiX1 = 0.8, RoiY1 = 0.85;
            // Color del boton en HSV (H en grados). Medido en el boton ACCEPT: H ~154, S ~0.30-0.35, V ~0.35.
            public double HueMin = 120, HueMax = 190, SatMin = 0.18, ValMin = 0.24;
            // Tamano y forma del boton en fracciones de la ventana.
            public double MinWidthFrac = 0.07, MaxWidthFrac = 0.40;
            public double MinHeightFrac = 0.025, MaxHeightFrac = 0.15;
            public double MinAspect = 1.8, MaxAspect = 8.0;
            // Cuanto llena el componente su rectangulo (el boton es un rectangulo macizo; el texto no cuenta como hueco).
            public double MinFill = 0.6;
        }

        public class Hit
        {
            public Rectangle Box;   // en coordenadas de la imagen
            public Point Center { get { return new Point(Box.X + Box.Width / 2, Box.Y + Box.Height / 2); } }
        }

        const int Step = 2;   // analizamos 1 de cada 2 pixeles en cada eje: 4 veces menos trabajo, sobra precision

        public static Hit Find(Bitmap img, Settings s)
        {
            int w = img.Width, h = img.Height;
            Rectangle roi = Roi(w, h, s);
            if (roi.Width < 10 || roi.Height < 10) return null;

            BitmapData data = img.LockBits(roi, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = data.Stride / 4;
            int[] px = new int[stride * roi.Height];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            img.UnlockBits(data);

            int gw = roi.Width / Step, gh = roi.Height / Step;
            bool[] mask = new bool[gw * gh];
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                    mask[gy * gw + gx] = IsButtonColor(px[gy * Step * stride + gx * Step], s);

            int[] label = new int[gw * gh];
            int next = 0;
            Hit best = null;
            Stack<int> stack = new Stack<int>();
            List<int> cells = new List<int>();

            for (int start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || label[start] != 0) continue;
                next++;
                cells.Clear();
                stack.Push(start);
                label[start] = next;
                int minX = gw, minY = gh, maxX = -1, maxY = -1;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    cells.Add(i);
                    int x = i % gw, y = i / gw;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (x > 0) Visit(i - 1, mask, label, next, stack);
                    if (x < gw - 1) Visit(i + 1, mask, label, next, stack);
                    if (y > 0) Visit(i - gw, mask, label, next, stack);
                    if (y < gh - 1) Visit(i + gw, mask, label, next, stack);
                }

                int bw = (maxX - minX + 1) * Step, bh = (maxY - minY + 1) * Step;
                if (bw < w * s.MinWidthFrac || bw > w * s.MaxWidthFrac) continue;
                if (bh < h * s.MinHeightFrac || bh > h * s.MaxHeightFrac) continue;
                double aspect = (double)bw / bh;
                if (aspect < s.MinAspect || aspect > s.MaxAspect) continue;
                if (RowFill(cells, gw, minX, minY, maxX, maxY) < s.MinFill) continue;

                Rectangle box = new Rectangle(roi.X + minX * Step, roi.Y + minY * Step, bw, bh);
                if (best == null || bw * bh > best.Box.Width * best.Box.Height)
                    best = new Hit { Box = box };
            }
            return best;
        }

        static void Visit(int i, bool[] mask, int[] label, int id, Stack<int> stack)
        {
            if (mask[i] && label[i] == 0) { label[i] = id; stack.Push(i); }
        }

        // Relleno medido por filas: de la primera a la ultima celda de cada fila. Asi las letras blancas
        // dentro del boton no cuentan como huecos, pero un marco o una forma irregular si baja el valor.
        static double RowFill(List<int> cells, int gw, int minX, int minY, int maxX, int maxY)
        {
            int rows = maxY - minY + 1;
            int[] first = new int[rows], last = new int[rows];
            for (int r = 0; r < rows; r++) { first[r] = int.MaxValue; last[r] = -1; }
            foreach (int i in cells)
            {
                int r = i / gw - minY, x = i % gw;
                if (x < first[r]) first[r] = x;
                if (x > last[r]) last[r] = x;
            }
            long filled = 0;
            for (int r = 0; r < rows; r++)
                if (last[r] >= 0) filled += last[r] - first[r] + 1;
            return (double)filled / ((long)rows * (maxX - minX + 1));
        }

        static bool IsButtonColor(int argb, Settings s)
        {
            int r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max == 0) return false;
            double v = max / 255.0, sat = (double)(max - min) / max;
            if (v < s.ValMin || sat < s.SatMin) return false;
            double d = max - min, hue;
            if (max == r) hue = 60 * (((g - b) / d) % 6);
            else if (max == g) hue = 60 * ((b - r) / d + 2);
            else hue = 60 * ((r - g) / d + 4);
            if (hue < 0) hue += 360;
            return hue >= s.HueMin && hue <= s.HueMax;
        }

        public static Rectangle Roi(int w, int h, Settings s)
        {
            int x0 = (int)(w * s.RoiX0), y0 = (int)(h * s.RoiY0);
            return Rectangle.FromLTRB(x0, y0, (int)(w * s.RoiX1), (int)(h * s.RoiY1));
        }

        // Copia de la captura con el area de busqueda (celeste) y el boton encontrado (rojo), para depurar.
        public static Bitmap Annotate(Bitmap img, Hit hit, Settings s)
        {
            Bitmap copy = new Bitmap(img);
            using (Graphics g = Graphics.FromImage(copy))
            {
                using (Pen roiPen = new Pen(Color.DeepSkyBlue, 3))
                    g.DrawRectangle(roiPen, Roi(img.Width, img.Height, s));
                if (hit != null)
                    using (Pen hitPen = new Pen(Color.Red, 4))
                    {
                        g.DrawRectangle(hitPen, hit.Box);
                        g.FillEllipse(Brushes.Red, hit.Center.X - 6, hit.Center.Y - 6, 12, 12);
                    }
            }
            return copy;
        }
    }
}
