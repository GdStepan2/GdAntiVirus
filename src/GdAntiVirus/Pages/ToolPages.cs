using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GdAntiVirus.Core;

namespace GdAntiVirus.Pages
{
    public sealed class AppsPage : PageBase
    {
        public override string Header => "Программы";
        public override string Subtitle => "Безопасная установка популярных программ из официальных источников (winget)";

        private readonly SemaphoreSlim _queue = new SemaphoreSlim(1);
        private readonly List<(AppEntry app, CheckBox box, TextBlock status, Border card)> _cards = new List<(AppEntry, CheckBox, TextBlock, Border)>();
        private TextBox _search;
        private bool _winget;
        private TextBlock _wingetInfo;

        protected override void Build()
        {
            var root = new StackPanel();
            _search = Ui.Input("Поиск программы…", 300);
            _search.TextChanged += (s, e) => Filter();
            var bar = new WrapPanel();
            bar.Children.Add(_search);
            foreach (var b in new[]
            {
                Ui.Button("Установить выбранные", InstallSelected, accent: true, glyph: "\uE896"),
                Ui.Button("Обновить все программы", () => Shell.Console("winget upgrade --all --accept-package-agreements --accept-source-agreements"), glyph: "\uE895"),
                Ui.Button("Удаление программ", () => Shell.Open("appwiz.cpl"), glyph: "\uE74D"),
            })
            {
                b.Margin = new Thickness(8, 0, 0, 0);
                bar.Children.Add(b);
            }
            root.Children.Add(bar);
            _wingetInfo = Ui.Caption("Проверка winget…");
            _wingetInfo.Margin = new Thickness(2, 10, 0, 0);
            root.Children.Add(_wingetInfo);

            foreach (var group in AppCatalog.All.GroupBy(a => a.Category))
            {
                root.Children.Add(Ui.Section(group.Key));
                var grid = new UniformGrid { Columns = 3 };
                foreach (var app in group) grid.Children.Add(Card(app));
                root.Children.Add(grid);
            }
            Content = Ui.Scroll(root);
            CheckWinget();
        }

        private async void CheckWinget()
        {
            _winget = await Task.Run(() => AppCatalog.WingetAvailable());
            if (_winget) _wingetInfo.Text = "Программы скачиваются с официальных сайтов разработчиков через Windows Package Manager. Установка идёт в фоне.";
            else
            {
                _wingetInfo.Text = "winget не найден. Установите «Установщик приложений» из Microsoft Store — или используйте кнопку «Сайт».";
                _wingetInfo.Foreground = Ui.B("WarningBrush");
            }
        }

        private Border Card(AppEntry app)
        {
            var box = new CheckBox { Visibility = app.WingetId == null ? Visibility.Hidden : Visibility.Visible, VerticalAlignment = VerticalAlignment.Top };
            var status = Ui.Caption(app.WingetId == null ? "Загрузка с сайта разработчика" : "winget: " + app.WingetId);
            status.Foreground = Ui.B("TextTertiaryBrush");
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.TextWrapping = TextWrapping.NoWrap;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icBg = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(8), Child = Ui.Icon(app.Glyph ?? "\uE7B8", 18), VerticalAlignment = VerticalAlignment.Top };
            icBg.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            g.Children.Add(icBg);
            var info = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
            info.Children.Add(Ui.Text(app.Name, 14, FontWeights.SemiBold));
            info.Children.Add(Ui.Caption(app.Description));
            info.Children.Add(status);
            Grid.SetColumn(info, 1); g.Children.Add(info);
            Grid.SetColumn(box, 2); g.Children.Add(box);

            var action = app.WingetId != null
                ? Ui.Button("Установить", null, glyph: "\uE896")
                : Ui.Button("Сайт", () => Shell.Open(app.Url), glyph: "\uE8A7");
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
            action.Margin = new Thickness(0, 12, 0, 0);
            var sp = new StackPanel();
            sp.Children.Add(g);
            sp.Children.Add(action);
            var card = Ui.Card(sp, new Thickness(14, 14, 14, 14));
            card.Margin = new Thickness(0, 0, 6, 6);
            if (app.WingetId != null) action.Click += async (s, e) => await Install(app, status, action);
            _cards.Add((app, box, status, card));
            return card;
        }

        private async Task Install(AppEntry app, TextBlock status, Button button)
        {
            if (!_winget) { Ui.Toast("winget недоступен на этом компьютере", ToastKind.Error); return; }
            button.IsEnabled = false;
            status.Text = "В очереди…";
            await _queue.WaitAsync();
            try
            {
                status.Text = "Загрузка и установка…";
                status.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                var r = await AppCatalog.InstallAsync(app);
                // 0x8A15002B = already installed / no applicable upgrade
                bool already = r.ExitCode == unchecked((int)0x8A15002B) || r.Output.IndexOf("already installed", StringComparison.OrdinalIgnoreCase) >= 0;
                if (r.Ok || already)
                {
                    status.Text = already ? "Уже установлено ✔" : "Установлено ✔";
                    status.Foreground = Ui.B("SuccessBrush");
                    Log.Info("Установлено: " + app.Name);
                    Ui.Toast($"{app.Name} установлен", ToastKind.Success);
                }
                else
                {
                    status.Text = $"Ошибка (код {r.ExitCode:X})";
                    status.Foreground = Ui.B("DangerBrush");
                    Log.Error($"winget {app.WingetId}: {r.ExitCode}\n{r.Output}");
                    Ui.Toast($"Не удалось установить {app.Name}. Подробности в журнале (Настройки).", ToastKind.Error);
                }
            }
            finally
            {
                _queue.Release();
                button.IsEnabled = true;
            }
        }

        private async void InstallSelected()
        {
            var sel = _cards.Where(c => c.box.IsChecked == true).ToList();
            if (sel.Count == 0) { Ui.Toast("Отметьте программы галочками", ToastKind.Warning); return; }
            Ui.Toast($"Устанавливаем программ: {sel.Count}", ToastKind.Info);
            var tasks = sel.Select(c =>
            {
                c.box.IsChecked = false;
                var btn = (Button)((StackPanel)c.card.Child).Children[1];
                return Install(c.app, c.status, btn);
            }).ToList();
            await Task.WhenAll(tasks);
        }

        private void Filter()
        {
            var q = _search.Text.Trim();
            foreach (var c in _cards)
                c.card.Visibility = q.Length == 0 || c.app.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    c.app.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public sealed class ToolsPage : PageBase
    {
        public override string Header => "Утилиты";
        public override string Subtitle => "Быстрый запуск системных приложений Windows";

        private static readonly (string group, string glyph, string title, string desc, string cmd, string args)[] Tools =
        {
            ("Основные", "\uE9D9", "Диспетчер задач", "Процессы и производительность", "taskmgr.exe", null),
            ("Основные", "\uE713", "Параметры Windows", "Все настройки системы", "ms-settings:", null),
            ("Основные", "\uE80F", "Панель управления", "Классические настройки", "control.exe", null),
            ("Основные", "\uE74C", "Программы и компоненты", "Удаление программ", "appwiz.cpl", null),
            ("Основные", "\uEA18", "Безопасность Windows", "Антивирус Microsoft Defender", "windowsdefender:", null),
            ("Основные", "\uE83D", "Брандмауэр", "Расширенные правила брандмауэра", "wf.msc", null),

            ("Командная строка", "\uE756", "Командная строка", "cmd от администратора", "cmd.exe", null),
            ("Командная строка", "\uE756", "PowerShell", "Оболочка PowerShell", "powershell.exe", null),
            ("Командная строка", "\uE70F", "Блокнот", "Текстовый редактор", "notepad.exe", null),
            ("Командная строка", "\uE8A7", "Выполнить", "Окно «Выполнить» (Win+R)", "explorer.exe", "shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}"),

            ("Администрирование", "\uE943", "Редактор реестра", "regedit", "regedit.exe", null),
            ("Администрирование", "\uE7E8", "Конфигурация системы", "msconfig — загрузка и службы", "msconfig.exe", null),
            ("Администрирование", "\uE912", "Службы", "Управление службами Windows", "services.msc", null),
            ("Администрирование", "\uE772", "Диспетчер устройств", "Драйверы и оборудование", "devmgmt.msc", null),
            ("Администрирование", "\uEDA2", "Управление дисками", "Разделы и тома", "diskmgmt.msc", null),
            ("Администрирование", "\uE7F4", "Управление компьютером", "Все оснастки в одном окне", "compmgmt.msc", null),
            ("Администрирование", "\uE81C", "Просмотр событий", "Журналы ошибок Windows", "eventvwr.msc", null),
            ("Администрирование", "\uE787", "Планировщик заданий", "Задачи по расписанию", "taskschd.msc", null),
            ("Администрирование", "\uE8D7", "Групповая политика", "gpedit (Pro и выше)", "gpedit.msc", null),
            ("Администрирование", "\uE77B", "Пользователи", "Учётные записи пользователей", "netplwiz.exe", null),

            ("Диагностика", "\uE9D2", "Монитор ресурсов", "Нагрузка на ЦП, диск и сеть", "resmon.exe", null),
            ("Диагностика", "\uE7F8", "Сведения о системе", "msinfo32", "msinfo32.exe", null),
            ("Диагностика", "\uE7FC", "Средство диагностики DirectX", "dxdiag", "dxdiag.exe", null),
            ("Диагностика", "\uE964", "Проверка памяти", "Тест оперативной памяти", "mdsched.exe", null),
            ("Диагностика", "\uE7A7", "Восстановление системы", "Откат к точке восстановления", "rstrui.exe", null),
            ("Диагностика", "\uE968", "Сетевые подключения", "Адаптеры сети", "ncpa.cpl", null),
            ("Диагностика", "\uE945", "Электропитание", "Схемы питания", "powercfg.cpl", null),
            ("Диагностика", "\uE765", "Экранная клавиатура", "osk", "osk.exe", null),
        };

        protected override void Build()
        {
            var root = new StackPanel();
            foreach (var group in Tools.GroupBy(t => t.group))
            {
                root.Children.Add(Ui.Section(group.Key));
                var grid = new UniformGrid { Columns = 3 };
                foreach (var t in group)
                {
                    var tool = t;
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.Children.Add(Ui.Icon(tool.glyph, 20));
                    var sp = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                    sp.Children.Add(Ui.Text(tool.title, 14, FontWeights.SemiBold, wrap: false));
                    sp.Children.Add(Ui.Caption(tool.desc));
                    Grid.SetColumn(sp, 1); g.Children.Add(sp);
                    var b = new Button { Style = Ui.S("TileButton"), Content = g };
                    b.Click += (s, e) =>
                    {
                        if (!Shell.Open(tool.cmd, tool.args)) Ui.Toast($"Не удалось открыть «{tool.title}» (возможно, недоступно в вашей редакции Windows)", ToastKind.Error);
                    };
                    grid.Children.Add(b);
                }
                root.Children.Add(grid);
            }
            Content = Ui.Scroll(root);
        }
    }

    public sealed class SystemInfoPage : PageBase
    {
        public override string Header => "О системе";
        public override string Subtitle => "Характеристики компьютера и Windows";

        private StackPanel _body;
        private string _plain = "";

        protected override void Build()
        {
            var root = new StackPanel();
            root.Children.Add(Ui.Wrap(
                Ui.Button("Обновить", Load, glyph: "\uE72C"),
                Ui.Button("Копировать всё", () => { try { Clipboard.SetText(_plain); Ui.Toast("Скопировано в буфер обмена", ToastKind.Success); } catch { } }, glyph: "\uE8C8")));
            _body = new StackPanel();
            root.Children.Add(_body);
            Content = Ui.Scroll(root);
            Load();
        }

        private async void Load()
        {
            _body.Children.Clear();
            var spin = Ui.HStack(Ui.Spinner(20), Ui.Text("Собираем информацию…", 14, null, "TextSecondaryBrush"));
            ((FrameworkElement)spin.Children[1]).Margin = new Thickness(12, 0, 0, 0);
            _body.Children.Add(spin);

            List<KeyValuePair<string, List<KeyValuePair<string, string>>>> data;
            try { data = await Task.Run(() => SysInfo.Collect()); }
            catch (Exception ex) { Log.Error("О системе", ex); _body.Children.Clear(); _body.Children.Add(Ui.Caption(ex.Message)); return; }

            _body.Children.Clear();
            var sb = new StringBuilder();
            foreach (var sec in data)
            {
                if (sec.Value.Count == 0) continue;
                _body.Children.Add(Ui.Section(sec.Key));
                sb.AppendLine("== " + sec.Key + " ==");
                var panel = new StackPanel();
                for (int i = 0; i < sec.Value.Count; i++)
                {
                    var kv = sec.Value[i];
                    sb.AppendLine(kv.Key + ": " + kv.Value);
                    var g = new Grid { Margin = new Thickness(0, 7, 0, 7) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.Children.Add(Ui.Text(kv.Key, 13, null, "TextSecondaryBrush"));
                    var v = Ui.Text(kv.Value, 13);
                    Grid.SetColumn(v, 1);
                    g.Children.Add(v);
                    if (i > 0) panel.Children.Add(new Border { Height = 1, Background = Ui.B("DividerBrush") });
                    panel.Children.Add(g);
                }
                _body.Children.Add(Ui.Card(panel, new Thickness(18, 8, 18, 8)));
            }
            _plain = sb.ToString();
        }
    }

    public sealed class SettingsPage : PageBase
    {
        public override string Header => "Настройки";
        public override string Subtitle => "Оформление, журнал и информация о программе";

        private TextBox _log;
        private TextBlock _updateText;

        protected override void Build()
        {
            var root = new StackPanel();

            root.Children.Add(Ui.Section("Оформление"));
            var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var hex in Theme.Accents)
            {
                var color = hex;
                var rb = new RadioButton
                {
                    Style = Ui.S("SwatchButton"), GroupName = "accent", ToolTip = color,
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)),
                    IsChecked = string.Equals(AppSettings.Accent, color, StringComparison.OrdinalIgnoreCase)
                };
                rb.Checked += (s, e) => { AppSettings.Accent = color; Theme.ApplyAccent(color); };
                swatches.Children.Add(rb);
            }
            var accentPanel = new StackPanel();
            accentPanel.Children.Add(Ui.Text("Цвет акцента", 14));
            accentPanel.Children.Add(Ui.Caption("Цвет кнопок, переключателей и выделения"));
            accentPanel.Children.Add(swatches);
            root.Children.Add(Ui.Card(accentPanel));

            root.Children.Add(Ui.Section("Поведение"));
            var confirm = Ui.Toggle(AppSettings.ConfirmActions);
            confirm.Click += (s, e) => AppSettings.ConfirmActions = confirm.IsChecked == true;
            root.Children.Add(Ui.Row("\uE8FB", "Подтверждать действия", "Спрашивать перед завершением процессов и очисткой", confirm));
            var upd = Ui.Toggle(AppSettings.CheckUpdates);
            upd.Click += (s, e) => AppSettings.CheckUpdates = upd.IsChecked == true;
            root.Children.Add(Ui.Row("\uE895", "Проверять обновления при открытии настроек", "Узнавать о новых версиях GdAntiVirus на GitHub", upd));

            root.Children.Add(Ui.Section("Журнал"));
            _log = Ui.Console(220);
            var logPanel = new StackPanel();
            logPanel.Children.Add(_log);
            var lb = Ui.Wrap(
                Ui.Button("Обновить", RefreshLog, glyph: "\uE72C"),
                Ui.Button("Открыть файл", () => Shell.Open("notepad.exe", "\"" + Log.FilePath + "\""), glyph: "\uE8E5"),
                Ui.Button("Очистить", () => { Log.Clear(); RefreshLog(); }, glyph: "\uE74D"));
            lb.Margin = new Thickness(0, 10, 0, 0);
            logPanel.Children.Add(lb);
            root.Children.Add(Ui.Card(logPanel));

            root.Children.Add(Ui.Section("О программе"));
            var about = new Grid();
            about.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            about.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var logo = new Image { Width = 56, Height = 56, Margin = new Thickness(0, 0, 18, 0), Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")) };
            about.Children.Add(logo);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Ui.Text("GdAntiVirus " + AppInfo.Version, 18, FontWeights.SemiBold));
            info.Children.Add(Ui.Caption("Автор: GdStepan2  •  Нашли ошибку или есть идея? Пишите: " + AppInfo.Email));
            _updateText = Ui.Caption("");
            info.Children.Add(_updateText);
            Grid.SetColumn(info, 1); about.Children.Add(info);
            var ab = Ui.HStack(
                Ui.Button("GitHub", () => Shell.Open("https://github.com/" + AppInfo.Repo), glyph: "\uE774"),
                Ui.Button("Проверить обновления", () => CheckUpdates(true), accent: true, glyph: "\uE895"));
            Grid.SetColumn(ab, 2); about.Children.Add(ab);
            root.Children.Add(Ui.Card(about, new Thickness(20, 18, 20, 18)));

            Content = Ui.Scroll(root);
            Log.Added += line => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) { _log.AppendText(line + "\n"); _log.ScrollToEnd(); } }));
        }

        protected override void OnShown()
        {
            RefreshLog();
            if (AppSettings.CheckUpdates && !App.IsSelfTest) CheckUpdates(false);
        }

        private void RefreshLog()
        {
            var text = Log.ReadAll();
            _log.Text = text.Length > 60000 ? text.Substring(text.Length - 60000) : text;
            _log.ScrollToEnd();
        }

        private async void CheckUpdates(bool manual)
        {
            _updateText.Text = "Проверка обновлений…";
            try
            {
                var (tag, url) = await Updater.LatestAsync();
                if (Updater.IsNewer(tag))
                {
                    _updateText.Text = $"Доступна новая версия {tag}!";
                    _updateText.Foreground = Ui.B("SuccessBrush");
                    if (manual && Ui.Confirm("Доступно обновление", $"Вышла версия {tag}. Открыть страницу загрузки?", "Открыть")) Shell.Open(url);
                }
                else
                {
                    _updateText.Text = "У вас последняя версия ✔";
                    if (manual) Ui.Toast("У вас последняя версия", ToastKind.Success);
                }
            }
            catch (Exception ex)
            {
                _updateText.Text = "Не удалось проверить обновления";
                if (manual) Ui.Toast("Нет связи с GitHub: " + ex.GetBaseException().Message, ToastKind.Error);
            }
        }
    }
}
