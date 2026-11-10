using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GdAntiVirus.Core
{
    /// <summary>Small factory for themed controls so pages can be built in code.</summary>
    public static class Ui
    {
        public static Style S(string key) => (Style)Application.Current.FindResource(key);
        public static Brush B(string key) => (Brush)Application.Current.FindResource(key);

        public static MainWindow Main => Application.Current.MainWindow as MainWindow;
        public static void Toast(string text, ToastKind kind = ToastKind.Info) => Main?.Toast(text, kind);

        public static TextBlock Text(string text, double size = 14, FontWeight? weight = null, string brush = "TextBrush", bool wrap = true)
        {
            return new TextBlock
            {
                Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = B(brush),
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBlock Caption(string text) => new TextBlock { Text = text, Style = S("Caption") };
        public static TextBlock Section(string text) => new TextBlock { Text = text, Style = S("SectionTitle") };

        public static TextBlock Icon(string glyph, double size = 16, Brush brush = null)
        {
            var t = new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = size,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
            if (brush != null) t.Foreground = brush; else t.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            return t;
        }

        public static Border Card(UIElement child, Thickness? padding = null)
        {
            var b = new Border { Style = S("Card"), Child = child };
            if (padding != null) b.Padding = padding.Value;
            return b;
        }

        public static Button Button(string text, Action click, bool accent = false, string glyph = null, string style = null)
        {
            var b = new Button();
            if (style != null) b.Style = S(style);
            else if (accent) b.Style = S("AccentButton");
            b.Content = glyph == null ? (object)text : IconText(glyph, text);
            b.Click += (s, e) => click?.Invoke();
            return b;
        }

        public static StackPanel IconText(string glyph, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(ic);
            if (!string.IsNullOrEmpty(text))
                sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        public static CheckBox Toggle(bool on)
        {
            return new CheckBox { Style = S("ToggleSwitch"), IsChecked = on };
        }

        /// <summary>Settings-style row: [icon] title/description ........ [right].</summary>
        public static Border Row(string glyph, string title, string description, UIElement right)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (glyph != null)
            {
                var ic = Icon(glyph, 18, B("TextBrush"));
                ic.Margin = new Thickness(2, 0, 18, 0);
                g.Children.Add(ic);
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 16, 2) };
            text.Children.Add(Text(title, 14));
            if (!string.IsNullOrEmpty(description)) text.Children.Add(Caption(description));
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            if (right != null)
            {
                if (right is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(right, 2);
                g.Children.Add(right);
            }
            var card = Card(g);
            card.MinHeight = 68;
            return card;
        }

        public static StackPanel HStack(params UIElement[] children)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is FrameworkElement fe && i > 0 && fe.Margin == default(Thickness)) fe.Margin = new Thickness(8, 0, 0, 0);
                sp.Children.Add(children[i]);
            }
            return sp;
        }

        public static WrapPanel Wrap(params UIElement[] children)
        {
            var wp = new WrapPanel();
            foreach (var c in children)
            {
                if (c is FrameworkElement fe && fe.Margin == default(Thickness)) fe.Margin = new Thickness(0, 0, 8, 8);
                wp.Children.Add(c);
            }
            return wp;
        }

        public static ScrollViewer Scroll(UIElement content)
        {
            var host = new Border { Padding = new Thickness(0, 0, 16, 28), Child = content };
            return new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        }

        public static FrameworkElement Spinner(double size = 18)
        {
            var e = new Ellipse
            {
                Width = size, Height = size, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { size * 0.6, 100 },
                StrokeDashCap = PenLineCap.Round, RenderTransformOrigin = new Point(0.5, 0.5), VerticalAlignment = VerticalAlignment.Center
            };
            e.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            var rt = new RotateTransform();
            e.RenderTransform = rt;
            rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
            return e;
        }

        public static Border Badge(string text, Brush brush)
        {
            var c = (brush as SolidColorBrush)?.Color ?? Colors.Gray;
            return new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B)),
                Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = brush }
            };
        }

        public static TextBox Input(string placeholder, double width = double.NaN)
        {
            return new TextBox { Tag = placeholder, Width = width };
        }

        public static TextBox Console(double height = 180)
        {
            return new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = height,
                VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 12,
                Background = B("InputBrush"), Foreground = B("TextSecondaryBrush")
            };
        }

        public static bool Confirm(string title, string text, string ok = "Да", bool danger = false)
        {
            if (App.IsSelfTest) return false;
            return Dialog.Show(title, text, ok, "Отмена", danger);
        }

        public static void Info(string title, string text) => Dialog.Show(title, text, "OK", null, false);
    }

    /// <summary>Dark modal dialog matching the app style.</summary>
    public sealed class Dialog : Window
    {
        private bool _result;

        private Dialog(string title, string text, string ok, string cancel, bool danger)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
            Foreground = Ui.B("TextBrush");
            if (Application.Current.MainWindow?.IsLoaded == true) Owner = Application.Current.MainWindow;

            var body = new StackPanel { Margin = new Thickness(24, 22, 24, 22), MaxWidth = 460, MinWidth = 340 };
            body.Children.Add(Ui.Text(title, 20, FontWeights.SemiBold));
            var t = Ui.Text(text, 14, null, "TextSecondaryBrush");
            t.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(t);

            var footer = new Grid { Background = Ui.B("WindowBrush") };
            var buttons = new UniformGrid { Columns = cancel == null ? 1 : 2, Margin = new Thickness(24, 18, 24, 18), HorizontalAlignment = HorizontalAlignment.Stretch };
            var okBtn = Ui.Button(ok, () => { _result = true; Close(); }, accent: !danger, style: danger ? "DangerButton" : null);
            okBtn.Margin = new Thickness(0, 0, cancel == null ? 0 : 4, 0);
            okBtn.IsDefault = true;
            buttons.Children.Add(okBtn);
            if (cancel != null)
            {
                var c = Ui.Button(cancel, () => Close());
                c.Margin = new Thickness(4, 0, 0, 0);
                c.IsCancel = true;
                buttons.Children.Add(c);
            }
            footer.Children.Add(buttons);

            var dock = new DockPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            dock.Children.Add(footer);
            dock.Children.Add(body);

            Content = new Border
            {
                Margin = new Thickness(16), CornerRadius = new CornerRadius(8), Background = Ui.B("CardBrush"),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.5 },
                Child = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = dock }
            };
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        }

        public static bool Show(string title, string text, string ok, string cancel, bool danger)
        {
            var d = new Dialog(title, text, ok, cancel, danger);
            d.ShowDialog();
            return d._result;
        }
    }
}
