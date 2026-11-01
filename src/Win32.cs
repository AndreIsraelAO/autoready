using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoReady
{
    // Wrappers de Win32: buscar la ventana de Dota, capturarla, darle foco y hacer click.
    static class Win32
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string msg);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_NOREPEAT = 0x4000;

        const uint PW_CLIENTONLY = 0x1, PW_RENDERFULLCONTENT = 0x2;
        const uint MOUSE_DOWN = 0x02, MOUSE_UP = 0x04, KEY_UP = 0x02;
        const byte VK_MENU = 0x12;
        const int SW_RESTORE = 9;
        public const int HSHELL_FLASH = 0x8006;

        public static IntPtr FindGameWindow(string processName)
        {
            foreach (Process p in Process.GetProcessesByName(processName))
            {
                try
                {
                    IntPtr h = p.MainWindowHandle;
                    if (h != IntPtr.Zero) return h;
                }
                finally { p.Dispose(); }
            }
            return IntPtr.Zero;
        }

        public static Rectangle ClientRectOnScreen(IntPtr hwnd)
        {
            RECT r;
            GetClientRect(hwnd, out r);
            POINT p = new POINT();
            ClientToScreen(hwnd, ref p);
            return new Rectangle(p.X, p.Y, r.Right, r.Bottom);
        }

        // Captura el area cliente de la ventana. PrintWindow funciona aunque la ventana este tapada;
        // si devuelve todo negro (algunos modos de DirectX) y Dota esta al frente, copiamos de la pantalla.
        public static Bitmap Capture(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) return null;
            Rectangle rc = ClientRectOnScreen(hwnd);
            if (rc.Width < 200 || rc.Height < 200) return null;

            Bitmap bmp = new Bitmap(rc.Width, rc.Height, PixelFormat.Format32bppArgb);
            bool ok;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                ok = PrintWindow(hwnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
                g.ReleaseHdc(hdc);
            }
            if (ok && !IsBlank(bmp)) return bmp;

            if (GetForegroundWindow() == hwnd)
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                return bmp;
            }
            bmp.Dispose();
            return null;
        }

        static bool IsBlank(Bitmap bmp)
        {
            int[] xs = { 1, 2, 3 }, ys = { 1, 2, 3 };
            foreach (int fx in xs)
                foreach (int fy in ys)
                {
                    Color c = bmp.GetPixel(bmp.Width * fx / 4, bmp.Height * fy / 4);
                    if (c.R > 8 || c.G > 8 || c.B > 8) return false;
                }
            return true;
        }

        public static void Focus(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            if (GetForegroundWindow() == hwnd) return;
            // Windows no deja que un proceso en segundo plano robe el foco; apretar Alt levanta ese bloqueo.
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEY_UP, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
        }

        // Click en coordenadas de pantalla y devuelve el mouse a donde estaba.
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        // Cuanto hace que el usuario toco el mouse o el teclado (en cualquier ventana).
        public static TimeSpan IdleTime()
        {
            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(li);
            if (!GetLastInputInfo(ref li)) return TimeSpan.MaxValue;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - li.dwTime));
        }

        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        // La ventana de nivel superior que esta debajo de ese punto de la pantalla es la del juego.
        public static bool PointBelongsTo(IntPtr hwnd, int x, int y)
        {
            IntPtr under = WindowFromPoint(new POINT { X = x, Y = y });
            return under != IntPtr.Zero && GetAncestor(under, 2 /* GA_ROOT */) == hwnd;
        }

        public static void PressEnter()
        {
            const byte VK_RETURN = 0x0D;
            keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
            Thread.Sleep(40);
            keybd_event(VK_RETURN, 0, KEY_UP, UIntPtr.Zero);
        }

        public static void Click(int x, int y)
        {
            POINT old;
            GetCursorPos(out old);
            SetCursorPos(x, y);
            Thread.Sleep(50);
            mouse_event(MOUSE_DOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            mouse_event(MOUSE_UP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            SetCursorPos(old.X, old.Y);
        }
    }
}
