using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
                k.SetValue("URLInfoAbout", "https://github.com/hercase/autoready");
                k.SetValue("HelpLink", "https://github.com/hercase/autoready/issues");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        public static int Uninstall()
        {
            if (MessageBox.Show(Lang.T("¿Desinstalar AutoReady?", "Uninstall AutoReady?"), "AutoReady",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return 0;
            bool deleteData = MessageBox.Show(
                Lang.T("¿Borrar también tu configuración y tus datos (atajo, avisos, capturas)?",
                       "Also delete your settings and data (hotkey, alerts, screenshots)?"),
                "AutoReady", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

            CloseOtherInstances();
            TryDelete(Shortcut.StartMenuPath);
            if (string.Equals(Shortcut.Target(Shortcut.DesktopPath), InstalledExe, StringComparison.OrdinalIgnoreCase))
                TryDelete(Shortcut.DesktopPath);
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null) k.DeleteValue("AutoReady", false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\keis.AutoReady", false);
            Gsi.RemoveAllConfigs();
            if (deleteData) { try { Directory.Delete(Config.Dir, true); } catch { } }

            // El exe no se puede borrar a si mismo mientras corre: lo borra un cmd oculto cuando este proceso termine.
            Process.Start(new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"")
                { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false });
            MessageBox.Show(Lang.T("AutoReady se desinstaló.", "AutoReady was uninstalled."), "AutoReady");
            return 0;
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

    // Ventana de instalacion, con el mismo estilo que el panel.
    class InstallWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public Installer.Choice Result = Installer.Choice.Cancel;
        readonly Theme theme = Theme.FromSystem();

        public InstallWindow()
        {
            Title = Lang.T("Instalar AutoReady", "Install AutoReady");
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

            StackPanel root = new StackPanel { Margin = new Thickness(24) };
            Image logo = new Image { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
            using (Stream st = typeof(InstallWindow).Assembly.GetManifestResourceStream("AutoReady.logo-64.png"))
            {
                System.Windows.Media.Imaging.BitmapImage img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit(); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.StreamSource = st; img.EndInit();
                logo.Source = img;
            }
            root.Children.Add(logo);
            root.Children.Add(new TextBlock { Text = "AutoReady", FontSize = 22, FontWeight = FontWeights.SemiBold });
            root.Children.Add(new TextBlock
            {
                Text = Lang.T("Acepta tus partidas de Steam mientras no estás.", "Accepts your Steam matches while you're away."),
                Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 18),
            });

            Toggle desktop = new Toggle(theme, true, Lang.T("Acceso directo en el escritorio", "Desktop shortcut")),
                   autostart = new Toggle(theme, true, Lang.T("Iniciar con Windows", "Start with Windows"));
            StackPanel card = new StackPanel();
            card.Children.Add(Row(Lang.T("Acceso directo en el escritorio", "Desktop shortcut"), desktop));
            card.Children.Add(new Border { Height = 1, Background = theme.CardStroke });
            card.Children.Add(Row(Lang.T("Iniciar con Windows", "Start with Windows"), autostart));
            root.Children.Add(Card(card));

            // Privacidad (PRIVACY.md): cada conexion automatica se puede apagar desde aca o despues en el panel.
            root.Children.Add(new TextBlock { Text = Lang.T("Conexiones", "Connections"), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 16, 0, 8) });
            Toggle updates = new Toggle(theme, true, Lang.T("Buscar actualizaciones", "Check for updates")),
                   steam = new Toggle(theme, true, Lang.T("Usar mi estado de Steam", "Use my Steam status"));
            StackPanel net = new StackPanel();
            net.Children.Add(Row(Lang.T("Buscar actualizaciones", "Check for updates"), updates,
                Lang.T("Consulta GitHub al iniciar y una vez por día", "Asks GitHub at startup and once a day")));
            net.Children.Add(new Border { Height = 1, Background = theme.CardStroke });
            net.Children.Add(Row(Lang.T("Usar mi estado de Steam", "Use my Steam status"), steam,
                Lang.T("Lee tu perfil público de Steam mientras un juego compatible está abierto",
                       "Reads your public Steam profile while a supported game is open")));
            root.Children.Add(Card(net));
            string privacyText = Lang.T("Política de privacidad", "Privacy policy");
            root.Children.Add(new ClickArea(theme,
                new TextBlock { Text = privacyText, Foreground = theme.Link, FontSize = 12, TextDecorations = TextDecorations.Underline },
                () => Process.Start("https://github.com/hercase/autoready/blob/main/PRIVACY.md"), privacyText)
                { Margin = new Thickness(2, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });

            // Cambios al sistema, avisados antes de instalar.
            root.Children.Add(new TextBlock
            {
                Text = Lang.T("Se instala solo para tu usuario, sin permisos de administrador, en " + Installer.InstallDir + ". " +
                              "Agrega un archivo de configuración oficial (Game State Integration) en las carpetas de tus juegos " +
                              "compatibles para saber cuándo estás en partida. Se desinstala desde Configuración > Aplicaciones, y ahí se borra todo.",
                              "Installs for your user only, no admin rights needed, in " + Installer.InstallDir + ". " +
                              "It adds an official config file (Game State Integration) to the folders of your supported games to know " +
                              "when you're in a match. Uninstall it from Settings > Apps, which removes everything."),
                FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 20),
            });

            Grid buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string installText = Lang.T("Instalar", "Install");
            ClickArea install = new ClickArea(theme,
                new TextBlock { Text = installText, Foreground = theme.OnFill, FontWeight = FontWeights.SemiBold }, () =>
            {
                try
                {
                    Installer.Install(desktop.IsOn, autostart.IsOn, updates.IsOn, steam.IsOn);
                    Result = Installer.Choice.Installed;
                    Close();
                }
                catch (Exception ex)
                {
                    Log.Write("Instalador: fallo: " + ex);
                    MessageBox.Show(Lang.T("No se pudo instalar: ", "Couldn't install: ") + ex.Message, "AutoReady");
                }
            }, installText) { Background = theme.Fill, CornerRadius = new CornerRadius(4), Padding = new Thickness(22, 8, 22, 8) };
            Grid.SetColumn(install, 1);
            buttons.Children.Add(install);
            root.Children.Add(buttons);
            Content = root;
            Loaded += (s, e) => install.Focus();   // Enter instala de una
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        Border Card(StackPanel content)
        {
            return new Border { Background = theme.Card, BorderBrush = theme.CardStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = content };
        }

        Grid Row(string title, UIElement right, string subtitle = null)
        {
            Grid g = new Grid { Margin = new Thickness(14, 12, 14, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            left.Children.Add(new TextBlock { Text = title });
            if (subtitle != null)
                left.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap });
            g.Children.Add(left);
            Grid.SetColumn((FrameworkElement)right, 1);
            g.Children.Add(right);
            return g;
        }
    }
}
