using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
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
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWCP_ROUND = 2;

        const string PhonePageUrl = "https://hercase.github.io/autoready/phone/";

        readonly IPanelHost host;
        readonly Theme theme;
        TextBlock statusText, updateText;
        Ellipse statusDot;
        Toggle acceptToggle, phoneToggle;
        StackPanel phoneDetails, moreBox;
        ClickArea phoneShowCode, hotkeyButton, updateButton, moreButton;
        InfoBar welcomeBar, protectionBar, updateBar;
        TextBlock hotkeyLabel, hotkeyHint, moreChevron;
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
            BorderThickness = new Thickness(0);   // el borde lo dibuja DWM (ver abajo), asi no se corta en las esquinas redondeadas
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            FontSize = 14;
            Foreground = theme.Text;
            UseLayoutRounding = true;

            // El pie queda fijo abajo; si la pantalla es baja, solo el contenido scrollea (MaxHeight se ajusta al abrir).
            DockPanel layout = new DockPanel { LastChildFill = true };
            Border footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            layout.Children.Add(footer);
            layout.Children.Add(new ScrollViewer { Content = Build(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
            Content = layout;

            SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int round = DWMWCP_ROUND, dark = theme.Dark ? 1 : 0;
                int border = theme.Dark ? 0x3A3A3A : 0xD0D0D0;   // COLORREF (BGR): con grises da igual el orden
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);   // esquinas redondeadas de Windows 11
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
                // Windows 10 no tiene color de borde de DWM: ahi lo dibuja WPF.
                if (DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4) != 0) BorderThickness = new Thickness(1);
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
            SetHotkeyLabel(S.HotkeyPress);
            hotkeyButton.BorderBrush = theme.Fill;
            hotkeyButton.BorderThickness = new Thickness(1, 1, 1, 2);
            Hint(S.HotkeyPressHint);
        }

        void StopHotkeyCapture()
        {
            if (!capturingHotkey) return;
            capturingHotkey = false;
            SetHotkeyLabel(host.HotkeyText);
            EndCaptureLook();
            Hint(null);
        }

        void EndCaptureLook()
        {
            hotkeyButton.BorderBrush = theme.ButtonStroke;
            hotkeyButton.BorderThickness = new Thickness(1);
        }

        void SetHotkeyLabel(string text)
        {
            hotkeyLabel.Text = text;
            AutomationProperties.SetName(hotkeyButton, S.HotkeyTitle + ", " + text);
        }

        // Aviso debajo de la tecla de pausa; sin mensaje no ocupa lugar. El lector de pantalla lo anuncia.
        void Hint(string text)
        {
            hotkeyHint.Text = text ?? "";
            hotkeyHint.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            if (text != null) Announce(hotkeyHint);
        }

        // Region "viva": WPF solo la lee en voz alta si se avisa el cambio.
        static void Announce(UIElement e)
        {
            AutomationPeer peer = UIElementAutomationPeer.FromElement(e) ?? UIElementAutomationPeer.CreatePeerForElement(e);
            if (peer != null) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
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
            EndCaptureLook();
            // Una tecla comun sin Ctrl/Alt/Shift quedaria tomada en todo Windows (dejaria de escribirse). Solo F1-F24 y
            // Pausa pueden ir solas.
            bool special = (vk >= 0x70 && vk <= 0x87) || vk == 0x13;
            if (vk != 0 && mods == 0 && !special)
            {
                SetHotkeyLabel(host.HotkeyText);
                Hint(S.HotkeyNeedsModifier);
                return;
            }
            bool ok = host.SetHotkey(vk, mods);
            SetHotkeyLabel(host.HotkeyText);
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

        public void ShowWelcome() { welcomeBar.Visibility = Visibility.Visible; }

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
            if (statusText.Text != st.Text) { statusText.Text = st.Text; Announce(statusText); }
            statusDot.Fill = st.Kind == StatusKind.Watching ? theme.Good : st.Kind == StatusKind.Paused ? theme.Paused : theme.Idle;
            acceptToggle.IsOn = !st.Paused;

            bool hasUpdate = st.UpdateTag != null;
            updateBar.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
            if (hasUpdate)
            {
                // El resultado de tocar Actualizar se ve en el mismo aviso (no escondido en Mas opciones).
                updateBar.Title.Text = S.UpdateAvailable(st.UpdateTag);
                bool busy = st.UpdateMessage == S.Updating || st.UpdateMessage == S.UpdateAfterMatch;
                bool show = busy || st.UpdateMessage == S.UpdateFailed;
                updateBar.Message.Text = show ? st.UpdateMessage : "";
                updateBar.Message.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                updateButton.Enabled = !busy;
            }
            updateText.Text = S.Version(Updater.CurrentVersionText, st.UpdateMessage);

            // Si el usuario ya toco "Configurar" y todavia no termino, no se pisa la instruccion.
            if (st.ProtectionFix != protectionGame) awaitingUser = false;
            protectionGame = st.ProtectionFix;
            // Solo se muestra cuando falta algo: si esta todo bien no hay nada que decir.
            protectionBar.Visibility = st.ProtectionFix != null ? Visibility.Visible : Visibility.Collapsed;
            if (!awaitingUser && st.ProtectionFix != null) protectionBar.Message.Text = S.ProtectionMissing(st.ProtectionFix.Name);
        }

        // ---------- construccion ----------

        // Lo que se usa siempre queda a la vista; lo que se configura una vez va en "Mas opciones".
        // Medidas de Windows 11: grilla de 4 px, renglones de 48, 12 entre el encabezado y lo demas, 8 entre bloques.
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
            statusDot = new Ellipse { Width = 8, Height = 8, Fill = theme.Idle, Margin = new Thickness(0, 6, 6, 0), VerticalAlignment = VerticalAlignment.Top };
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

            // Avisos (mismo componente): bienvenida la primera vez, el paso que le falta a un juego, version nueva.
            StackPanel bars = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            welcomeBar = new InfoBar(theme, false, "", S.WelcomeTitle, S.WelcomeText, null,
                () => welcomeBar.Visibility = Visibility.Collapsed) { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            bars.Children.Add(welcomeBar);
            protectionBar = new InfoBar(theme, true, "", null, "", ClickArea.Make(theme, S.ProtectionButton, SetUpProtection, ButtonKind.Accent), null)
                { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            bars.Children.Add(protectionBar);
            updateButton = ClickArea.Make(theme, S.UpdateButton, () => host.InstallUpdate(), ButtonKind.Accent);
            updateBar = new InfoBar(theme, false, "", "", "", updateButton, null)
                { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            bars.Children.Add(updateBar);
            root.Children.Add(bars);

            // Lo de todos los dias: tecla de pausa, sonido y celular.
            StackPanel main = new StackPanel();
            hotkeyButton = ClickArea.Make(theme, host.HotkeyText, StartHotkeyCapture, ButtonKind.Standard);
            hotkeyButton.MinWidth = 56;
            AutomationProperties.SetName(hotkeyButton, S.HotkeyTitle + ", " + host.HotkeyText);
            hotkeyLabel = (TextBlock)hotkeyButton.Child;
            StackPanel hotkeyLeft = Texts(S.HotkeyTitle, "");
            hotkeyHint = (TextBlock)hotkeyLeft.Children[1];
            hotkeyHint.Visibility = Visibility.Collapsed;
            main.Children.Add(Row(hotkeyLeft, hotkeyButton));
            main.Children.Add(Divider());

            Toggle sound = new Toggle(theme, host.Cfg.Sound, S.Sound);
            sound.Changed += on => { host.Cfg.Sound = on; host.Cfg.Save(); };
            main.Children.Add(Row(Texts(S.Sound, null), sound));
            main.Children.Add(Divider());

            // Celular: el QR se muestra hasta que la prueba llega una vez; despues queda a un click (Mostrar/Ocultar codigo).
            phoneToggle = new Toggle(theme, host.Cfg.NotifyPhone, S.PhoneTitle);
            phoneShowCode = ClickArea.Make(theme, S.PhoneShowCode, () => ShowPhoneDetails(phoneDetails.Visibility != Visibility.Visible), ButtonKind.Link, 12);
            phoneShowCode.Expanded = () => phoneDetails.Visibility == Visibility.Visible;
            phoneShowCode.Padding = new Thickness(0, 4, 8, 4);   // area para tocar de al menos 24 px
            phoneShowCode.Margin = new Thickness(0, -2, 0, -4);
            StackPanel phoneLeft = Texts(S.PhoneTitle, null);
            phoneLeft.Children.Add(phoneShowCode);
            main.Children.Add(Row(phoneLeft, phoneToggle));
            phoneDetails = BuildPhoneDetails();
            main.Children.Add(phoneDetails);
            phoneToggle.Changed += on => { host.SetPhone(on); ShowPhoneDetails(on); };
            ShowPhoneDetails(host.Cfg.NotifyPhone && !host.Cfg.PhoneTested);
            root.Children.Add(Wrap(main, new Thickness(0, 12, 0, 0)));

            // Mas opciones: una tarjeta que se despliega (como SettingsExpander); cerrada por defecto.
            StackPanel more = new StackPanel();
            moreChevron = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Foreground = theme.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
            Grid moreHead = Row(Texts(S.MoreSettings, null), moreChevron);
            moreButton = new ClickArea(theme, moreHead, ToggleMore, S.MoreSettings) { CornerRadius = new CornerRadius(7) };
            moreButton.Expanded = () => moreBox.Visibility == Visibility.Visible;
            moreButton.SetBackgrounds(Brushes.Transparent, theme.SubtleHover, theme.SubtlePressed);
            more.Children.Add(moreButton);
            moreBox = new StackPanel { Visibility = Visibility.Collapsed };
            moreBox.Children.Add(Divider());
            Toggle autostart = new Toggle(theme, host.AutostartEnabled, S.StartWithWindows);
            autostart.Changed += on => host.SetAutostart(on);
            moreBox.Children.Add(Row(Texts(S.StartWithWindows, null), autostart));
            moreBox.Children.Add(Divider());
            Toggle autoUpdate = new Toggle(theme, host.Cfg.AutoUpdateCheck, S.AutoUpdate);
            autoUpdate.Changed += on => host.SetAutoUpdate(on);
            moreBox.Children.Add(Row(Texts(S.AutoUpdate, null), autoUpdate));
            moreBox.Children.Add(Divider());
            updateText = new TextBlock { FontSize = 12, Foreground = theme.TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            ClickArea check = ClickArea.Make(theme, S.CheckUpdates, () => host.CheckUpdatesNow(), ButtonKind.Standard);
            AutomationProperties.SetName(check, S.CheckUpdatesName);
            moreBox.Children.Add(Row(updateText, check));
            more.Children.Add(moreBox);
            root.Children.Add(Wrap(more, new Thickness(0, 8, 0, 0)));

            return root;
        }

        // Pie: franja un tono mas oscura con linea arriba (como Configuracion rapida de Windows 11), de borde a borde,
        // con el wordmark de keis.studio centrado. La k en tres colores necesita 18 px y fondo neutro para verse nitida.
        Border BuildFooter()
        {
            Image brand = new Image { Source = KeisMark.BuildWordmark(theme.Dark, true, 2.2), Height = 18, ToolTip = "keis.studio" };
            ClickArea link = new ClickArea(theme, brand, () => System.Diagnostics.Process.Start("https://keis.studio"), "keis.studio")
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(4),
            };
            link.SetBackgrounds(Brushes.Transparent, theme.SubtleHover, theme.SubtlePressed);
            return new Border
            {
                Height = 48, BorderThickness = new Thickness(0, 1, 0, 0), Child = link,
                Background = new SolidColorBrush(theme.Dark ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xEE, 0xEE, 0xEE)),
                BorderBrush = new SolidColorBrush(theme.Dark ? Color.FromRgb(0x2E, 0x2E, 0x2E) : Color.FromRgb(0xE5, 0xE5, 0xE5)),
            };
        }

        void ToggleMore()
        {
            bool open = moreBox.Visibility != Visibility.Visible;
            moreButton.RaiseExpanded(!open, open);
            moreBox.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            moreChevron.Text = open ? "" : "";   // chevron arriba / abajo
        }

        // QR y codigo a la vista o colapsados; el link de arriba alterna entre los dos (solo si el celular esta activado).
        void ShowPhoneDetails(bool open)
        {
            bool on = phoneToggle.IsOn;
            bool was = phoneDetails.Visibility == Visibility.Visible;
            open = on && open;
            phoneDetails.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            phoneShowCode.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            string label = open ? S.PhoneHideCode : S.PhoneShowCode;
            ((TextBlock)phoneShowCode.Child).Text = label;
            AutomationProperties.SetName(phoneShowCode, label);
            if (was != open) phoneShowCode.RaiseExpanded(was, open);
        }

        void SetUpProtection()
        {
            if (protectionGame == null) return;
            // Se copian las opciones que el jugador ya tenia + la nueva: reemplazando todo no pierde las suyas.
            string options = host.LaunchOptionsWithProtection(protectionGame);
            try { Clipboard.SetText(options); }
            catch { }   // portapapeles ocupado por otra app: igual se abre Steam
            host.FixProtection(protectionGame);
            awaitingUser = true;
            protectionBar.Message.Text = S.ProtectionCopied(protectionGame.Name);
        }

        StackPanel BuildPhoneDetails()
        {
            StackPanel p = new StackPanel { Margin = new Thickness(16, 0, 16, 16) };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            // Un solo QR para cualquier celular: abre la pagina de ayuda (docs/phone), que en Android suscribe ntfy de un toque
            // y en iPhone guia a copiar y pegar el codigo. El codigo va despues del #, asi nunca llega a un servidor.
            // Con un servidor de ntfy propio, el link directo a ese servidor.
            string qrUrl = host.Cfg.NtfyServer.TrimEnd('/') == "https://ntfy.sh"
                ? PhonePageUrl + (Lang.Spanish ? "?lang=es" : "?lang=en") + "#" + host.Cfg.NtfyTopic
                : host.Cfg.NtfyServer.TrimEnd('/') + "/" + host.Cfg.NtfyTopic;
            Border qrBox = new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(4), Padding = new Thickness(6), Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Top, BorderBrush = theme.Dark ? Brushes.White : theme.CardStroke, BorderThickness = new Thickness(1),
            };
            qrBox.Child = new Image { Source = QrImage(qrUrl), Width = 96, Height = 96 };
            AutomationProperties.SetName(qrBox, S.PhoneScan);
            g.Children.Add(qrBox);

            StackPanel right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock { Text = S.PhoneScan, FontWeight = FontWeights.SemiBold });
            right.Children.Add(Small(S.PhoneOrCode));

            // El codigo a la vista y seleccionable: se lee de una para escribirlo en el celular.
            TextBox code = new TextBox
            {
                Text = host.Cfg.NtfyTopic, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = theme.Text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 15,
                Padding = new Thickness(-2, 0, 0, 0), Margin = new Thickness(0, 4, 0, 0), CaretBrush = Brushes.Transparent,
            };
            AutomationProperties.SetName(code, S.PhoneOrCode);
            // Boton de copiar al lado (por si se lo manda al celular por chat): el icono pasa a un tilde un momento.
            TextBlock copyIcon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ClickArea copy = null;
            copy = new ClickArea(theme, copyIcon, () =>
            {
                try { Clipboard.SetText(host.Cfg.NtfyTopic); } catch { return; }
                copyIcon.Text = "";
                System.Windows.Threading.DispatcherTimer back = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                back.Tick += (s2, e2) => { back.Stop(); copyIcon.Text = ""; };
                back.Start();
            }, S.CopyCode) { Width = 26, Height = 26, CornerRadius = new CornerRadius(4), Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = S.CopyCode };
            copy.SetBackgrounds(Brushes.Transparent, theme.SubtleHover, theme.SubtlePressed);
            copy.SetLabelColors(copyIcon, theme.TextSecondary, theme.Text, theme.TextSecondary);
            StackPanel codeRow = new StackPanel { Orientation = Orientation.Horizontal };
            code.VerticalAlignment = VerticalAlignment.Center;
            codeRow.Children.Add(code);
            codeRow.Children.Add(copy);
            right.Children.Add(codeRow);

            TextBlock testText = null;
            TextBlock result = Small("");
            // Si la prueba llego, el QR ya no hace falta: se oculta en el momento (y en las proximas aperturas).
            ClickArea done = ClickArea.Make(theme, S.PhoneDone, () => { ShowPhoneDetails(false); phoneShowCode.Focus(); }, ButtonKind.Link, 12);
            done.Padding = new Thickness(0, 4, 8, 4);
            done.Visibility = Visibility.Collapsed;
            result.Visibility = Visibility.Collapsed;
            result.Margin = new Thickness(0, 6, 0, 0);
            AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite);
            ClickArea test = null;
            test = ClickArea.Make(theme, S.PhoneTest, () =>
            {
                testText.Text = S.PhoneSending;
                test.Enabled = false;
                result.Visibility = Visibility.Collapsed;
                done.Visibility = Visibility.Collapsed;
                host.SendTestPush(ok => Dispatcher.BeginInvoke(new Action(() =>
                {
                    testText.Text = S.PhoneTest;
                    test.Enabled = true;
                    result.Text = ok ? S.PhoneSent : S.PhoneFailed;
                    result.Foreground = ok ? theme.TextSecondary : theme.Text;
                    result.Visibility = Visibility.Visible;
                    done.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
                    Announce(result);
                    if (ok && !host.Cfg.PhoneTested) { host.Cfg.PhoneTested = true; host.Cfg.Save(); }
                })));
            }, ButtonKind.Standard);
            testText = (TextBlock)test.Child;
            test.Margin = new Thickness(0, 12, 0, 0);
            test.HorizontalAlignment = HorizontalAlignment.Left;
            right.Children.Add(test);
            right.Children.Add(result);
            right.Children.Add(done);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            p.Children.Add(g);
            TextBlock secret = Small(S.PhoneSecret);
            secret.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(secret);
            return p;
        }

        // ---------- piezas ----------

        Border Wrap(StackPanel content, Thickness margin)
        {
            return new Border { Background = theme.Card, BorderBrush = theme.CardStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = margin, Child = content };
        }

        Border Divider()
        {
            return new Border { Height = 1, Background = theme.Divider };
        }

        // Titulo y subtitulo de un renglon (el subtitulo es el segundo hijo, para poder cambiarlo despues).
        StackPanel Texts(string title, string subtitle)
        {
            StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
            if (subtitle != null) left.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap });
            return left;
        }

        // Renglon de Windows 11: al menos 48 de alto, 16 a los costados; el control a la derecha.
        Grid Row(UIElement left, UIElement right)
        {
            Grid g = new Grid { MinHeight = 32, Margin = new Thickness(16, 8, 16, 8) };   // 32 + 8 arriba y abajo = 48
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(left);
            FrameworkElement r = (FrameworkElement)right;
            r.Margin = new Thickness(12, 0, 0, 0);
            r.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(r, 1);
            g.Children.Add(r);
            return g;
        }

        TextBlock Small(string text)
        {
            return new TextBlock { Text = text, FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
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
