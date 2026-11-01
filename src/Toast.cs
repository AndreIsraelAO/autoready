using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.Foundation;
using Windows.UI.Notifications;

namespace AutoReady
{
    // Notificaciones nativas de Windows 10/11 (toasts): logo de la app, titulo, texto y botones.
    // Como AutoReady no se instala, la identidad de la app (nombre e icono que muestra Windows) se registra
    // en HKCU\Software\Classes\AppUserModelId, que es lo que Windows usa para apps de escritorio sin paquete.
    static class Toast
    {
        const string AppId = "keis.AutoReady";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        static ToastNotifier notifier;
        static string logoPath;

        // Devuelve false si este Windows no soporta toasts (ahi se usan los avisos clasicos de la bandeja).
        public static bool Init(Stream logoPng)
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                logoPath = Path.Combine(Config.Dir, "logo.png");
                using (FileStream f = File.Create(logoPath)) logoPng.CopyTo(f);

                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId))
                {
                    k.SetValue("DisplayName", "AutoReady");
                    k.SetValue("IconUri", logoPath);
                    k.SetValue("IconBackgroundColor", "FF09090B");
                }
                SetCurrentProcessExplicitAppUserModelID(AppId);
                notifier = ToastNotificationManager.CreateToastNotifier(AppId);
                return true;
            }
            catch (Exception e)
            {
                Log.Write("Toasts no disponibles: " + e.Message);
                return false;
            }
        }

        // button/onButton: boton opcional dentro del aviso (por ejemplo "Reanudar").
        public static bool Show(string title, string body, string button, Action onButton, Action onClick)
        {
            if (notifier == null) return false;
            try
            {
                string actions = button == null ? "" :
                    "<actions><action content='" + Esc(button) + "' arguments='button' activationType='foreground'/></actions>";
                string xml =
                    "<toast launch='open' duration='short'>" +
                    "<visual><binding template='ToastGeneric'>" +
                    "<text>" + Esc(title) + "</text>" +
                    (string.IsNullOrEmpty(body) ? "" : "<text>" + Esc(body) + "</text>") +
                    "<image placement='appLogoOverride' src='" + Esc(new Uri(logoPath).AbsoluteUri) + "'/>" +
                    "</binding></visual>" + actions +
                    "<audio silent='true'/>" +   // el sonido lo maneja AutoReady (se puede apagar en el panel)
                    "</toast>";
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                ToastNotification t = new ToastNotification(doc);
                // El csc de .NET Framework no compila "+=" sobre eventos de WinRT; por reflexion si funciona.
                TypedEventHandler<ToastNotification, object> handler = (s, e) =>
                {
                    ToastActivatedEventArgs a = e as ToastActivatedEventArgs;
                    Action h = a != null && a.Arguments == "button" ? onButton : onClick;
                    if (h != null) h();
                };
                typeof(ToastNotification).GetEvent("Activated").AddEventHandler(t, handler);
                notifier.Show(t);
                return true;
            }
            catch (Exception e)
            {
                Log.Write("No pude mostrar la notificacion: " + e.Message);
                return false;
            }
        }

        static string Esc(string s) { return SecurityElement.Escape(s); }
    }
}
