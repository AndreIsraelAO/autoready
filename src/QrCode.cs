using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace AutoReady
{
    // Generador de codigos QR minimo: modo byte, correccion de errores nivel M, versiones 1 a 6
    // (hasta 106 bytes, sobra para un link de ntfy). Desde la version 7 el QR necesita un bloque extra
    // de "version info" que aca no hace falta.
    class QrCode
    {
        // Por version (indice = version): codewords de correccion por bloque, cantidad de bloques y
        // codewords de datos por bloque, para nivel M. Hasta la version 6 todos los bloques son iguales.
        static readonly int[] EcPerBlock = { 0, 10, 16, 26, 18, 24, 16 };
        static readonly int[] Blocks = { 0, 1, 1, 1, 2, 2, 4 };
        static readonly int[] DataPerBlock = { 0, 16, 28, 44, 32, 43, 27 };
        static readonly int[] AlignPos = { 0, 0, 18, 22, 26, 30, 34 };   // 0 = sin patron de alineacion

        public readonly int Size;
        readonly bool[,] dark;
        readonly bool[,] isFunction;

        public bool this[int x, int y] { get { return dark[y, x]; } }

        public static QrCode Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            for (int v = 1; v <= 6; v++)
            {
                int capacityBits = Blocks[v] * DataPerBlock[v] * 8;
                if (4 + 8 + data.Length * 8 <= capacityBits) return new QrCode(v, data);
            }
            throw new ArgumentException("texto demasiado largo para el QR");
        }

        QrCode(int version, byte[] data)
        {
            Size = version * 4 + 17;
            dark = new bool[Size, Size];
            isFunction = new bool[Size, Size];

            DrawFunctionPatterns(version);
            byte[] codewords = AddErrorCorrection(version, BuildDataCodewords(version, data));
            PlaceCodewords(codewords);

            // Se prueban las 8 mascaras y se queda la que deja el QR mas facil de leer.
            int bestMask = 0, bestScore = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(m);
                DrawFormatBits(m);
                int score = Penalty();
                if (score < bestScore) { bestScore = score; bestMask = m; }
                ApplyMask(m);   // XOR otra vez: deshace la mascara
            }
            ApplyMask(bestMask);
            DrawFormatBits(bestMask);
        }

        // ---------- patrones fijos ----------

        void Set(int x, int y, bool isDark)
        {
            dark[y, x] = isDark;
            isFunction[y, x] = true;
        }

        void DrawFunctionPatterns(int version)
        {
            for (int i = 0; i < Size; i++)
            {
                Set(6, i, i % 2 == 0);   // lineas de timing
                Set(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);

            if (AlignPos[version] != 0)
            {
                int p = AlignPos[version];
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(p + dx, p + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }

            DrawFormatBits(0);   // reserva el lugar; se reescribe con la mascara elegida
        }

        // Patron de esquina de 7x7 mas su separador blanco.
        void DrawFinder(int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(x, y, d != 2 && d != 4);
                }
        }

        // 15 bits: nivel M (00) + mascara, con BCH y la mascara fija 0x5412. Van dos copias.
        void DrawFormatBits(int mask)
        {
            int data = mask;   // nivel M = 00 en los dos bits altos
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ (((rem >> 9) & 1) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;
            Func<int, bool> bit = i => ((bits >> i) & 1) != 0;

            for (int i = 0; i <= 5; i++) Set(8, i, bit(i));
            Set(8, 7, bit(6));
            Set(8, 8, bit(7));
            Set(7, 8, bit(8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, bit(i));

            for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, bit(i));
            for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, bit(i));
            Set(8, Size - 8, true);   // modulo oscuro fijo
        }

        // ---------- datos ----------

        static byte[] BuildDataCodewords(int version, byte[] data)
        {
            int capacity = Blocks[version] * DataPerBlock[version];
            List<bool> bits = new List<bool>();
            Action<int, int> append = (val, len) => { for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); };

            append(0x4, 4);             // modo byte
            append(data.Length, 8);     // cantidad de bytes (8 bits hasta la version 9)
            foreach (byte b in data) append(b, 8);
            append(0, Math.Min(4, capacity * 8 - bits.Count));   // terminador
            while (bits.Count % 8 != 0) bits.Add(false);

            List<byte> result = new List<byte>();
            for (int i = 0; i < bits.Count; i += 8)
            {
                int b = 0;
                for (int j = 0; j < 8; j++) b = (b << 1) | (bits[i + j] ? 1 : 0);
                result.Add((byte)b);
            }
            for (int pad = 0; result.Count < capacity; pad ^= 1) result.Add((byte)(pad == 0 ? 0xEC : 0x11));
            return result.ToArray();
        }

        // Divide en bloques, calcula Reed-Solomon de cada uno e intercala: primero los datos y despues
        // la correccion, tomando un codeword de cada bloque por vez.
        static byte[] AddErrorCorrection(int version, byte[] data)
        {
            int nBlocks = Blocks[version], perBlock = DataPerBlock[version], ecLen = EcPerBlock[version];
            byte[] generator = RsGenerator(ecLen);
            byte[][] dataBlocks = new byte[nBlocks][], ecBlocks = new byte[nBlocks][];
            for (int b = 0; b < nBlocks; b++)
            {
                dataBlocks[b] = new byte[perBlock];
                Array.Copy(data, b * perBlock, dataBlocks[b], 0, perBlock);
                ecBlocks[b] = RsRemainder(dataBlocks[b], generator);
            }
            List<byte> result = new List<byte>();
            for (int i = 0; i < perBlock; i++)
                for (int b = 0; b < nBlocks; b++) result.Add(dataBlocks[b][i]);
            for (int i = 0; i < ecLen; i++)
                for (int b = 0; b < nBlocks; b++) result.Add(ecBlocks[b][i]);
            return result.ToArray();
        }

        // Recorre la matriz en zigzag de a dos columnas, de abajo a la derecha, salteando la columna 6.
        void PlaceCodewords(byte[] codewords)
        {
            int bitIndex = 0, totalBits = codewords.Length * 8;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                bool upward = ((right + 1) & 2) == 0;
                for (int step = 0; step < Size; step++)
                {
                    int y = upward ? Size - 1 - step : step;
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        if (isFunction[y, x]) continue;
                        if (bitIndex < totalBits)
                            dark[y, x] = ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                        bitIndex++;   // los bits sobrantes quedan en blanco
                    }
                }
            }
        }

        void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (isFunction[y, x]) continue;
                    bool flip;
                    switch (mask)
                    {
                        case 0: flip = (x + y) % 2 == 0; break;
                        case 1: flip = y % 2 == 0; break;
                        case 2: flip = x % 3 == 0; break;
                        case 3: flip = (x + y) % 3 == 0; break;
                        case 4: flip = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: flip = x * y % 2 + x * y % 3 == 0; break;
                        case 6: flip = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: flip = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (flip) dark[y, x] = !dark[y, x];
                }
        }

        // Puntaje de penalizacion del estandar: rachas largas, bloques 2x2, patrones parecidos a las
        // esquinas y desbalance entre oscuro y claro. Menor es mejor.
        int Penalty()
        {
            int score = 0, darkCount = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < Size; a++)
                {
                    int run = 1;
                    for (int b = 1; b <= Size; b++)
                    {
                        bool same = b < Size && Get(pass, a, b) == Get(pass, a, b - 1);
                        if (same) { run++; continue; }
                        if (run >= 5) score += 3 + (run - 5);
                        run = 1;
                    }
                    for (int b = 0; b + 10 < Size; b++)
                    {
                        bool p1 = Matches(pass, a, b, new[] { true, false, true, true, true, false, true, false, false, false, false });
                        bool p2 = Matches(pass, a, b, new[] { false, false, false, false, true, false, true, true, true, false, true });
                        if (p1) score += 40;
                        if (p2) score += 40;
                    }
                }
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (dark[y, x]) darkCount++;
                    if (x + 1 < Size && y + 1 < Size && dark[y, x] == dark[y, x + 1] &&
                        dark[y, x] == dark[y + 1, x] && dark[y, x] == dark[y + 1, x + 1]) score += 3;
                }
            int percent = darkCount * 100 / (Size * Size);
            score += Math.Abs(percent - 50) / 5 * 10;
            return score;
        }

        bool Get(int pass, int a, int b) { return pass == 0 ? dark[a, b] : dark[b, a]; }

        bool Matches(int pass, int a, int b, bool[] pattern)
        {
            for (int i = 0; i < pattern.Length; i++)
                if (Get(pass, a, b + i) != pattern[i]) return false;
            return true;
        }

        // ---------- Reed-Solomon en GF(256) con el polinomio 0x11D ----------

        static int GfMul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        static byte[] RsGenerator(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = (byte)GfMul(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = GfMul(root, 2);
            }
            return result;
        }

        static byte[] RsRemainder(byte[] data, byte[] generator)
        {
            byte[] result = new byte[generator.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)GfMul(generator[i], factor);
            }
            return result;
        }

        // ---------- dibujo ----------

        // Bitmap con 4 modulos de margen blanco (lo pide el estandar para que las camaras lo lean).
        public Bitmap ToBitmap(int moduleSize)
        {
            int border = 4, px = (Size + border * 2) * moduleSize;
            Bitmap bmp = new Bitmap(px, px);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        if (dark[y, x])
                            g.FillRectangle(Brushes.Black, (x + border) * moduleSize, (y + border) * moduleSize, moduleSize, moduleSize);
            }
            return bmp;
        }
    }
}
