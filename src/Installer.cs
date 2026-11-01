using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace AutoReady
{
    // Instalador dentro del mismo exe: sin dependencias ni permisos de administrador.
    // Se instala por usuario en %LOCALAPPDATA%\Programs\AutoReady (como Discord o VS Code), con accesos directos,
    // inicio con Windows opcional y entrada en Configuracion > Aplicaciones con su desinstalador.
    static class Installer
    {
        public enum Choice { Installed, Cancel }

        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AutoReady";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\AutoReady"); }
        }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "AutoReady.exe"); } }

        public static bool IsInstalledCopy
        {
            get { return string.Equals(Path.GetFullPath(System.Windows.Forms.Application.ExecutablePath), InstalledExe, StringComparison.OrdinalIgnoreCase); }
        }

        // Un exe bajado siempre se instala (no hay modo portable). Las builds locales de desarrollo corren donde estan.
        public static bool ShouldOffer(string[] args)
        {
            return !IsInstalledCopy && !Updater.IsDevBuild;
        }

        public static Choice Offer()
        {
            InstallWindow w = new InstallWindow();
            w.ShowDialog();
            return w.Result;
        }

        public static void Install(bool desktop, bool autostart, bool updates, bool steamStatus)
        {
            string self = Path.GetFullPath(System.Windows.Forms.Application.ExecutablePath);
            CloseOtherInstances();
            Directory.CreateDirectory(InstallDir);
            File.Copy(self, InstalledExe, true);

            // Lo que se eligio en el instalador sobre conexiones (ver PRIVACY.md) queda guardado antes del primer arranque.
            Config cfg = Config.Load();
            cfg.AutoUpdateCheck = updates;
            cfg.UseSteamStatus = steamStatus;
            cfg.Save();

            Shortcut.Create(Shortcut.StartMenuPath, InstalledExe);
            if (desktop) Shortcut.Create(Shortcut.DesktopPath, InstalledExe);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (autostart) k.SetValue("AutoReady", "\"" + InstalledExe + "\"");
                else k.DeleteValue("AutoReady", false);
            }
            Register();
            Log.Write("Instalado en " + InstallDir + (desktop ? " (con acceso directo en el escritorio)" : ""));
            Process.Start(InstalledExe);
        }

        // Entrada en Configuracion > Aplicaciones. Se refresca al arrancar (la version cambia al actualizarse).
        public static void Register()
        {
            if (!File.Exists(InstalledExe)) return;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "AutoReady");
                k.SetValue("DisplayVersion", Updater.CurrentVersionText);
                k.SetValue("Publisher", "keis.studio");
                k.SetValue("DisplayIcon", InstalledExe);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + InstalledExe + "\" --uninstall --quiet");
                k.SetValue("URLInfoAbout", "https://hercase.github.io/autoready/");
                k.SetValue("HelpLink", "https://hercase.github.io/autoready/faq/");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
                if (k.GetValue("InstallDate") == null) k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }

        // Lo llama Configuracion > Aplicaciones (o winget con --quiet, sin preguntar y sin borrar la configuracion).
        public static int Uninstall(bool quiet)
        {
            if (quiet) { Remove(false); ScheduleFolderDelete(); return 0; }
            UninstallWindow w = new UninstallWindow();
            w.ShowDialog();
            if (w.Removed) ScheduleFolderDelete();
            return 0;
        }

        // Todo lo que dejo la instalacion: procesos, accesos directos, inicio con Windows, registro y la config de GSI
        // en las carpetas de los juegos. La configuracion propia (%APPDATA%) solo si se pidio.
        public static void Remove(bool deleteData)
        {
            CloseOtherInstances();
            TryDelete(Shortcut.StartMenuPath);
            if (string.Equals(Shortcut.Target(Shortcut.DesktopPath), InstalledExe, StringComparison.OrdinalIgnoreCase))
                TryDelete(Shortcut.DesktopPath);
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null) k.DeleteValue("AutoReady", false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\keis.AutoReady", false);
            Gsi.RemoveAllConfigs();
            Log.Write("Desinstalado" + (deleteData ? " (con la configuracion)" : ""));
            if (deleteData) { try { Directory.Delete(Config.Dir, true); } catch { } }
        }

        // El exe no se puede borrar a si mismo mientras corre: lo borra un cmd oculto cuando este proceso termine.
        static void ScheduleFolderDelete()
        {
            if (!IsInstalledCopy && !Directory.Exists(InstallDir)) return;
            Process.Start(new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"")
                { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false });
        }

        static void CloseOtherInstances()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("AutoReady"))
            {
                try { if (p.Id != me) { p.Kill(); p.WaitForExit(5000); } }
                catch { }
                finally { p.Dispose(); }
            }
        }

        static void TryDelete(string file) { try { if (File.Exists(file)) File.Delete(file); } catch { } }
    }

    // Base de las ventanas del instalador y el desinstalador, con el mismo estilo que el panel: encabezado con el logo,
    // una tarjeta y una franja abajo con los botones (dialogo de Windows 11).
    class SetupWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected readonly Theme theme = Theme.FromSystem();
        protected readonly StackPanel Body = new StackPanel { Margin = new Thickness(24) };
        protected readonly Grid Buttons = new Grid();
        protected TextBlock Subtitle;

        protected SetupWindow(string title, string subtitle)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = theme.Background;
            Foreground = theme.Text;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            FontSize = 14;
            UseLayoutRounding = true;
            SourceInitialized += (s, e) =>
            {
                int dark = theme.Dark ? 1 : 0;
                DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4);   // barra de titulo oscura
            };

            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            Image logo = new Image { Width = 48, Height = 48, Margin = new Thickness(0, 0, 16, 0) };
            using (Stream st = typeof(SetupWindow).Assembly.GetManifestResourceStream("AutoReady.logo-64.png"))
            {
                System.Windows.Media.Imaging.BitmapImage img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit(); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.StreamSource = st; img.EndInit();
                logo.Source = img;
            }
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            header.Children.Add(logo);
            StackPanel titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "AutoReady", FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
            Subtitle = new TextBlock { Text = subtitle, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(Subtitle, AutomationLiveSetting.Polite);
            titles.Children.Add(Subtitle);
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);
            Body.Children.Add(header);

            Buttons.ColumnDefinitions.Add(new ColumnDefinition());
            Buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border footer = new Border
            {
                Padding = new Thickness(24), BorderThickness = new Thickness(0, 1, 0, 0), Child = Buttons,
                Background = new SolidColorBrush(theme.Dark ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xEE, 0xEE, 0xEE)),
                BorderBrush = new SolidColorBrush(theme.Dark ? Color.FromRgb(0x2E, 0x2E, 0x2E) : Color.FromRgb(0xE5, 0xE5, 0xE5)),
            };

            StackPanel root = new StackPanel();
            root.Children.Add(Body);
            root.Children.Add(footer);
            Content = root;
        }

        // Columnas de la franja: 0 = a la izquierda (link), 1 y 2 = botones a la derecha.
        protected void AddButton(FrameworkElement b, int column)
        {
            if (column > 0) { b.MinWidth = 120; if (column == 2) b.Margin = new Thickness(8, 0, 0, 0); }
            Grid.SetColumn(b, column);
            Buttons.Children.Add(b);
        }

        // Trabajo de unos segundos en segundo plano, con el boton apagado y otro texto mientras tanto.
        protected void RunInBackground(ClickArea button, string busyText, Action work, Action<Exception> done)
        {
            TextBlock label = (TextBlock)button.Child;
            string text = label.Text;
            label.Text = busyText;
            button.Enabled = false;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                Exception error = null;
                try { work(); }
                catch (Exception ex) { error = ex; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    label.Text = text;
                    button.Enabled = true;
                    done(error);
                }));
            });
        }

        protected Border Divider() { return new Border { Height = 1, Background = theme.Divider }; }

        protected Border Card(StackPanel content)
        {
            return new Border { Background = theme.Card, BorderBrush = theme.CardStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = content };
        }

        protected Grid Row(string title, UIElement right, string subtitle = null)
        {
            Grid g = new Grid { MinHeight = 32, Margin = new Thickness(16, 8, 16, 8) };   // 48 de alto, como en el panel
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            left.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
            if (subtitle != null)
                left.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap });
            g.Children.Add(left);
            ((FrameworkElement)right).VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn((FrameworkElement)right, 1);
            g.Children.Add(right);
            return g;
        }
    }

    // Ventana de instalacion. El detalle de que instala y la privacidad esta en la pagina de preguntas del sitio, a un click.
    class InstallWindow : SetupWindow
    {
        public Installer.Choice Result = Installer.Choice.Cancel;

        public InstallWindow() : base(Lang.T("Instalar AutoReady", "Install AutoReady"),
                                      Lang.T("Acepta tus partidas mientras no estás.", "Accepts your matches while you're away."))
        {
            // Actualizar automaticamente queda prendido (se cambia en Mas opciones). El estado de Steam es la unica
            // opcion de privacidad que no esta en el panel: por eso se elige aca, con una linea que dice para que sirve.
            string desktopText = Lang.T("Acceso directo en el escritorio", "Desktop shortcut");
            string autostartText = Lang.T("Iniciar con Windows", "Start with Windows");
            string steamText = Lang.T("Usar mi estado de Steam", "Use my Steam status");
            Toggle desktop = new Toggle(theme, true, desktopText), autostart = new Toggle(theme, true, autostartText),
                   steam = new Toggle(theme, true, steamText);
            StackPanel card = new StackPanel();
            card.Children.Add(Row(desktopText, desktop));
            card.Children.Add(Divider());
            card.Children.Add(Row(autostartText, autostart));
            card.Children.Add(Divider());
            card.Children.Add(Row(steamText, steam, Lang.T("Lee tu estado público de Steam para saber cuándo estás en partida",
                                                            "Reads your public Steam status to know when you're in a match")));
            Body.Children.Add(Card(card));

            // Instalar puede tardar unos segundos (cierra otras copias, copia el exe): en segundo plano para que la
            // ventana no se congele.
            ClickArea install = null;
            install = ClickArea.Make(theme, Lang.T("Instalar", "Install"), () =>
            {
                bool d = desktop.IsOn, a = autostart.IsOn, st = steam.IsOn;
                RunInBackground(install, Lang.T("Instalando…", "Installing…"), () => Installer.Install(d, a, true, st), error =>
                {
                    if (error == null) { Result = Installer.Choice.Installed; Close(); return; }
                    Log.Write("Instalador: fallo: " + error);
                    MessageBox.Show(Lang.T("No se pudo instalar AutoReady. Cerralo si está abierto y probá de nuevo.",
                                           "Couldn't install AutoReady. Close it if it's open and try again."), "AutoReady");
                });
            }, ButtonKind.Accent);
            ClickArea details = ClickArea.Make(theme, Lang.T("Privacidad y detalles", "Privacy & details"),
                () => Process.Start(Lang.T("https://hercase.github.io/autoready/faq/?lang=es#install", "https://hercase.github.io/autoready/faq/#install")),
                ButtonKind.Link);
            details.ToolTip = Lang.T("Se instala para tu usuario, sin permisos de administrador, en ", "Installs for your user, no admin rights, in ") + Installer.InstallDir;
            AddButton(details, 0);
            AddButton(install, 2);
            Loaded += (s, e) => install.Focus();   // Enter instala de una
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }
    }

    // Ventana de desinstalacion: una sola pregunta (borrar tambien la configuracion, apagado por defecto: si se
    // reinstala, el celular y la tecla de pausa siguen andando) y el resultado en la misma ventana.
    class UninstallWindow : SetupWindow
    {
        public bool Removed;

        public UninstallWindow() : base(Lang.T("Desinstalar AutoReady", "Uninstall AutoReady"),
                                        Lang.T("Se quita de tu PC, junto con lo que agregó en tus juegos.",
                                               "Removes it from your PC, along with what it added to your games."))
        {
            string dataText = Lang.T("Borrar también mi configuración", "Also delete my settings");
            Toggle data = new Toggle(theme, false, dataText);
            StackPanel card = new StackPanel();
            card.Children.Add(Row(dataText, data, Lang.T("Tecla de pausa, código del celular y capturas",
                                                         "Pause hotkey, phone code and screenshots")));
            Border cardBox = Card(card);
            Body.Children.Add(cardBox);

            ClickArea cancel = ClickArea.Make(theme, Lang.T("Cancelar", "Cancel"), Close, ButtonKind.Standard);
            ClickArea uninstall = null;
            uninstall = ClickArea.Make(theme, Lang.T("Desinstalar", "Uninstall"), () =>
            {
                bool deleteData = data.IsOn;
                cancel.Enabled = false;
                RunInBackground(uninstall, Lang.T("Desinstalando…", "Uninstalling…"), () => Installer.Remove(deleteData), error =>
                {
                    if (error != null)
                    {
                        Log.Write("Desinstalador: fallo: " + error);
                        cancel.Enabled = true;
                        Subtitle.Text = Lang.T("No se pudo desinstalar. Cerrá AutoReady y probá de nuevo.",
                                               "Couldn't uninstall. Close AutoReady and try again.");
                        return;
                    }
                    // Listo: queda el mensaje y un solo boton para cerrar.
                    Removed = true;
                    cardBox.Visibility = Visibility.Collapsed;
                    Body.Children[0].SetValue(MarginProperty, new Thickness(0));
                    Subtitle.Text = Lang.T("Se desinstaló. Gracias por probarlo.", "Uninstalled. Thanks for trying it.");
                    Buttons.Children.Clear();
                    ClickArea close = ClickArea.Make(theme, Lang.T("Cerrar", "Close"), Close, ButtonKind.Accent);
                    AddButton(close, 2);
                    close.Focus();
                });
            }, ButtonKind.Accent);
            AddButton(uninstall, 1);
            AddButton(cancel, 2);
            Loaded += (s, e) => cancel.Focus();   // lo destructivo no queda a un Enter de distancia
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Closing += (s, e) => { if (!Removed && !cancel.Enabled) e.Cancel = true; };   // a mitad de camino no se cierra
        }
    }
}
