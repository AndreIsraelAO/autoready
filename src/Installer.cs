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

        public static void Install(bool desktop, bool autostart)
        {
            string self = Path.GetFullPath(System.Windows.Forms.Application.ExecutablePath);
            CloseOtherInstances();
            Directory.CreateDirectory(InstallDir);
            File.Copy(self, InstalledExe, true);

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

            Toggle desktop = new Toggle(theme, true), autostart = new Toggle(theme, true);
            StackPanel card = new StackPanel();
            card.Children.Add(Row(Lang.T("Acceso directo en el escritorio", "Desktop shortcut"), desktop));
            card.Children.Add(new Border { Height = 1, Background = theme.CardStroke });
            card.Children.Add(Row(Lang.T("Iniciar con Windows", "Start with Windows"), autostart));
            root.Children.Add(new Border { Background = theme.Card, BorderBrush = theme.CardStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = card });

            root.Children.Add(new TextBlock
            {
                Text = Lang.T("Se instala solo para tu usuario, sin permisos de administrador. Se desinstala desde Configuración > Aplicaciones.",
                              "Installs for your user only, no admin rights needed. Uninstall it from Settings > Apps."),
                FontSize = 12, Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 20),
            });

            Grid buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border install = new Border
            {
                Background = theme.Accent, CornerRadius = new CornerRadius(4), Padding = new Thickness(22, 8, 22, 8), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = Lang.T("Instalar", "Install"), Foreground = theme.OnAccent, FontWeight = FontWeights.SemiBold },
            };
            install.MouseLeftButtonUp += (s, e) =>
            {
                try
                {
                    Installer.Install(desktop.IsOn, autostart.IsOn);
                    Result = Installer.Choice.Installed;
                    Close();
                }
                catch (Exception ex)
                {
                    Log.Write("Instalador: fallo: " + ex);
                    MessageBox.Show(Lang.T("No se pudo instalar: ", "Couldn't install: ") + ex.Message, "AutoReady");
                }
            };
            Grid.SetColumn(install, 1);
            buttons.Children.Add(install);
            root.Children.Add(buttons);
            Content = root;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        Grid Row(string title, UIElement right)
        {
            Grid g = new Grid { Margin = new Thickness(14, 12, 14, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn((FrameworkElement)right, 1);
            g.Children.Add(right);
            return g;
        }
    }
}
