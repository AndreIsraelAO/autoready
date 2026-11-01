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
    // Colores de Windows 11 para tema claro y oscuro (sigue el tema de apps del sistema).
    // Fill: verde de botones e interruptores, mas oscuro que el de la marca para que el blanco encima se lea (>= 4,5:1).
    class Theme
    {
        public Brush Background, Card, CardStroke, Text, TextSecondary, ControlStroke, Fill, OnFill, Good, Idle, Paused, Link, Focus;
        public bool Dark;

        public static Theme FromSystem()
        {
            bool light = true;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                if (k != null && k.GetValue("AppsUseLightTheme") is int) light = (int)k.GetValue("AppsUseLightTheme") != 0;

            Theme t = new Theme { Dark = !light };
            t.Fill = Rgb(0x1E, 0x7A, 0x52);      // blanco encima: 5,4:1
            t.OnFill = Brushes.White;
            t.Good = Rgb(0x3F, 0xB3, 0x7F);
            t.Idle = Rgb(0x9A, 0x9A, 0x9A);
            t.Paused = Rgb(0xD9, 0x9A, 0x2B);
            if (light)
            {
                t.Background = Rgb(0xF3, 0xF3, 0xF3); t.Card = Rgb(0xFF, 0xFF, 0xFF); t.CardStroke = Rgb(0xE5, 0xE5, 0xE5);
                t.Text = Rgb(0x1B, 0x1B, 0x1B); t.TextSecondary = Rgb(0x5F, 0x5F, 0x5F); t.ControlStroke = Rgb(0x8A, 0x8A, 0x8A);
                t.Link = Rgb(0x17, 0x6B, 0x47);   // sobre blanco: 6:1
                t.Focus = Rgb(0x1B, 0x1B, 0x1B);
            }
            else
            {
                t.Background = Rgb(0x20, 0x20, 0x20); t.Card = Rgb(0x2B, 0x2B, 0x2B); t.CardStroke = Rgb(0x1C, 0x1C, 0x1C);
                t.Text = Rgb(0xFF, 0xFF, 0xFF); t.TextSecondary = Rgb(0xC5, 0xC5, 0xC5); t.ControlStroke = Rgb(0x9A, 0x9A, 0x9A);
                t.Link = Rgb(0x5F, 0xD4, 0x9E);
                t.Focus = Rgb(0xFF, 0xFF, 0xFF);
            }
            return t;
        }

        // Marco de foco (solo con teclado) como el de Windows 11: borde de 2 px alrededor del control.
        public Style FocusVisual()
        {
            FrameworkElementFactory f = new FrameworkElementFactory(typeof(Border));
            f.SetValue(Border.BorderBrushProperty, Focus);
            f.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            f.SetValue(Border.MarginProperty, new Thickness(-4));
            Style s = new Style(typeof(Control));
            s.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = f }));
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
    // interruptor con su nombre y estado.
    class Toggle : Border
    {
        readonly Theme theme;
        readonly Ellipse thumb;
        readonly TranslateTransform slide = new TranslateTransform();
        bool on;
        public event Action<bool> Changed;

        public Toggle(Theme theme, bool initial, string label = null)
        {
            this.theme = theme;
            Width = 40; Height = 20; CornerRadius = new CornerRadius(10);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = theme.FocusVisual();
            VerticalAlignment = VerticalAlignment.Center;
            if (label != null) AutomationProperties.SetName(this, label);
            thumb = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), RenderTransform = slide };
            Child = thumb;
            MouseLeftButtonUp += (s, e) => { Focus(); Flip(); };
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
            Background = on ? theme.Fill : Brushes.Transparent;
            BorderBrush = on ? theme.Fill : theme.ControlStroke;
            thumb.Fill = on ? theme.OnFill : theme.ControlStroke;
            double to = on ? 20 : 0;
            if (animate)
                slide.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() });
            else
                slide.X = to;
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

    // Area clickeable (boton o link) armada con un Border: responde a mouse, Tab + Enter/Espacio y el lector de
    // pantalla la anuncia como boton con su texto.
    class ClickArea : Border
    {
        readonly Action click;

        public ClickArea(Theme theme, UIElement content, Action click, string name)
        {
            this.click = click;
            Child = content;
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = theme.FocusVisual();
            Background = Brushes.Transparent;   // para que toda el area reciba el click
            AutomationProperties.SetName(this, name);
            MouseLeftButtonUp += (s, e) => click();
            KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { click(); e.Handled = true; } };
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
}
