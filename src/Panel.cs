using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AutoReady
{
    enum StatusKind { Idle, Watching, Paused }

    // Lo que el panel muestra; lo arma TrayApp cada vez que algo cambia.
    class PanelStatus
    {
        public string Text;            // "Esperando partida en Dota 2"
        public StatusKind Kind;        // color del punto de estado
        public bool Paused;
        public string UpdateTag;       // "v0.2.3" si hay version nueva, si no null
        public string UpdateMessage;   // resultado de "Buscar actualizaciones"
        public string Protection;      // estado de la proteccion en partida
        public GameProfile ProtectionFix;   // juego al que le falta la opcion de lanzamiento (null = todo bien)
    }

    // Lo que el panel puede pedirle a la app. Cada cambio se aplica al instante (sin boton Guardar).
    interface IPanelHost
    {
        Config Cfg { get; }
        void SetPaused(bool paused);
        void SetPhone(bool on);
        void FixProtection(GameProfile game);
        // Opciones de lanzamiento actuales del juego + la que falta, listas para reemplazar sin perder nada.
        string LaunchOptionsWithProtection(GameProfile game);
        void SendTestPush(Action<bool> done);
        void SetAutostart(bool on);
        bool AutostartEnabled { get; }
        void SetAutoUpdate(bool on);
        void CheckUpdatesNow();
        void InstallUpdate();
        string HotkeyText { get; }
        // Registra el atajo global (vk = 0 lo borra). Devuelve false si otra app ya usa esa combinacion.
        bool SetHotkey(int vk, int mods);
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
        ClickArea updateButton;
        TextBlock updateButtonText;
        StackPanel phoneDetails, moreBox;
        ClickArea phoneShowCode;
        Border welcomeCard, protectionCard;
        TextBlock hotkeyLabel, hotkeyHint, protectionText, moreChevron;
        GameProfile protectionGame;
        bool awaitingUser;      // el usuario toco "Configurar" y esta haciendo el paso en Steam
        bool capturingHotkey;
        DateTime hiddenAt = DateTime.MinValue;
        Rect workArea;          // area de trabajo del monitor donde se abrio (en DIPs)
        bool anchoredBottom;    // pegado a la barra de tareas de abajo: si crece, crece hacia arriba

        public event Action Hidden;

        public Panel(IPanelHost host)
        {
            this.host = host;
            theme = Theme.FromSystem();

            Title = "AutoReady";
            Width = 360;
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

            // Si la pantalla es baja, el contenido scrollea en vez de cortarse (MaxHeight se ajusta al abrir).
            Content = new ScrollViewer { Content = Build(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

            SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int round = DWMWCP_ROUND, dark = theme.Dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);   // esquinas redondeadas de Windows 11
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            };
            Deactivated += (s, e) => { StopHotkeyCapture(); HidePanel(); };
            // Al abrir "Mas opciones" o el QR el panel cambia de alto: se reacomoda para no salirse de la pantalla.
            SizeChanged += (s, e) => { if (IsVisible && e.HeightChanged) KeepOnScreen(); };
            PreviewKeyDown += OnPreviewKeyDown;
        }

        // ---------- atajo de teclado ----------

        void StartHotkeyCapture()
        {
            capturingHotkey = true;
            hotkeyLabel.Text = S.HotkeyPress;
            Hint(S.HotkeyPressHint);
        }

        void StopHotkeyCapture()
        {
            if (!capturingHotkey) return;
            capturingHotkey = false;
            hotkeyLabel.Text = host.HotkeyText;
            Hint(null);
        }

        // Aviso debajo de la tecla de pausa; sin mensaje no ocupa lugar.
        void Hint(string text)
        {
            hotkeyHint.Text = text ?? "";
            hotkeyHint.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
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
            capturingHotkey = false;
            // Una tecla comun sin Ctrl/Alt/Shift quedaria tomada en todo Windows (dejaria de escribirse). Solo F1-F24 y
            // Pausa pueden ir solas.
            bool special = (vk >= 0x70 && vk <= 0x87) || vk == 0x13;
            if (vk != 0 && mods == 0 && !special)
            {
                hotkeyLabel.Text = host.HotkeyText;
                Hint(S.HotkeyNeedsModifier);
                return;
            }
            bool ok = host.SetHotkey(vk, mods);
            hotkeyLabel.Text = host.HotkeyText;
            Hint(ok ? null : S.HotkeyTaken);
        }

        // ---------- mostrar / ocultar ----------

        // Click en el icono: abre o cierra. Si se acaba de cerrar por ese mismo click (perdio el foco), no lo reabre.
        public void TogglePanel()
        {
            if (IsVisible) { HidePanel(); return; }
            if ((DateTime.Now - hiddenAt).TotalMilliseconds < 300) return;
            ShowPanel();
        }

        public void ShowWelcome() { welcomeCard.Visibility = Visibility.Visible; }

        // Se abre junto al mouse (que acaba de tocar el icono de la bandeja), dentro del area de trabajo de ESE monitor.
        public void ShowPanel()
        {
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            System.Drawing.Rectangle wa = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
            Show();
            PresentationSource src = PresentationSource.FromVisual(this);
            Matrix toDip = src != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
            Point tl = toDip.Transform(new Point(wa.Left, wa.Top)), br = toDip.Transform(new Point(wa.Right, wa.Bottom));
            Point c = toDip.Transform(new Point(cursor.X, cursor.Y));
            workArea = new Rect(tl, br);
            MaxHeight = Math.Max(300, workArea.Height - 24);
            UpdateLayout();
            Left = Math.Max(tl.X + 12, Math.Min(br.X - ActualWidth - 12, c.X - ActualWidth / 2));
            // Barra de tareas abajo (lo comun): el panel arriba de ella. Si el mouse esta en la mitad de arriba, abajo del mouse.
            anchoredBottom = c.Y > (tl.Y + br.Y) / 2;
            KeepOnScreen();
            Activate();   // sin foco en un control: el marco de foco aparece recien cuando se usa Tab
        }

        void KeepOnScreen()
        {
            if (workArea.IsEmpty) return;
            Top = anchoredBottom ? Math.Max(workArea.Top + 12, workArea.Bottom - ActualHeight - 12) : workArea.Top + 12;
        }

        void HidePanel()
        {
            if (!IsVisible) return;
            hiddenAt = DateTime.Now;
            Hide();
            Action h = Hidden;
            if (h != null) h();
        }

        public void Refresh(PanelStatus st)
        {
            statusText.Text = st.Text;
            statusDot.Fill = st.Kind == StatusKind.Watching ? theme.Good : st.Kind == StatusKind.Paused ? theme.Paused : theme.Idle;
            acceptToggle.IsOn = !st.Paused;

            bool hasUpdate = st.UpdateTag != null;
            updateButton.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
            if (hasUpdate)
            {
                updateButtonText.Text = S.UpdateTo(st.UpdateTag);
                AutomationProperties.SetName(updateButton, updateButtonText.Text);
            }
            updateText.Text = S.Version(Updater.CurrentVersionText, st.UpdateMessage);

            // Si el usuario ya toco "Configurar" y todavia no termino, no se pisa la instruccion.
            if (st.ProtectionFix != protectionGame) awaitingUser = false;
            protectionGame = st.ProtectionFix;
            // Solo se muestra cuando falta algo: si esta todo bien no hay nada que decir.
            protectionCard.Visibility = st.ProtectionFix != null ? Visibility.Visible : Visibility.Collapsed;
            if (!awaitingUser) protectionText.Text = st.Protection;
        }

        // ---------- construccion ----------

        // Lo que se usa siempre queda a la vista; lo que se configura una vez va en "Mas opciones".
        UIElement Build()
        {
            StackPanel root = new StackPanel { Margin = new Thickness(16) };

            // Encabezado: logo, nombre, estado y el interruptor principal (pausar / reanudar).
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Image logo = new Image { Source = LoadPng("AutoReady.logo-64.png"), Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            header.Children.Add(logo);
            StackPanel titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "AutoReady", FontSize = 16, FontWeight = FontWeights.SemiBold });
            DockPanel statusLine = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };   // DockPanel: el texto largo baja de renglon
            statusDot = new Ellipse { Width = 8, Height = 8, Fill = theme.Idle, Margin = new Thickness(0, 5, 6, 0), VerticalAlignment = VerticalAlignment.Top };
            statusText = new TextBlock { Text = S.Starting, FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(statusText, AutomationLiveSetting.Polite);   // el lector anuncia los cambios de estado
            statusLine.Children.Add(statusDot);
            statusLine.Children.Add(statusText);
            titles.Children.Add(statusLine);
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);
            acceptToggle = new Toggle(theme, true, S.AcceptTitle) { Margin = new Thickness(12, 0, 0, 0) };
            acceptToggle.ToolTip = S.AcceptTitle;
            acceptToggle.Changed += on => host.SetPaused(!on);
            Grid.SetColumn(acceptToggle, 2);
            header.Children.Add(acceptToggle);
            root.Children.Add(header);

            // Bienvenida: solo la primera vez.
            StackPanel welcome = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            welcome.Children.Add(new TextBlock { Text = S.WelcomeTitle, FontWeight = FontWeights.SemiBold });
            welcome.Children.Add(new TextBlock { Text = S.WelcomeText, FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
            welcome.Children.Add(Link(S.WelcomeDismiss, () => welcomeCard.Visibility = Visibility.Collapsed, 13));
            welcomeCard = Wrap(welcome, new Thickness(0, 14, 0, 0));
            welcomeCard.Visibility = Visibility.Collapsed;
            root.Children.Add(welcomeCard);

            // Proteccion en partida: aparece solo si a un juego le falta un paso (mientras tanto, modo seguro).
            protectionText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            ClickArea protectionButton = new ClickArea(theme,
                new TextBlock { Text = S.ProtectionButton, Foreground = theme.OnFill, FontSize = 13, FontWeight = FontWeights.SemiBold },
                SetUpProtection, S.ProtectionButton)
            {
                Background = theme.Fill, CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center,
            };
            StackPanel prot = new StackPanel();
            prot.Children.Add(Row(protectionText, protectionButton));
            protectionCard = Wrap(prot, new Thickness(0, 14, 0, 0));
            protectionCard.BorderBrush = theme.Paused;
            protectionCard.Visibility = Visibility.Collapsed;
            root.Children.Add(protectionCard);

            // Boton de actualizacion: solo aparece si hay version nueva.
            updateButtonText = new TextBlock { Foreground = theme.OnFill, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            updateButton = new ClickArea(theme, updateButtonText, () => host.InstallUpdate(), S.UpdateTo(""))
            {
                Background = theme.Fill, CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed,
            };
            root.Children.Add(updateButton);

            // Lo de todos los dias: tecla de pausa, sonido y celular.
            StackPanel main = new StackPanel();
            hotkeyLabel = new TextBlock { Text = host.HotkeyText, FontSize = 13 };
            ClickArea hotkeyButton = new ClickArea(theme, hotkeyLabel, StartHotkeyCapture, S.HotkeyTitle)
            {
                BorderBrush = theme.ControlStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center,
            };
            StackPanel hotkeyLeft = Texts(S.HotkeyTitle, "");
            hotkeyHint = (TextBlock)hotkeyLeft.Children[1];
            hotkeyHint.Visibility = Visibility.Collapsed;
            main.Children.Add(Row(hotkeyLeft, hotkeyButton));
            main.Children.Add(Divider());

            Toggle sound = new Toggle(theme, host.Cfg.Sound, S.Sound);
            sound.Changed += on => { host.Cfg.Sound = on; host.Cfg.Save(); };
            main.Children.Add(Row(Texts(S.Sound, null), sound));
            main.Children.Add(Divider());

            // Celular: el QR se muestra hasta que la prueba llega una vez; despues queda a un click.
            phoneToggle = new Toggle(theme, host.Cfg.NotifyPhone, S.PhoneTitle);
            phoneShowCode = Link(S.PhoneShowCode, () => ShowPhoneDetails(true), 12);
            phoneShowCode.Margin = new Thickness(0, 2, 0, 0);
            StackPanel phoneLeft = Texts(S.PhoneTitle, null);
            phoneLeft.Children.Add(phoneShowCode);
            main.Children.Add(Row(phoneLeft, phoneToggle));
            phoneDetails = BuildPhoneDetails();
            main.Children.Add(phoneDetails);
            phoneToggle.Changed += on => { host.SetPhone(on); ShowPhoneDetails(on); };
            ShowPhoneDetails(host.Cfg.NotifyPhone && !host.Cfg.PhoneTested);
            root.Children.Add(Wrap(main, new Thickness(0, 14, 0, 0)));

            // Mas opciones: se configuran una vez (ya se eligieron al instalar), cerradas por defecto.
            moreChevron = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = theme.TextSecondary };
            StackPanel moreHead = new StackPanel { Orientation = Orientation.Horizontal };
            moreHead.Children.Add(moreChevron);
            moreHead.Children.Add(new TextBlock { Text = S.MoreSettings, FontSize = 13, Foreground = theme.TextSecondary });
            ClickArea moreButton = new ClickArea(theme, moreHead, ToggleMore, S.MoreSettings)
            {
                Margin = new Thickness(2, 14, 0, 0), Padding = new Thickness(0, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Left,
            };
            root.Children.Add(moreButton);

            StackPanel general = new StackPanel();
            Toggle autostart = new Toggle(theme, host.AutostartEnabled, S.StartWithWindows);
            autostart.Changed += on => host.SetAutostart(on);
            general.Children.Add(Row(Texts(S.StartWithWindows, null), autostart));
            general.Children.Add(Divider());
            Toggle autoUpdate = new Toggle(theme, host.Cfg.AutoUpdateCheck, S.AutoUpdate);
            autoUpdate.Changed += on => host.SetAutoUpdate(on);
            general.Children.Add(Row(Texts(S.AutoUpdate, null), autoUpdate));
            general.Children.Add(Divider());
            updateText = new TextBlock { FontSize = 12, Foreground = theme.TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            general.Children.Add(Row(updateText, Link(S.CheckUpdates, () => host.CheckUpdatesNow(), 12)));
            moreBox = new StackPanel { Visibility = Visibility.Collapsed };
            moreBox.Children.Add(Wrap(general, new Thickness(0, 8, 0, 0)));
            root.Children.Add(moreBox);

            // Pie: el wordmark de keis.studio con la k en tres colores; a este tamano el corrimiento va exagerado
            // (como mark-*-small de la guia) para que los colores se vean nitidos.
            Image brand = new Image { Source = KeisMark.BuildWordmark(theme.Dark, true, 2.2), Height = 18, ToolTip = "keis.studio" };
            root.Children.Add(new ClickArea(theme, brand, () => System.Diagnostics.Process.Start("https://keis.studio"), "keis.studio")
            {
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 2),
            });

            return root;
        }

        void ToggleMore()
        {
            bool open = moreBox.Visibility != Visibility.Visible;
            moreBox.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            moreChevron.Text = open ? "" : "";   // chevron abajo / derecha
        }

        // QR y codigo a la vista, o colapsados con un link "Mostrar codigo" (solo si el celular esta activado).
        void ShowPhoneDetails(bool open)
        {
            bool on = phoneToggle.IsOn;
            phoneDetails.Visibility = on && open ? Visibility.Visible : Visibility.Collapsed;
            phoneShowCode.Visibility = on && !open ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetUpProtection()
        {
            if (protectionGame == null) return;
            // Se copian las opciones que el jugador ya tenia + la nueva: reemplazando todo no pierde las suyas.
            string options = host.LaunchOptionsWithProtection(protectionGame);
            bool hadOwn = options != protectionGame.GsiLaunchOption;
            try { Clipboard.SetText(options); }
            catch { }   // portapapeles ocupado por otra app: igual se abre Steam
            host.FixProtection(protectionGame);
            awaitingUser = true;
            protectionText.Text = hadOwn ? S.ProtectionCopiedOwn(protectionGame.Name) : S.ProtectionCopiedNew(protectionGame.Name);
        }

        StackPanel BuildPhoneDetails()
        {
            StackPanel p = new StackPanel { Margin = new Thickness(14, 2, 14, 14) };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            // Un solo QR para cualquier celular: la camara abre https://ntfy.sh/<codigo>, que ya queda suscripto.
            Border qrBox = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(5), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            qrBox.Child = new Image { Source = QrImage("https://ntfy.sh/" + host.Cfg.NtfyTopic), Width = 96, Height = 96 };
            AutomationProperties.SetName(qrBox, S.PhoneScan);
            g.Children.Add(qrBox);

            StackPanel right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock { Text = S.PhoneScan, FontSize = 13, FontWeight = FontWeights.SemiBold });
            right.Children.Add(Small(S.PhoneOrCode));

            // El codigo a la vista y seleccionable: se lee de una para escribirlo en el celular.
            TextBox code = new TextBox
            {
                Text = host.Cfg.NtfyTopic, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = theme.Text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 17,
                FontWeight = FontWeights.SemiBold, Padding = new Thickness(0), Margin = new Thickness(0, 2, 0, 0), CaretBrush = Brushes.Transparent,
            };
            AutomationProperties.SetName(code, S.PhoneOrCode);
            right.Children.Add(code);

            TextBlock testText = new TextBlock { Text = S.PhoneTest, FontSize = 12 };
            ClickArea test = null;
            test = new ClickArea(theme, testText, () =>
            {
                testText.Text = S.PhoneSending;
                host.SendTestPush(ok => Dispatcher.BeginInvoke(new Action(() =>
                {
                    testText.Text = ok ? S.PhoneSent : S.PhoneFailed;
                    if (ok && !host.Cfg.PhoneTested) { host.Cfg.PhoneTested = true; host.Cfg.Save(); }
                    System.Windows.Threading.DispatcherTimer back = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    back.Tick += (s2, e2) => { back.Stop(); testText.Text = S.PhoneTest; };
                    back.Start();
                })));
            }, S.PhoneTest)
            {
                BorderBrush = theme.ControlStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            };
            right.Children.Add(test);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            p.Children.Add(g);
            TextBlock secret = Small(S.PhoneSecret);
            secret.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(secret);
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

        // Titulo y subtitulo de un renglon (el subtitulo es el segundo hijo, para poder cambiarlo despues).
        StackPanel Texts(string title, string subtitle)
        {
            StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
            if (subtitle != null) left.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap });
            return left;
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

        ClickArea Link(string text, Action click, double size)
        {
            return new ClickArea(theme, new TextBlock { Text = text, FontSize = size, Foreground = theme.Link, TextDecorations = TextDecorations.Underline },
                                 click, text) { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
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
