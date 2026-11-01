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
                Lang.T("¿Borrar también tu configuración (tecla de pausa, código del celular, capturas)?",
                       "Also delete your settings (pause hotkey, phone code, screenshots)?"),
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

            // Como el panel: encabezado chico, una sola tarjeta y una franja abajo con el boton (dialogo de Windows 11).
            // El detalle de que instala y la privacidad esta en la pagina de preguntas del sitio, a un click.
            StackPanel body = new StackPanel { Margin = new Thickness(24, 24, 24, 24) };
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            Image logo = new Image { Width = 48, Height = 48, Margin = new Thickness(0, 0, 16, 0) };
            using (Stream st = typeof(InstallWindow).Assembly.GetManifestResourceStream("AutoReady.logo-64.png"))
            {
                System.Windows.Media.Imaging.BitmapImage img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit(); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.StreamSource = st; img.EndInit();
                logo.Source = img;
            }
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            header.Children.Add(logo);
            StackPanel titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "AutoReady", FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
            titles.Children.Add(new TextBlock
            {
                Text = Lang.T("Acepta tus partidas de Dota 2 y CS2 mientras no estás.", "Accepts your Dota 2 and CS2 matches while you're away."),
                Foreground = theme.TextSecondary, TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);
            body.Children.Add(header);

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
            body.Children.Add(Card(card));

            // Instalar puede tardar unos segundos (cierra otras copias, copia el exe): se hace en segundo plano para que
            // la ventana no se congele, con el boton apagado y "Instalando…" mientras tanto.
            string installText = Lang.T("Instalar", "Install");
            ClickArea install = null;
            install = ClickArea.Make(theme, installText, () =>
            {
                bool d = desktop.IsOn, a = autostart.IsOn, st = steam.IsOn;
                TextBlock label = (TextBlock)install.Child;
                label.Text = Lang.T("Instalando…", "Installing…");
                install.Enabled = false;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    Exception error = null;
                    try { Installer.Install(d, a, true, st); }
                    catch (Exception ex) { error = ex; }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (error == null) { Result = Installer.Choice.Installed; Close(); return; }
                        Log.Write("Instalador: fallo: " + error);
                        label.Text = installText;
                        install.Enabled = true;
                        MessageBox.Show(Lang.T("No se pudo instalar AutoReady. Cerralo si está abierto y probá de nuevo.",
                                               "Couldn't install AutoReady. Close it if it's open and try again."), "AutoReady");
                    }));
                });
            }, ButtonKind.Accent);
            install.MinWidth = 120;
            ClickArea details = ClickArea.Make(theme, Lang.T("Privacidad y detalles", "Privacy & details"),
                () => Process.Start(Lang.T("https://hercase.github.io/autoready/faq/?lang=es#install", "https://hercase.github.io/autoready/faq/#install")),
                ButtonKind.Link);
            details.ToolTip = Lang.T("Se instala para tu usuario, sin permisos de administrador, en ", "Installs for your user, no admin rights, in ") + Installer.InstallDir;
            Grid buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.Children.Add(details);
            Grid.SetColumn(install, 1);
            buttons.Children.Add(install);
            Border footer = new Border
            {
                Padding = new Thickness(24), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons,
                Background = new SolidColorBrush(theme.Dark ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xEE, 0xEE, 0xEE)),
                BorderBrush = new SolidColorBrush(theme.Dark ? Color.FromRgb(0x2E, 0x2E, 0x2E) : Color.FromRgb(0xE5, 0xE5, 0xE5)),
            };

            StackPanel root = new StackPanel();
            root.Children.Add(body);
            root.Children.Add(footer);
            Content = root;
            Loaded += (s, e) => install.Focus();   // Enter instala de una
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        Border Divider() { return new Border { Height = 1, Background = theme.Divider }; }

        Border Card(StackPanel content)
        {
            return new Border { Background = theme.Card, BorderBrush = theme.CardStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = content };
        }

        Grid Row(string title, UIElement right, string subtitle = null)
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
}
