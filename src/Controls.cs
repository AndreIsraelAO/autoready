using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace AutoReady
{
    // Colores de Windows 11 (Fluent) para tema claro y oscuro (sigue el tema de apps del sistema).
    // En oscuro el acento es el verde claro con contenido negro, como hace Windows; en claro, el verde oscuro con blanco.
    class Theme
    {
        public Brush Background, Card, CardStroke, Divider, Text, TextSecondary, ControlStroke, Good, Idle, Paused, Focus, FocusInner;
        public Brush Fill, FillHover, FillPressed, OnFill;                       // botones de acento e interruptores
        public Brush Button, ButtonHover, ButtonPressed, ButtonStroke;           // botones comunes
        public Brush SubtleHover, SubtlePressed;                                 // links, encabezados clickeables
        public Brush Link, LinkHover, LinkPressed;
        public Brush InfoBg, InfoStroke, InfoIcon, CautionBg, CautionIcon;       // avisos (InfoBar)
        public Brush ToggleOffHover;
        public bool Dark;

        public static Theme FromSystem()
        {
            bool light = true;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                if (k != null && k.GetValue("AppsUseLightTheme") is int) light = (int)k.GetValue("AppsUseLightTheme") != 0;
            string forced = Environment.GetEnvironmentVariable("AUTOREADY_THEME");   // light|dark: para revisar capturas
            if (forced == "light") light = true; else if (forced == "dark") light = false;

            Theme t = new Theme { Dark = !light };
            t.Good = Rgb(0x3F, 0xB3, 0x7F);
            t.Idle = Rgb(0x9A, 0x9A, 0x9A);
            t.Paused = Rgb(0xD9, 0x9A, 0x2B);
            if (light)
            {
                t.Background = Rgb(0xF3, 0xF3, 0xF3); t.Card = Rgb(0xFF, 0xFF, 0xFF); t.CardStroke = Rgb(0xE5, 0xE5, 0xE5); t.Divider = Rgb(0xEA, 0xEA, 0xEA);
                t.Text = Rgb(0x1B, 0x1B, 0x1B); t.TextSecondary = Rgb(0x5F, 0x5F, 0x5F); t.ControlStroke = Rgb(0x8A, 0x8A, 0x8A);
                t.Fill = Rgb(0x1E, 0x7A, 0x52); t.FillHover = Rgb(0x35, 0x87, 0x63); t.FillPressed = Rgb(0x4B, 0x95, 0x75); t.OnFill = Brushes.White;   // 5,4:1
                t.Button = Rgb(0xFB, 0xFB, 0xFB); t.ButtonHover = Rgb(0xF6, 0xF6, 0xF6); t.ButtonPressed = Rgb(0xF5, 0xF5, 0xF5); t.ButtonStroke = Rgb(0xE5, 0xE5, 0xE5);
                t.SubtleHover = Rgb(0xF6, 0xF6, 0xF6); t.SubtlePressed = Rgb(0xF9, 0xF9, 0xF9);
                t.Link = Rgb(0x17, 0x6B, 0x47); t.LinkHover = Rgb(0x14, 0x5C, 0x3D); t.LinkPressed = Rgb(0x2C, 0x85, 0x60);   // sobre blanco: 6:1
                t.InfoBg = Rgb(0xF6, 0xF6, 0xF6); t.InfoStroke = Rgb(0xE5, 0xE5, 0xE5); t.InfoIcon = t.Fill;
                t.CautionBg = Rgb(0xFF, 0xF4, 0xCE); t.CautionIcon = Rgb(0x9D, 0x5D, 0x00);
                t.ToggleOffHover = Rgb(0xF3, 0xF3, 0xF3);
                t.Focus = Rgb(0x00, 0x00, 0x00); t.FocusInner = Brushes.White;
            }
            else
            {
                t.Background = Rgb(0x20, 0x20, 0x20); t.Card = Rgb(0x2B, 0x2B, 0x2B); t.CardStroke = Rgb(0x1C, 0x1C, 0x1C); t.Divider = Rgb(0x1D, 0x1D, 0x1D);
                t.Text = Rgb(0xFF, 0xFF, 0xFF); t.TextSecondary = Rgb(0xC5, 0xC5, 0xC5); t.ControlStroke = Rgb(0x9A, 0x9A, 0x9A);
                t.Fill = Rgb(0x3F, 0xB3, 0x7F); t.FillHover = Rgb(0x3D, 0xA5, 0x77); t.FillPressed = Rgb(0x3B, 0x98, 0x6E); t.OnFill = Brushes.Black;   // 8:1
                t.Button = Rgb(0x36, 0x36, 0x36); t.ButtonHover = Rgb(0x3A, 0x3A, 0x3A); t.ButtonPressed = Rgb(0x31, 0x31, 0x31); t.ButtonStroke = Rgb(0x3D, 0x3D, 0x3D);
                t.SubtleHover = Rgb(0x36, 0x36, 0x36); t.SubtlePressed = Rgb(0x32, 0x32, 0x32);
                t.Link = Rgb(0x5F, 0xD4, 0x9E); t.LinkHover = Rgb(0x7F, 0xE0, 0xB4); t.LinkPressed = Rgb(0x4F, 0xB3, 0x86);
                t.InfoBg = Rgb(0x2B, 0x2B, 0x2B); t.InfoStroke = Rgb(0x1D, 0x1D, 0x1D); t.InfoIcon = t.Fill;
                t.CautionBg = Rgb(0x43, 0x35, 0x19); t.CautionIcon = Rgb(0xFC, 0xE1, 0x00);
                t.ToggleOffHover = Rgb(0x35, 0x35, 0x35);
                t.Focus = Rgb(0xFF, 0xFF, 0xFF); t.FocusInner = Brushes.Black;
            }
            return t;
        }

        // Marco de foco (solo con teclado) como el de Windows 11: 2 px por fuera y 1 px de contraste por dentro,
        // con las esquinas siguiendo la forma del control (en el interruptor, una pastilla).
        public Style FocusVisual(double radius = 4)
        {
            FrameworkElementFactory outer = new FrameworkElementFactory(typeof(Border));
            outer.SetValue(Border.BorderBrushProperty, Focus);
            outer.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius + 3));
            outer.SetValue(Border.MarginProperty, new Thickness(-3));
            FrameworkElementFactory inner = new FrameworkElementFactory(typeof(Border));
            inner.SetValue(Border.BorderBrushProperty, FocusInner);
            inner.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            inner.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius + 1));
            outer.AppendChild(inner);
            Style s = new Style(typeof(Control));
            s.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = outer }));
            return s;
        }

        static Brush Rgb(byte r, byte g, byte b)
        {
            SolidColorBrush s = new SolidColorBrush(Color.FromRgb(r, g, b));
            s.Freeze();
            return s;
        }
    }

    // Interruptor estilo Windows 11. Se usa con mouse, Tab + Espacio/Enter, y el lector de pantalla lo anuncia como
    // interruptor con su nombre y estado. Con el mouse encima la bolita crece; al apretar se estira, como en Windows.
    class Toggle : Border
    {
        readonly Theme theme;
        readonly Ellipse thumb;
        readonly TranslateTransform slide = new TranslateTransform();
        bool on, hover, pressed;
        public event Action<bool> Changed;

        public Toggle(Theme theme, bool initial, string label = null)
        {
            this.theme = theme;
            Width = 40; Height = 20; CornerRadius = new CornerRadius(10);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = theme.FocusVisual(10);
            VerticalAlignment = VerticalAlignment.Center;
            if (label != null) AutomationProperties.SetName(this, label);
            thumb = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RenderTransform = slide };
            Child = thumb;
            MouseEnter += (s, e) => { hover = true; Paint(); };
            MouseLeave += (s, e) => { hover = pressed = false; Paint(); };
            MouseLeftButtonDown += (s, e) => { pressed = true; Paint(); };
            MouseLeftButtonUp += (s, e) => { pressed = false; Focus(); Flip(); };
            KeyDown += (s, e) => { if (e.Key == Key.Space || e.Key == Key.Enter) { Flip(); e.Handled = true; } };
            Apply(initial, false);
        }

        public bool IsOn { get { return on; } set { if (value != on) Apply(value, true); } }

        public void Flip()
        {
            Apply(!on, true);
            Action<bool> h = Changed;
            if (h != null) h(on);
        }

        void Apply(bool value, bool animate)
        {
            on = value;
            Paint();
            double to = on ? 20 : 0;
            if (animate)
                slide.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() });
            else
                slide.X = to;
        }

        void Paint()
        {
            Background = on ? (hover ? theme.FillHover : theme.Fill) : (hover ? theme.ToggleOffHover : Brushes.Transparent);
            BorderBrush = on ? Background : theme.ControlStroke;
            thumb.Fill = on ? theme.OnFill : theme.ControlStroke;
            double size = hover || pressed ? 14 : 12;
            double width = pressed ? 17 : size;
            thumb.Height = size;
            thumb.Width = width;
            // Centrada en su lugar aunque cambie de tamano; apretada se estira hacia donde va.
            thumb.Margin = new Thickness(on ? 15 - width : 3 - (size - 12) / 2, 0, 0, 0);
        }

        protected override AutomationPeer OnCreateAutomationPeer() { return new Peer(this); }

        class Peer : FrameworkElementAutomationPeer, IToggleProvider
        {
            public Peer(Toggle owner) : base(owner) { }
            Toggle T { get { return (Toggle)Owner; } }
            protected override AutomationControlType GetAutomationControlTypeCore() { return AutomationControlType.Button; }
            protected override string GetClassNameCore() { return "ToggleSwitch"; }
            protected override bool IsKeyboardFocusableCore() { return true; }
            public override object GetPattern(PatternInterface p) { return p == PatternInterface.Toggle ? this : base.GetPattern(p); }
            public ToggleState ToggleState { get { return T.on ? ToggleState.On : ToggleState.Off; } }
            public void Toggle() { T.Flip(); }
        }
    }

    enum ButtonKind { Accent, Standard, Subtle, Link }

    // Area clickeable (boton o link) armada con un Border: responde a mouse, Tab + Enter/Espacio y el lector de
    // pantalla la anuncia como boton con su texto. Tiene estados de reposo, mouse encima y apretado segun su tipo.
    class ClickArea : Border
    {
        readonly Action click;
        Brush rest = Brushes.Transparent, hover = Brushes.Transparent, pressed = Brushes.Transparent;   // fondo
        TextBlock label; Brush labelRest, labelHover, labelPressed;                                      // color del texto
        bool over;

        public ClickArea(Theme theme, UIElement content, Action click, string name)
        {
            this.click = click;
            Child = content;
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = theme.FocusVisual(4);
            Background = rest;   // transparente pero presente: toda el area recibe el click
            AutomationProperties.SetName(this, name);
            MouseEnter += (s, e) => { over = true; Paint(hover, labelHover); };
            MouseLeave += (s, e) => { over = false; Paint(rest, labelRest); };
            MouseLeftButtonDown += (s, e) => Paint(pressed, labelPressed);
            MouseLeftButtonUp += (s, e) => { Paint(over ? hover : rest, over ? labelHover : labelRest); click(); };
            KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { click(); e.Handled = true; } };
        }

        public void SetBackgrounds(Brush rest, Brush hover, Brush pressed)
        {
            this.rest = rest; this.hover = hover; this.pressed = pressed;
            Background = rest;
        }

        public void SetLabelColors(TextBlock label, Brush rest, Brush hover, Brush pressed)
        {
            this.label = label; labelRest = rest; labelHover = hover; labelPressed = pressed;
            label.Foreground = rest;
        }

        void Paint(Brush bg, Brush fg)
        {
            Background = bg;
            if (label != null && fg != null) label.Foreground = fg;
        }

        // Botones con el estilo de Windows 11: acento (accion principal), comun, sutil (sin fondo) y link.
        public static ClickArea Make(Theme t, string text, Action click, ButtonKind kind, double fontSize = 14)
        {
            TextBlock tb = new TextBlock { Text = text, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            ClickArea b = new ClickArea(t, tb, click, text) { CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
            switch (kind)
            {
                case ButtonKind.Accent:
                    tb.FontWeight = FontWeights.SemiBold;
                    tb.Foreground = t.OnFill;
                    b.SetBackgrounds(t.Fill, t.FillHover, t.FillPressed);
                    b.MinHeight = 32; b.Padding = new Thickness(12, 0, 12, 0);
                    break;
                case ButtonKind.Standard:
                    b.SetBackgrounds(t.Button, t.ButtonHover, t.ButtonPressed);
                    b.SetLabelColors(tb, t.Text, t.Text, t.TextSecondary);
                    b.BorderBrush = t.ButtonStroke; b.BorderThickness = new Thickness(1);
                    b.MinHeight = 32; b.Padding = new Thickness(12, 0, 12, 0);
                    break;
                case ButtonKind.Subtle:
                    b.SetBackgrounds(Brushes.Transparent, t.SubtleHover, t.SubtlePressed);
                    b.SetLabelColors(tb, t.Text, t.Text, t.TextSecondary);
                    b.Padding = new Thickness(8, 4, 8, 4);
                    break;
                default:   // Link: sin subrayado, como el HyperlinkButton de Fluent
                    b.SetLabelColors(tb, t.Link, t.LinkHover, t.LinkPressed);
                    b.HorizontalAlignment = HorizontalAlignment.Left;
                    break;
            }
            return b;
        }

        protected override AutomationPeer OnCreateAutomationPeer() { return new Peer(this); }

        class Peer : FrameworkElementAutomationPeer, IInvokeProvider
        {
            public Peer(ClickArea owner) : base(owner) { }
            protected override AutomationControlType GetAutomationControlTypeCore() { return AutomationControlType.Button; }
            protected override bool IsKeyboardFocusableCore() { return true; }
            public override object GetPattern(PatternInterface p) { return p == PatternInterface.Invoke ? this : base.GetPattern(p); }
            public void Invoke() { ((ClickArea)Owner).click(); }
        }
    }

    // Aviso estilo InfoBar de Windows 11: icono, titulo y/o mensaje, una accion opcional y una X opcional.
    // El mismo componente sirve para la bienvenida, la actualizacion y el paso que le falta a un juego.
    class InfoBar : Border
    {
        public readonly TextBlock Title, Message;

        public InfoBar(Theme t, bool caution, string glyph, string title, string message, ClickArea action, Action onClose)
        {
            Background = caution ? t.CautionBg : t.InfoBg;
            BorderBrush = caution ? t.CautionBg : t.InfoStroke;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(16, 12, onClose != null ? 6 : 12, 12);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16,
                Foreground = caution ? t.CautionIcon : t.InfoIcon, Margin = new Thickness(0, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            });
            StackPanel texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Title = new TextBlock { Text = title ?? "", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Visibility = title == null ? Visibility.Collapsed : Visibility.Visible };
            Message = new TextBlock { Text = message ?? "", TextWrapping = TextWrapping.Wrap, Visibility = message == null ? Visibility.Collapsed : Visibility.Visible };
            if (title != null && message != null) Message.Margin = new Thickness(0, 2, 0, 0);
            texts.Children.Add(Title);
            texts.Children.Add(Message);
            Grid.SetColumn(texts, 1);
            g.Children.Add(texts);
            if (action != null)
            {
                action.Margin = new Thickness(12, 0, 0, 0);
                Grid.SetColumn(action, 2);
                g.Children.Add(action);
            }
            if (onClose != null)
            {
                TextBlock x = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                ClickArea close = new ClickArea(t, x, onClose, S.Close) { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, -6, 0, -6) };
                close.SetBackgrounds(Brushes.Transparent, t.SubtleHover, t.SubtlePressed);
                close.SetLabelColors(x, t.TextSecondary, t.Text, t.TextSecondary);
                Grid.SetColumn(close, 3);
                g.Children.Add(close);
            }
            Child = g;
        }
    }
}
