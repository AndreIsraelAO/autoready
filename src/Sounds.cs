using System;
using System.IO;
using System.Media;
using System.Text;

namespace AutoReady
{
    // Avisos sonoros: notas suaves de una escala mayor (ataque lento, decaimiento tipo campanita), generadas al vuelo
    // como WAV en memoria: sin archivos ni recursos. Cada aviso tiene un dibujo distinto para no confundirlos.
    static class Sounds
    {
        const int Rate = 44100;

        static readonly object gate = new object();
        static SoundPlayer current;   // una referencia viva: si se recolecta, el sonido se corta

        // Aceptada / activo: mi -> sol.
        public static void Accepted() { Play(new double[][] { Note(659.25, 0, 450), Note(783.99, 140, 600) }, 800); }

        // Empezo la partida: arpegio mayor do-mi-sol.
        public static void MatchStarted() { Play(new double[][] { Note(523.25, 0, 450), Note(659.25, 130, 450), Note(783.99, 260, 700) }, 1000); }

        // Pausa: sol -> do, bajando.
        public static void Paused() { Play(new double[][] { Note(783.99, 0, 400), Note(523.25, 150, 600) }, 750); }

        static double[] Note(double freq, double startMs, double durMs) { return new double[] { freq, startMs, durMs }; }

        static void Play(double[][] notes, int totalMs)
        {
            try
            {
                byte[] wav = Synthesize(notes, totalMs);
                lock (gate)
                {
                    if (current != null) current.Stop();
                    current = new SoundPlayer(new MemoryStream(wav));
                    current.Play();   // en su propio hilo
                }
            }
            catch (Exception ex) { Log.Write("No se pudo reproducir el sonido: " + ex.Message); }
        }

        internal static byte[] Synthesize(double[][] notes, int totalMs)
        {
            int n = (int)((long)Rate * totalMs / 1000);
            double[] buf = new double[n];
            foreach (double[] note in notes)
            {
                int start = (int)(Rate * note[1] / 1000);
                int len = (int)(Rate * note[2] / 1000);
                for (int i = 0; i < len && start + i < n; i++)
                {
                    double t = (double)i / Rate;
                    double attack = Math.Min(1.0, i / 440.0);
                    double decay = Math.Exp(-5.0 * i / len);
                    double wave = Math.Sin(2 * Math.PI * note[0] * t)
                                + 0.25 * Math.Sin(4 * Math.PI * note[0] * t)
                                + 0.08 * Math.Sin(6 * Math.PI * note[0] * t);
                    buf[start + i] += 0.35 * attack * decay * wave;
                }
            }

            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            int dataLen = n * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
            foreach (double v in buf)
                w.Write((short)(Math.Max(-1.0, Math.Min(1.0, v)) * 28000));
            w.Flush();
            return ms.ToArray();
        }
    }
}
