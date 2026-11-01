using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace AutoReady
{
    // Lo que el panel muestra; lo arma TrayApp cada vez que algo cambia.
    class PanelStatus
    {
        public string Text;            // "Esperando partida en Dota 2"
        public bool Good;              // punto verde (vigilando) o gris
        public bool Paused;
        public string UpdateTag;       // "v0.2.3" si hay version nueva, si no null
        public string UpdateMessage;   // resultado de "Buscar actualizaciones"
        public string Protection;      // "Activa en Dota 2 y CS2" o "Falta un paso en Dota 2"
        public GameProfile ProtectionFix;   // juego al que le falta la opcion de lanzamiento (null = todo bien)
    }

    // Lo que el panel puede pedirle a la app. Cada cambio se aplica al instante (sin boton Guardar).
    interface IPanelHost
    {
        Config Cfg { get; }
        void SetPaused(bool paused);
        void SetPhone(bool on);
        void FixProtection(GameProfile game);
        void SendTestPush();
        void SetAutostart(bool on);
        bool AutostartEnabled { get; }
        void SetAutoUpdate(bool on);
        void CheckUpdatesNow();
        void InstallUpdate();
        string HotkeyText { get; }
        // Registra el atajo global (vk = 0 lo borra). Devuelve false si otra app ya usa esa combinacion.
        bool SetHotkey(int vk, int mods);
    }

    // Colores de Windows 11 para tema claro y oscuro (sigue el tema de apps del sistema).
    class Theme
    {
        public Brush Background, Card, CardStroke, Text, TextSecondary, ControlStroke, Accent, OnAccent, Good, Idle, Link;
        public bool Dark;

        public static Theme FromSystem()
        {
            bool light = true;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                if (k != null && k.GetValue("AppsUseLightTheme") is int) light = (int)k.GetValue("AppsUseLightTheme") != 0;

            Theme t = new Theme { Dark = !light };
            t.Accent = Rgb(0x3F, 0xB3, 0x7F);
            t.OnAccent = Brushes.White;
            t.Good = Rgb(0x3F, 0xB3, 0x7F);
            t.Idle = Rgb(0x9A, 0x9A, 0x9A);
            if (light)
            {
                t.Background = Rgb(0xF3, 0xF3, 0xF3); t.Card = Rgb(0xFF, 0xFF, 0xFF); t.CardStroke = Rgb(0xE5, 0xE5, 0xE5);
                t.Text = Rgb(0x1B, 0x1B, 0x1B); t.TextSecondary = Rgb(0x5F, 0x5F, 0x5F); t.ControlStroke = Rgb(0x8A, 0x8A, 0x8A);
                t.Link = Rgb(0x1F, 0x8A, 0x5E);
            }
            else
            {
                t.Background = Rgb(0x20, 0x20, 0x20); t.Card = Rgb(0x2B, 0x2B, 0x2B); t.CardStroke = Rgb(0x1C, 0x1C, 0x1C);
                t.Text = Rgb(0xFF, 0xFF, 0xFF); t.TextSecondary = Rgb(0xC5, 0xC5, 0xC5); t.ControlStroke = Rgb(0x9A, 0x9A, 0x9A);
                t.Link = Rgb(0x5F, 0xD4, 0x9E);
            }
            return t;
        }

        static Brush Rgb(byte r, byte g, byte b)
        {
            SolidColorBrush s = new SolidColorBrush(Color.FromRgb(r, g, b));
            s.Freeze();
            return s;
        }
    }

    // Interruptor estilo Windows 11: pista redondeada y circulo que se desliza.
    class Toggle : Border
    {
        readonly Theme theme;
        readonly Ellipse thumb;
        readonly TranslateTransform slide = new TranslateTransform();
        bool on;
        public event Action<bool> Changed;

        public Toggle(Theme theme, bool initial)
        {
            this.theme = theme;
            Width = 40; Height = 20; CornerRadius = new CornerRadius(10);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            thumb = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), RenderTransform = slide };
            Child = thumb;
            MouseLeftButtonUp += (s, e) =>
            {
                Apply(!on, true);
                Action<bool> h = Changed;
                if (h != null) h(on);
            };
            Apply(initial, false);
        }

        public bool IsOn { get { return on; } set { if (value != on) Apply(value, true); } }

        void Apply(bool value, bool animate)
        {
            on = value;
            Background = on ? theme.Accent : Brushes.Transparent;
            BorderBrush = on ? theme.Accent : theme.ControlStroke;
            thumb.Fill = on ? theme.OnAccent : theme.ControlStroke;
            double to = on ? 20 : 0;
            if (animate)
                slide.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() });
            else
                slide.X = to;
        }
    }

    // Panel que se abre con click izquierdo en el icono de la bandeja y se cierra al hacer click afuera.
    class Panel : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

        readonly IPanelHost host;
        readonly Theme theme;
        TextBlock statusText, updateText;
        Ellipse statusDot;
        Toggle acceptToggle, phoneToggle;
        Border updateButton;
        StackPanel phoneDetails;
        TextBlock hotkeyLabel, hotkeyHint, protectionText;
        Border protectionButton;
        GameProfile protectionGame;
        Border hotkeyButton;
        bool capturingHotkey;
        DateTime hiddenAt = DateTime.MinValue;

        public Panel(IPanelHost host)
        {
            this.host = host;
            theme = Theme.FromSystem();

            Title = "AutoReady";
            Width = 380;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = theme.Background;
            BorderBrush = theme.Dark ? new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)) : new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            BorderThickness = new Thickness(1);
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            FontSize = 14;
            Foreground = theme.Text;
            UseLayoutRounding = true;

            // Si la pantalla es baja (1080p con escala, por ejemplo), el contenido scrollea en vez de cortarse.
            MaxHeight = SystemParameters.WorkArea.Height - 24;
            Content = new ScrollViewer { Content = Build(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int round = DWMWCP_ROUND, dark = theme.Dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);   // esquinas redondeadas de Windows 11
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            };
            Deactivated += (s, e) => { StopHotkeyCapture(); HidePanel(); };
            PreviewKeyDown += OnPreviewKeyDown;
        }

        // ---------- atajo de teclado ----------

        void StartHotkeyCapture()
        {
            capturingHotkey = true;
            hotkeyLabel.Text = "Apretá una tecla…";
            hotkeyHint.Text = "Esc cancela · Borrar lo quita";
        }

        void StopHotkeyCapture()
        {
            if (!capturingHotkey) return;
            capturingHotkey = false;
            hotkeyLabel.Text = host.HotkeyText;
            hotkeyHint.Text = "Pausa o reanuda desde cualquier lado";
        }

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!capturingHotkey)
            {
                if (e.Key == Key.Escape) HidePanel();
                return;
            }
            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
                return;   // esperamos la tecla de verdad
            if (key == Key.Escape) { StopHotkeyCapture(); return; }

            int vk = (key == Key.Back || key == Key.Delete) ? 0 : KeyInterop.VirtualKeyFromKey(key);
            int mods = 0;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= 0x1;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= 0x2;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= 0x4;
            bool ok = host.SetHotkey(vk, mods);
            capturingHotkey = false;
            hotkeyLabel.Text = host.HotkeyText;
            hotkeyHint.Text = ok ? "Pausa o reanuda desde cualquier lado"
                                 : "Esa tecla ya la usa otra app. Probá con otra.";
        }

        // Click en el icono: abre o cierra. Si se acaba de cerrar por ese mismo click (perdio el foco), no lo reabre.
        public void TogglePanel()
        {
            if (IsVisible) { HidePanel(); return; }
            if ((DateTime.Now - hiddenAt).TotalMilliseconds < 300) return;
            ShowPanel();
        }

        public void ShowPanel()
        {
            Show();
            UpdateLayout();
            Rect wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 12;
            Top = wa.Bottom - ActualHeight - 12;
            Activate();
        }

        void HidePanel()
        {
            if (!IsVisible) return;
            hiddenAt = DateTime.Now;
            Hide();
        }

        public void Refresh(PanelStatus st)
        {
            statusText.Text = st.Text;
            statusDot.Fill = st.Good ? theme.Good : theme.Idle;
            acceptToggle.IsOn = !st.Paused;

            bool hasUpdate = st.UpdateTag != null;
            updateButton.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
            if (hasUpdate) ((TextBlock)updateButton.Child).Text = "Actualizar a " + st.UpdateTag;
            updateText.Text = "Versión " + Updater.CurrentVersionText + (st.UpdateMessage != null ? " · " + st.UpdateMessage : "");

            // Si el usuario ya toco "Activar" y todavia no termino, no se pisa la instruccion.
            bool waitingUser = protectionGame != null && st.ProtectionFix == protectionGame && protectionText.Text.StartsWith("En Steam");
            protectionGame = st.ProtectionFix;
            protectionButton.Visibility = st.ProtectionFix != null ? Visibility.Visible : Visibility.Collapsed;
            if (!waitingUser) protectionText.Text = st.Protection;
        }

        // ---------- construccion ----------

        UIElement Build()
        {
            StackPanel root = new StackPanel { Margin = new Thickness(16) };

            // Encabezado: logo, nombre y estado.
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            Image logo = new Image { Source = LoadPng("AutoReady.logo-64.png"), Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            header.Children.Add(logo);
            StackPanel titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "AutoReady", FontSize = 16, FontWeight = FontWeights.SemiBold });
            StackPanel statusLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            statusDot = new Ellipse { Width = 8, Height = 8, Fill = theme.Idle, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            statusText = new TextBlock { Text = "Iniciando…", FontSize = 12, Foreground = theme.TextSecondary };
            statusLine.Children.Add(statusDot);
            statusLine.Children.Add(statusText);
            titles.Children.Add(statusLine);
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);
            root.Children.Add(header);

            // Boton de actualizacion: solo aparece si hay version nueva.
            updateButton = new Border
            {
                Background = theme.Accent, CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 12, 0, 0), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed,
                Child = new TextBlock { Foreground = theme.OnAccent, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
            };
            updateButton.MouseLeftButtonUp += (s, e) => host.InstallUpdate();
            root.Children.Add(updateButton);

            // Interruptor principal.
            acceptToggle = new Toggle(theme, true);
            acceptToggle.Changed += on => host.SetPaused(!on);
            StackPanel main = new StackPanel();
            main.Children.Add(Row("Aceptar partidas", "Apagalo para pausar", acceptToggle));
            main.Children.Add(Divider());
            hotkeyLabel = new TextBlock { Text = host.HotkeyText, FontSize = 13 };
            hotkeyButton = new Border
            {
                BorderBrush = theme.ControlStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 3, 10, 3), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Child = hotkeyLabel,
            };
            hotkeyButton.MouseLeftButtonUp += (s, e) => StartHotkeyCapture();
            StackPanel hotkeyLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            hotkeyLeft.Children.Add(new TextBlock { Text = "Atajo" });
            hotkeyHint = new TextBlock { Text = "Pausa o reanuda desde cualquier lado", FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap };
            hotkeyLeft.Children.Add(hotkeyHint);
            main.Children.Add(Row(hotkeyLeft, hotkeyButton));
            main.Children.Add(Divider());

            // Proteccion en partida (GSI): si falta, AutoReady trabaja en modo seguro y se ofrece activarla.
            StackPanel protLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            protLeft.Children.Add(new TextBlock { Text = "Protección en partida" });
            protectionText = new TextBlock { Text = "Revisando…", FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap };
            protLeft.Children.Add(protectionText);
            protectionButton = new Border
            {
                Background = theme.Accent, CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "Activar", Foreground = theme.OnAccent, FontSize = 13, FontWeight = FontWeights.SemiBold },
            };
            protectionButton.MouseLeftButtonUp += (s, e) =>
            {
                if (protectionGame == null) return;
                Clipboard.SetText(protectionGame.GsiLaunchOption);
                host.FixProtection(protectionGame);
                protectionText.Text = "En Steam se abrió Propiedades de " + protectionGame.Name + ": pegá (Ctrl+V) en " +
                                      "Opciones de lanzamiento y reiniciá el juego.";
            };
            main.Children.Add(Row(protLeft, protectionButton));
            root.Children.Add(Wrap(main, new Thickness(0, 14, 0, 0)));

            // Avisos.
            root.Children.Add(SectionLabel("Avisos"));
            StackPanel alerts = new StackPanel();
            Toggle sound = new Toggle(theme, host.Cfg.Sound);
            sound.Changed += on => { host.Cfg.Sound = on; host.Cfg.Save(); };
            alerts.Children.Add(Row("Sonido", null, sound));
            alerts.Children.Add(Divider());
            Toggle match = new Toggle(theme, host.Cfg.NotifyMatchStarted);
            match.Changed += on => { host.Cfg.NotifyMatchStarted = on; host.Cfg.Save(); };
            alerts.Children.Add(Row("Cuando arranca la partida", "Para que vuelvas a tiempo", match));
            alerts.Children.Add(Divider());
            phoneToggle = new Toggle(theme, host.Cfg.NotifyPhone);
            phoneToggle.Changed += on => { host.SetPhone(on); phoneDetails.Visibility = on ? Visibility.Visible : Visibility.Collapsed; };
            alerts.Children.Add(Row("Celular", "Avisos en el celular con la app gratis ntfy", phoneToggle));
            phoneDetails = BuildPhoneDetails();
            phoneDetails.Visibility = host.Cfg.NotifyPhone ? Visibility.Visible : Visibility.Collapsed;
            alerts.Children.Add(phoneDetails);
            root.Children.Add(Wrap(alerts, new Thickness(0)));

            // General.
            root.Children.Add(SectionLabel("General"));
            StackPanel general = new StackPanel();
            Toggle autostart = new Toggle(theme, host.AutostartEnabled);
            autostart.Changed += on => host.SetAutostart(on);
            general.Children.Add(Row("Iniciar con Windows", null, autostart));
            general.Children.Add(Divider());
            Toggle autoUpdate = new Toggle(theme, host.Cfg.AutoUpdateCheck);
            autoUpdate.Changed += on => host.SetAutoUpdate(on);
            general.Children.Add(Row("Actualizar automáticamente", null, autoUpdate));
            general.Children.Add(Divider());
            updateText = new TextBlock { FontSize = 12, Foreground = theme.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
            general.Children.Add(Row(updateText, Link("Buscar actualizaciones", () => host.CheckUpdatesNow())));
            root.Children.Add(Wrap(general, new Thickness(0)));

            // Pie: el wordmark de keis.studio con la k en tres colores; a este tamano el corrimiento va exagerado
            // (como mark-*-small de la guia) para que los colores se vean nitidos.
            Image brand = new Image
            {
                Source = KeisMark.BuildWordmark(theme.Dark, true, 2.2), Height = 20, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 4), Cursor = Cursors.Hand, ToolTip = "keis.studio",
            };
            brand.MouseLeftButtonUp += (s, e) => System.Diagnostics.Process.Start("https://keis.studio");
            root.Children.Add(brand);

            return root;
        }

        StackPanel BuildPhoneDetails()
        {
            StackPanel p = new StackPanel { Margin = new Thickness(14, 2, 14, 14) };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            // Un solo QR para cualquier celular: la camara abre https://ntfy.sh/<codigo>, que ya queda suscripto.
            Border qrBox = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(6), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            qrBox.Child = new Image { Source = QrImage("https://ntfy.sh/" + host.Cfg.NtfyTopic), Width = 108, Height = 108 };
            g.Children.Add(qrBox);

            StackPanel right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock { Text = "Escaneá con la cámara", FontSize = 13, FontWeight = FontWeights.SemiBold });
            right.Children.Add(Small("o agregá este código en ntfy:"));

            // El codigo a la vista y seleccionable: se lee de una para escribirlo en el celular.
            TextBox code = new TextBox
            {
                Text = host.Cfg.NtfyTopic, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = theme.Text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 17,
                FontWeight = FontWeights.SemiBold, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 0), CaretBrush = Brushes.Transparent,
            };
            right.Children.Add(code);

            Border test = new Border
            {
                BorderBrush = theme.ControlStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand,
                Child = new TextBlock { Text = "Enviar prueba", FontSize = 12 },
            };
            test.MouseLeftButtonUp += (s, e) => { host.SendTestPush(); ((TextBlock)test.Child).Text = "Enviada al celular"; };
            right.Children.Add(test);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            p.Children.Add(g);
            return p;
        }

        // ---------- piezas ----------

        Border Wrap(StackPanel content, Thickness margin)
        {
            return new Border { Background = theme.Card, BorderBrush = theme.CardStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = margin, Child = content };
        }

        TextBlock SectionLabel(string text)
        {
            return new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 16, 0, 8) };
        }

        Border Divider()
        {
            return new Border { Height = 1, Background = theme.Dark ? new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)) : new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA)) };
        }

        Grid Row(string title, string subtitle, UIElement right)
        {
            StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = title });
            if (subtitle != null) left.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap });
            return Row(left, right);
        }

        Grid Row(UIElement left, UIElement right)
        {
            Grid g = new Grid { Margin = new Thickness(14, 11, 14, 11) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(left);
            FrameworkElement r = (FrameworkElement)right;
            r.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(r, 1);
            g.Children.Add(r);
            return g;
        }

        TextBlock Small(string text)
        {
            return new TextBlock { Text = text, FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
        }

        TextBlock Link(string text, Action click, double size = 13)
        {
            TextBlock t = new TextBlock { Text = text, FontSize = size, Foreground = theme.Link, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            t.MouseLeftButtonUp += (s, e) => click();
            return t;
        }

        static BitmapSource LoadPng(string resource)
        {
            using (Stream s = typeof(Panel).Assembly.GetManifestResourceStream(resource))
            {
                BitmapImage img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = s;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        // QR dibujado como vectores: queda nitido con cualquier escala de pantalla.
        static ImageSource QrImage(string text)
        {
            QrCode qr = QrCode.Encode(text);
            GeometryGroup modules = new GeometryGroup();
            for (int y = 0; y < qr.Size; y++)
                for (int x = 0; x < qr.Size; x++)
                    if (qr[x, y]) modules.Children.Add(new RectangleGeometry(new Rect(x, y, 1, 1)));
            DrawingGroup d = new DrawingGroup();
            d.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(-2, -2, qr.Size + 4, qr.Size + 4))));
            d.Children.Add(new GeometryDrawing(Brushes.Black, null, modules));
            DrawingImage img = new DrawingImage(d);
            img.Freeze();
            return img;
        }
    }
}
