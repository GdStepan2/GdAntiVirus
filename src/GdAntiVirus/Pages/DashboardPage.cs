using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GdAntiVirus.Core;

namespace GdAntiVirus.Pages
{
    public sealed class DashboardPage : PageBase
    {
        public override string Header => "Главная";
        public override string Subtitle => "Состояние защиты и быстрые действия";

        private TextBlock _heroTitle, _heroText, _heroIcon;
        private Border _heroIconBg;
        private StackPanel _issues;
        private TextBlock _avValue, _avSub, _fwValue, _fwSub, _scanValue, _scanSub;
        private ProgressBar _cpuBar, _ramBar, _diskBar;
        private TextBlock _cpuText, _ramText, _diskText;
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };

        protected override void Build()
        {
            var root = new StackPanel();

            // Hero card
            _heroIcon = Ui.Icon("\uEA18", 40, Ui.B("SuccessBrush"));
            _heroIconBg = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(42), Child = _heroIcon, Margin = new Thickness(4, 0, 24, 0) };
            _heroTitle = Ui.Text("Проверка состояния…", 24, FontWeights.SemiBold);
            _heroText = Ui.Caption("Собираем информацию о защите компьютера");
            _heroText.FontSize = 13;
            _issues = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var heroInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            heroInfo.Children.Add(_heroTitle);
            heroInfo.Children.Add(_heroText);
            heroInfo.Children.Add(_issues);
            var heroButtons = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            var quick = Ui.Button("Быстрая проверка", () => Main.Navigate<ScannerPage>()?.StartQuick(), accent: true, glyph: "\uE721");
            quick.MinWidth = 190; quick.Height = 36;
            var refresh = Ui.Button("Обновить статус", Refresh, glyph: "\uE72C");
            refresh.Margin = new Thickness(0, 8, 0, 0);
            heroButtons.Children.Add(quick);
            heroButtons.Children.Add(refresh);

            var hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.Children.Add(_heroIconBg);
            Grid.SetColumn(heroInfo, 1); hero.Children.Add(heroInfo);
            Grid.SetColumn(heroButtons, 2); hero.Children.Add(heroButtons);
            var heroCard = Ui.Card(hero, new Thickness(24, 22, 24, 22));
            heroCard.Background = new LinearGradientBrush(Color.FromRgb(0x2F, 0x2F, 0x2F), Color.FromRgb(0x29, 0x29, 0x29), 0);
            root.Children.Add(heroCard);

            // Status tiles
            var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 0) };
            tiles.Children.Add(StatTile("\uEA18", "Антивирус", out _avValue, out _avSub, () => Main.Navigate<SecurityPage>()));
            tiles.Children.Add(StatTile("\uE83D", "Брандмауэр", out _fwValue, out _fwSub, () => Main.Navigate<SecurityPage>()));
            tiles.Children.Add(StatTile("\uE721", "Последняя проверка", out _scanValue, out _scanSub, () => Main.Navigate<ScannerPage>()));
            root.Children.Add(tiles);

            // Resources
            root.Children.Add(Ui.Section("Ресурсы компьютера"));
            var res = new UniformGrid { Columns = 3 };
            res.Children.Add(Meter("\uE950", "Процессор", out _cpuBar, out _cpuText));
            res.Children.Add(Meter("\uE964", "Память", out _ramBar, out _ramText));
            res.Children.Add(Meter("\uEDA2", "Диск " + Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\'), out _diskBar, out _diskText));
            root.Children.Add(res);

            // Quick actions
            root.Children.Add(Ui.Section("Быстрые действия"));
            var actions = new UniformGrid { Columns = 3 };
            actions.Children.Add(ActionTile("\uE74D", "Очистка", "Удалить мусор и кэш", () => Main.Navigate<CleanerPage>()));
            actions.Children.Add(ActionTile("\uE7E8", "Автозагрузка", "Ускорить запуск Windows", () => Main.Navigate<StartupPage>()));
            actions.Children.Add(ActionTile("\uE9D9", "Процессы", "Найти подозрительные программы", () => Main.Navigate<ProcessesPage>()));
            actions.Children.Add(ActionTile("\uE896", "Программы", "Установить нужный софт", () => Main.Navigate<AppsPage>()));
            actions.Children.Add(ActionTile("\uE777", "Точка восстановления", "Сохранить состояние системы", () => SecurityPage.CreateRestorePoint()));
            actions.Children.Add(ActionTile("\uE7F8", "О системе", "Характеристики компьютера", () => Main.Navigate<SystemInfoPage>()));
            root.Children.Add(actions);

            Content = Ui.Scroll(root);

            _timer.Tick += (s, e) => UpdateMeters();
            IsVisibleChanged += (s, e) => { if (IsVisible) _timer.Start(); else _timer.Stop(); };
            SysInfo.CpuLoad();
            Refresh();
        }

        protected override void OnShown()
        {
            _timer.Start();
            UpdateMeters();
            UpdateScanTile();
        }

        private Border StatTile(string glyph, string title, out TextBlock value, out TextBlock sub, Action click)
        {
            var sp = new StackPanel();
            var head = Ui.HStack(Ui.Icon(glyph, 16), Ui.Text(title, 13, null, "TextSecondaryBrush"));
            ((FrameworkElement)head.Children[1]).Margin = new Thickness(10, 0, 0, 0);
            sp.Children.Add(head);
            value = Ui.Text("…", 18, FontWeights.SemiBold);
            value.Margin = new Thickness(0, 10, 0, 0);
            sub = Ui.Caption("");
            sp.Children.Add(value);
            sp.Children.Add(sub);
            var btn = new Button { Style = Ui.S("TileButton"), Content = sp, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 14, 16, 14) };
            btn.Click += (s, e) => click();
            return new Border { Child = btn };
        }

        private Border Meter(string glyph, string title, out ProgressBar bar, out TextBlock text)
        {
            var sp = new StackPanel();
            var top = new DockPanel();
            text = Ui.Text("", 13, FontWeights.SemiBold);
            DockPanel.SetDock(text, Dock.Right);
            top.Children.Add(text);
            var head = Ui.HStack(Ui.Icon(glyph, 16), Ui.Text(title, 13, null, "TextSecondaryBrush"));
            ((FrameworkElement)head.Children[1]).Margin = new Thickness(10, 0, 0, 0);
            top.Children.Add(head);
            sp.Children.Add(top);
            bar = new ProgressBar { Maximum = 100, Margin = new Thickness(0, 14, 0, 4), Height = 6 };
            sp.Children.Add(bar);
            var card = Ui.Card(sp, new Thickness(16, 14, 16, 14));
            card.Margin = new Thickness(0, 0, 6, 6);
            return card;
        }

        private Button ActionTile(string glyph, string title, string desc, Action click)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icBg = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), Child = Ui.Icon(glyph, 18) };
            icBg.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            g.Children.Add(icBg);
            var t = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(Ui.Text(title, 14, FontWeights.SemiBold));
            t.Children.Add(Ui.Caption(desc));
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            var b = new Button { Style = Ui.S("TileButton"), Content = g };
            b.Click += (s, e) => click();
            return b;
        }

        private void UpdateMeters()
        {
            double cpu = SysInfo.CpuLoad();
            _cpuBar.Value = cpu;
            _cpuText.Text = $"{cpu:0}%";
            var m = SysInfo.Memory();
            _ramBar.Value = m.load;
            _ramText.Text = m.total > 0 ? $"{Fmt.Size((long)(m.total - m.free))} / {Fmt.Size((long)m.total)}" : "";
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
                _diskBar.Value = 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize;
                _diskText.Text = $"{Fmt.Size(d.TotalFreeSpace)} своб.";
                _diskBar.Foreground = d.TotalFreeSpace < 10L * 1024 * 1024 * 1024 ? Ui.B("WarningBrush") : null;
                if (_diskBar.Foreground == null) _diskBar.ClearValue(ForegroundProperty);
            }
            catch { }
        }

        private void UpdateScanTile()
        {
            var last = AppSettings.LastScan;
            _scanValue.Text = Fmt.Ago(last);
            _scanSub.Text = last == null ? "Запустите быструю проверку" : $"Найдено угроз: {AppSettings.LastScanThreats}";
        }

        private async void Refresh()
        {
            _heroTitle.Text = "Проверка состояния…";
            SecuritySummary s;
            try { s = await Task.Run(() => SecurityStatus.Evaluate()); }
            catch (Exception ex) { Log.Error("Статус", ex); return; }

            var brush = s.Level == 0 ? Ui.B("SuccessBrush") : s.Level == 1 ? Ui.B("WarningBrush") : Ui.B("DangerBrush");
            var c = ((SolidColorBrush)brush).Color;
            _heroIcon.Foreground = brush;
            _heroIcon.Text = s.Level == 0 ? "\uEA18" : "\uE7BA";
            _heroIconBg.Background = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));
            _heroTitle.Text = s.Title;
            _heroText.Text = s.Level == 0 ? "Защита работает, проблем не обнаружено." : "Мы нашли то, что стоит исправить:";
            _issues.Children.Clear();
            foreach (var issue in s.Issues)
            {
                var line = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
                var bullet = Ui.Icon("\uE76C", 10, Ui.B("TextTertiaryBrush"));
                bullet.Margin = new Thickness(0, 3, 8, 0);
                bullet.VerticalAlignment = VerticalAlignment.Top;
                line.Children.Add(bullet);
                line.Children.Add(Ui.Text(issue, 13, null, "TextSecondaryBrush"));
                _issues.Children.Add(line);
            }

            var active = s.Products.Where(p => p.Enabled).Select(p => p.Name).ToList();
            if (active.Count > 0) { _avValue.Text = "Включён"; _avSub.Text = string.Join(", ", active); }
            else if (s.Defender.Available) { _avValue.Text = s.Defender.RealTime ? "Включён" : "Выключен"; _avSub.Text = "Microsoft Defender"; }
            else { _avValue.Text = "Не найден"; _avSub.Text = "Установите антивирус"; }
            if (s.Defender.Available && s.Defender.SignatureUpdated != null)
                _avSub.Text += $" • базы {Fmt.Ago(s.Defender.SignatureUpdated)}";

            _fwValue.Text = s.Firewall == true ? "Включён" : s.Firewall == false ? "Выключен" : "Неизвестно";
            _fwSub.Text = s.Firewall == false ? "Нажмите, чтобы включить" : "Все профили сети";
            UpdateScanTile();
            Main?.RefreshStatus();
        }
    }
}
