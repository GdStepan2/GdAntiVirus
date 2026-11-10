using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GdAntiVirus.Core;
using GdAntiVirus.Pages;

namespace GdAntiVirus
{
    public enum ToastKind { Info, Success, Warning, Error }

    public partial class MainWindow : Window
    {
        private sealed class NavEntry
        {
            public string Glyph, Text;
            public Func<PageBase> Factory;
            public Type PageType;
            public PageBase Page;
            public RadioButton Button;
        }

        private readonly List<NavEntry> _entries = new List<NavEntry>();
        private readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };

        public MainWindow()
        {
            InitializeComponent();
            VersionText.Text = AppInfo.Version + (AppInfo.IsAdmin ? "  •  Администратор" : "");
            BuildNav();
            _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); HideToast(); };
            StateChanged += (s, e) => OnStateChanged();
            SourceInitialized += (s, e) => ApplyDwm();
            Loaded += (s, e) =>
            {
                Navigate(_entries[0]);
                RefreshStatus();
                if (App.IsSelfTest) RunSelfTest();
            };
        }

        private void BuildNav()
        {
            Group("ЗАЩИТА");
            Add("\uE80F", "Главная", () => new DashboardPage());
            Add("\uE721", "Сканер", () => new ScannerPage());
            Add("\uE72E", "Карантин", () => new QuarantinePage());
            Add("\uEA18", "Безопасность", () => new SecurityPage());
            Group("СИСТЕМА");
            Add("\uE9D9", "Процессы", () => new ProcessesPage());
            Add("\uE7E8", "Автозагрузка", () => new StartupPage());
            Add("\uE74D", "Очистка", () => new CleanerPage());
            Add("\uE90F", "Твики", () => new TweaksPage("Твики", "Настройка проводника, внешнего вида, панели задач и системы",
                Tweaks.Explorer, Tweaks.Personalization, Tweaks.Taskbar, Tweaks.System));
            Add("\uE8D7", "Конфиденциальность", () => new TweaksPage("Конфиденциальность", "Отключение телеметрии, рекламы и сбора данных",
                Tweaks.Privacy));
            Group("ИНСТРУМЕНТЫ");
            Add("\uE896", "Программы", () => new AppsPage());
            Add("\uE756", "Утилиты", () => new ToolsPage());
            Add("\uE7F8", "О системе", () => new SystemInfoPage());
            Add("\uE713", "Настройки", () => new SettingsPage(), bottom: true);
        }

        private void Group(string text)
        {
            NavTop.Children.Add(new TextBlock
            {
                Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, NavTop.Children.Count == 0 ? 0 : 14, 0, 6),
                Foreground = Ui.B("TextTertiaryBrush")
            });
        }

        private void Add<T>(string glyph, string text, Func<T> factory, bool bottom = false) where T : PageBase
        {
            var entry = new NavEntry { Glyph = glyph, Text = text, Factory = factory, PageType = typeof(T) };
            entry.Button = new RadioButton { Style = Ui.S("NavButton"), Tag = glyph, Content = text, GroupName = "nav" };
            entry.Button.Checked += (s, e) => Navigate(entry);
            _entries.Add(entry);
            (bottom ? NavBottom : NavTop).Children.Add(entry.Button);
        }

        private void Navigate(NavEntry entry)
        {
            if (entry.Page == null) entry.Page = entry.Factory();
            if (entry.Button.IsChecked != true) entry.Button.IsChecked = true;
            if (PageHost.Content == entry.Page) return;

            PageTitle.Text = entry.Page.Header;
            PageSubtitle.Text = entry.Page.Subtitle;
            PageHost.Content = entry.Page;
            entry.Page.Show();

            // Small slide/fade-in like WinUI page transitions.
            var tt = new TranslateTransform(0, 14);
            entry.Page.RenderTransform = tt;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            entry.Page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }

        public T Navigate<T>() where T : PageBase
        {
            var e = _entries.Find(x => x.PageType == typeof(T));
            if (e == null) return null;
            Navigate(e);
            return (T)e.Page;
        }

        public void RefreshStatus()
        {
            Task.Run(() => SecurityStatus.Evaluate()).ContinueWith(t =>
            {
                if (t.Exception != null) { Log.Error("Статус защиты", t.Exception); return; }
                var s = t.Result;
                var brush = s.Level == 0 ? Ui.B("SuccessBrush") : s.Level == 1 ? Ui.B("WarningBrush") : Ui.B("DangerBrush");
                StatusDot.Fill = brush;
                StatusIcon.Foreground = brush;
                StatusIcon.Text = s.Level == 0 ? "\uEA18" : "\uE7BA";
                StatusTitle.Text = s.Title;
                StatusSub.Text = s.Short;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------- Toast ----------

        public void Toast(string text, ToastKind kind = ToastKind.Info)
        {
            ToastText.Text = text;
            switch (kind)
            {
                case ToastKind.Success: ToastIcon.Text = "\uE73E"; ToastIcon.Foreground = Ui.B("SuccessBrush"); break;
                case ToastKind.Warning: ToastIcon.Text = "\uE7BA"; ToastIcon.Foreground = Ui.B("WarningBrush"); break;
                case ToastKind.Error: ToastIcon.Text = "\uEA39"; ToastIcon.Foreground = Ui.B("DangerBrush"); break;
                default: ToastIcon.Text = "\uE946"; ToastIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); break;
            }
            ToastBox.Visibility = Visibility.Visible;
            ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void HideToast()
        {
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            a.Completed += (s, e) => { if (!_toastTimer.IsEnabled) ToastBox.Visibility = Visibility.Collapsed; };
            ToastBox.BeginAnimation(OpacityProperty, a);
        }

        // ---------- Window chrome ----------

        private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void OnMaximize(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void OnStateChanged()
        {
            // A maximized WindowChrome window overhangs the screen by the resize border.
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private void ApplyDwm()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int dark = 1, round = 2;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));  // DWMWA_WINDOW_CORNER_PREFERENCE = round (Win11)
            }
            catch { /* older Windows: ignore */ }
        }

        // ---------- Self test (CI) ----------

        private void RunSelfTest()
        {
            int i = 0;
            var shots = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(App.SelfTestResultFile)), "screens");
            System.IO.Directory.CreateDirectory(shots);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            timer.Tick += (s, e) =>
            {
                if (i > 0) SaveScreenshot(System.IO.Path.Combine(shots, $"{i:00}-{_entries[i - 1].PageType.Name}.png"));
                if (i < _entries.Count)
                {
                    Log.Info("selftest: " + _entries[i].Text);
                    Navigate(_entries[i++]);
                    return;
                }
                timer.Stop();
                App.Pass();
            };
            timer.Start();
        }

        private void SaveScreenshot(string file)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
                dc.DrawRectangle(new VisualBrush(Root), null, new Rect(0, 0, ActualWidth, ActualHeight));
            }
            bmp.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(file)) enc.Save(fs);
        }
    }
}
