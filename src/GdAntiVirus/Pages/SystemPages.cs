using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using GdAntiVirus.Core;

namespace GdAntiVirus.Pages
{
    public sealed class ProcessesPage : PageBase
    {
        public override string Header => "Процессы";
        public override string Subtitle => "Собственный диспетчер задач: найдите и завершите подозрительные программы";

        private ListView _list;
        private TextBox _search;
        private TextBlock _count;
        private CheckBox _auto;
        private List<ProcessRow> _all = new List<ProcessRow>();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private string _sortBy = "Memory";
        private ListSortDirection _sortDir = ListSortDirection.Descending;
        private bool _loading;

        protected override void Build()
        {
            var root = new DockPanel();

            _search = Ui.Input("Поиск по имени, описанию или пути…", 300);
            _search.TextChanged += (s, e) => ApplyFilter();
            _auto = new CheckBox { Content = "Автообновление", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _auto.Checked += (s, e) => _timer.Start();
            _auto.Unchecked += (s, e) => _timer.Stop();
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            toolbar.Children.Add(_search);
            foreach (var b in new[]
            {
                Ui.Button("Обновить", Reload, glyph: "\uE72C"),
                Ui.Button("Завершить", () => Kill(false), glyph: "\uE711"),
                Ui.Button("Завершить дерево", () => Kill(true), glyph: "\uE8BB"),
                Ui.Button("Папка", () => { var r = Current(); if (r?.Path != null) Shell.ShowInExplorer(r.Path); }, glyph: "\uE838"),
                Ui.Button("Проверить", ScanSelected, glyph: "\uE721"),
            })
            {
                b.Margin = new Thickness(8, 0, 0, 0);
                toolbar.Children.Add(b);
            }
            toolbar.Children.Add(_auto);
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            _count = Ui.Caption("");
            _count.Margin = new Thickness(2, 8, 0, 8);
            DockPanel.SetDock(_count, Dock.Bottom);
            root.Children.Add(_count);

            var view = new GridView();
            view.Columns.Add(Col("Имя", "Name", 190));
            view.Columns.Add(Col("PID", "Pid", 70));
            view.Columns.Add(Col("Память", "MemoryText", 95, "Memory"));
            view.Columns.Add(Col("Описание", "Description", 260));
            view.Columns.Add(StatusCol());
            view.Columns.Add(Col("Путь", "Path", 420));
            _list = new ListView { View = view, SelectionMode = SelectionMode.Single };
            _list.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
            _list.MouseDoubleClick += (s, e) => { var r = Current(); if (r?.Path != null) Shell.ShowInExplorer(r.Path); };
            root.Children.Add(Ui.Card(_list, new Thickness(8)));

            Content = root;
            _timer.Tick += (s, e) => Reload();
            IsVisibleChanged += (s, e) => { if (!IsVisible) _timer.Stop(); else if (_auto.IsChecked == true) _timer.Start(); };
        }

        private static GridViewColumn Col(string header, string prop, double width, string sortKey = null)
        {
            return new GridViewColumn
            {
                Header = new GridViewColumnHeader { Content = header, Tag = sortKey ?? prop },
                DisplayMemberBinding = new Binding(prop), Width = width
            };
        }

        private static GridViewColumn StatusCol()
        {
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetBinding(TextBlock.TextProperty, new Binding("Status"));
            f.SetBinding(TextBlock.ForegroundProperty, new Binding("StatusBrush"));
            f.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            return new GridViewColumn
            {
                Header = new GridViewColumnHeader { Content = "Статус", Tag = "Status" },
                CellTemplate = new DataTemplate { VisualTree = f }, Width = 190
            };
        }

        protected override void OnShown() => Reload();

        private async void Reload()
        {
            if (_loading) return;
            _loading = true;
            try
            {
                var selectedPid = Current()?.Pid;
                _all = await Task.Run(() => Processes.List());
                ApplyFilter();
                if (selectedPid != null) _list.SelectedItem = _list.Items.Cast<ProcessRow>().FirstOrDefault(r => r.Pid == selectedPid);
            }
            catch (Exception ex) { Log.Error("Процессы", ex); }
            finally { _loading = false; }
        }

        private void ApplyFilter()
        {
            var q = _search.Text.Trim();
            IEnumerable<ProcessRow> rows = _all;
            if (q.Length > 0)
                rows = rows.Where(r => Has(r.Name, q) || Has(r.Description, q) || Has(r.Path, q) || r.Pid.ToString() == q);

            Func<ProcessRow, object> key;
            switch (_sortBy)
            {
                case "Name": key = r => r.Name; break;
                case "Pid": key = r => r.Pid; break;
                case "Description": key = r => r.Description; break;
                case "Path": key = r => r.Path ?? ""; break;
                case "Status": key = r => r.Status ?? ""; break;
                default: key = r => r.Memory; break;
            }
            var list = (_sortDir == ListSortDirection.Ascending ? rows.OrderBy(key) : rows.OrderByDescending(key)).ToList();
            _list.ItemsSource = list;
            int suspicious = _all.Count(r => !string.IsNullOrEmpty(r.Status));
            _count.Text = $"Процессов: {_all.Count}   •   Показано: {list.Count}   •   Требуют внимания: {suspicious}   •   Память: {Fmt.Size(_all.Sum(r => r.Memory))}";
        }

        private static bool Has(string s, string q) => s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        private void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is GridViewColumnHeader h) || !(h.Tag is string key)) return;
            _sortDir = _sortBy == key && _sortDir == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            if (_sortBy != key && key == "Memory") _sortDir = ListSortDirection.Descending;
            _sortBy = key;
            ApplyFilter();
        }

        private ProcessRow Current() => _list?.SelectedItem as ProcessRow;

        private async void Kill(bool tree)
        {
            var r = Current();
            if (r == null) { Ui.Toast("Выберите процесс в списке", ToastKind.Warning); return; }
            if (!Confirm("Завершить процесс?", $"{r.Name} (PID {r.Pid}){(tree ? " и все его дочерние процессы" : "")} будет закрыт. Несохранённые данные будут потеряны.", "Завершить", danger: true)) return;
            try
            {
                bool ok = await Task.Run(() => Processes.Kill(r.Pid, tree));
                Log.Info($"Процесс завершён: {r.Name} ({r.Pid})");
                Ui.Toast(ok ? $"{r.Name} завершён" : $"Не удалось завершить {r.Name}", ok ? ToastKind.Success : ToastKind.Error);
            }
            catch (Exception ex) { Ui.Toast("Не удалось завершить: " + ex.Message, ToastKind.Error); }
            await Task.Delay(400);
            Reload();
        }

        private void ScanSelected()
        {
            var r = Current();
            if (r?.Path == null || !File.Exists(r.Path)) { Ui.Toast("У процесса нет доступного файла", ToastKind.Warning); return; }
            Main.Navigate<ScannerPage>()?.ScanPaths("Проверка " + r.Name, new[] { r.Path });
        }
    }

    public sealed class StartupPage : PageBase
    {
        public override string Header => "Автозагрузка";
        public override string Subtitle => "Программы, запускающиеся вместе с Windows. Отключение ускоряет загрузку и останавливает вирусы";

        private StackPanel _list;
        private TextBlock _count;

        protected override void Build()
        {
            var root = new StackPanel();
            _count = Ui.Text("", 14, FontWeights.SemiBold);
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var buttons = Ui.HStack(
                Ui.Button("Обновить", Reload, glyph: "\uE72C"),
                Ui.Button("Папка автозагрузки", () => Shell.ShowInExplorer(Environment.GetFolderPath(Environment.SpecialFolder.Startup)), glyph: "\uE838"),
                Ui.Button("Планировщик заданий", () => Shell.Open("taskschd.msc"), glyph: "\uE787"));
            DockPanel.SetDock(buttons, Dock.Right);
            bar.Children.Add(buttons);
            bar.Children.Add(_count);
            root.Children.Add(bar);
            _list = new StackPanel();
            root.Children.Add(_list);
            Content = Ui.Scroll(root);
        }

        protected override void OnShown() => Reload();

        private void Reload()
        {
            _list.Children.Clear();
            var items = StartupManager.Load();
            _count.Text = $"Элементов: {items.Count}   •   Включено: {items.Count(i => i.Enabled)}";
            foreach (var group in items.GroupBy(i => i.Location))
            {
                _list.Children.Add(Ui.Section(group.Key));
                foreach (var item in group) _list.Children.Add(Row(item));
            }
            if (items.Count == 0) _list.Children.Add(Ui.Row("\uE73E", "Автозагрузка пуста", "Ни одна программа не запускается вместе с Windows.", null));
        }

        private Border Row(StartupItem item)
        {
            var toggle = Ui.Toggle(item.Enabled);
            toggle.IsEnabled = item.CanToggle;
            toggle.Click += (s, e) =>
            {
                try
                {
                    StartupManager.SetEnabled(item, toggle.IsChecked == true);
                    Ui.Toast($"«{item.Name}» {(item.Enabled ? "включено" : "отключено")} в автозагрузке", ToastKind.Success);
                }
                catch (Exception ex)
                {
                    toggle.IsChecked = item.Enabled;
                    Ui.Toast("Ошибка: " + ex.Message, ToastKind.Error);
                }
            };
            var del = Ui.Button("", () =>
            {
                if (!Ui.Confirm("Удалить из автозагрузки?", $"«{item.Name}» больше не будет запускаться.\nКоманда: {item.Command}\n\nРезервная копия сохранится в папке GdAntiVirus.", "Удалить", danger: true)) return;
                try { StartupManager.Delete(item); Ui.Toast("Удалено из автозагрузки", ToastKind.Success); }
                catch (Exception ex) { Ui.Toast("Ошибка: " + ex.Message, ToastKind.Error); }
                Reload();
            }, glyph: "\uE74D", style: "GhostButton");
            del.ToolTip = "Удалить";
            var open = Ui.Button("", () => Shell.ShowInExplorer(item.FilePath ?? item.Command), glyph: "\uE838", style: "GhostButton");
            open.ToolTip = "Показать файл";
            var right = Ui.HStack(open, del, toggle);
            toggle.Margin = new Thickness(16, 0, 0, 0);

            var desc = item.Command + (item.FileMissing ? "\n⚠ Файл не найден — запись можно удалить" : "");
            var row = Ui.Row(item.ShortcutPath != null ? "\uE71B" : "\uE7E8", item.Name, desc, right);
            if (item.FileMissing) row.BorderBrush = Ui.B("WarningBrush");
            return row;
        }
    }

    public sealed class CleanerPage : PageBase
    {
        public override string Header => "Очистка";
        public override string Subtitle => "Удаление временных файлов, кэша браузеров и мусора Windows";

        private readonly List<(CleanTarget target, CheckBox box, TextBlock size)> _items = new List<(CleanTarget, CheckBox, TextBlock)>();
        private TextBlock _total;
        private Button _analyze, _clean;
        private FrameworkElement _spinner;

        protected override void Build()
        {
            var root = new StackPanel();
            _total = Ui.Text("Нажмите «Анализ», чтобы узнать, сколько места можно освободить", 15, FontWeights.SemiBold);
            _spinner = Ui.Spinner(22);
            _spinner.Visibility = Visibility.Collapsed;
            _spinner.Margin = new Thickness(0, 0, 14, 0);
            _analyze = Ui.Button("Анализ", Analyze, glyph: "\uE721");
            _clean = Ui.Button("Очистить", Clean, accent: true, glyph: "\uE74D");
            var head = new DockPanel();
            var hb = Ui.HStack(_analyze, _clean);
            DockPanel.SetDock(hb, Dock.Right);
            head.Children.Add(hb);
            head.Children.Add(Ui.HStack(_spinner, _total));
            var sub = Ui.Caption($"Всего освобождено с помощью GdAntiVirus: {Fmt.Size(AppSettings.TotalCleaned)}");
            var hp = new StackPanel();
            hp.Children.Add(head);
            sub.Margin = new Thickness(0, 6, 0, 0);
            hp.Children.Add(sub);
            root.Children.Add(Ui.Card(hp, new Thickness(20, 18, 20, 18)));

            root.Children.Add(Ui.Section("Что очищать"));
            foreach (var t in Cleaner.Targets())
            {
                var box = new CheckBox { IsChecked = t.DefaultOn };
                var size = Ui.Text("—", 13, FontWeights.SemiBold, "TextSecondaryBrush");
                size.MinWidth = 80;
                size.TextAlignment = TextAlignment.Right;
                var row = Ui.Row(null, t.Title, t.Description, size);
                var g = (Grid)row.Child;
                box.Margin = new Thickness(2, 0, 16, 0);
                g.Children.Add(box); // column 0
                _items.Add((t, box, size));
                root.Children.Add(row);
            }

            root.Children.Add(Ui.Section("Инструменты Windows"));
            root.Children.Add(Ui.Row("\uE74D", "Очистка диска Windows", "Встроенная утилита cleanmgr, включая системные файлы",
                Ui.Button("Открыть", () => Shell.Open("cleanmgr.exe", "/d C"))));
            root.Children.Add(Ui.Row("\uEDA2", "Память устройства", "Контроль памяти и рекомендации по очистке",
                Ui.Button("Открыть", () => Shell.Open("ms-settings:storagesense"))));
            Content = Ui.Scroll(root);
        }

        private void Busy(bool busy)
        {
            _spinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _analyze.IsEnabled = _clean.IsEnabled = !busy;
        }

        private async void Analyze()
        {
            Busy(true);
            _total.Text = "Анализ…";
            long total = 0;
            foreach (var (t, box, size) in _items)
            {
                size.Text = "…";
                t.Size = await Task.Run(() => Cleaner.Measure(t));
                size.Text = Fmt.Size(t.Size);
                if (box.IsChecked == true) total += t.Size;
            }
            _total.Text = $"Можно освободить: {Fmt.Size(total)}";
            Busy(false);
        }

        private async void Clean()
        {
            var selected = _items.Where(i => i.box.IsChecked == true).ToList();
            if (selected.Count == 0) { Ui.Toast("Выберите, что очищать", ToastKind.Warning); return; }
            if (!Confirm("Начать очистку?", "Выбранные временные файлы и кэш будут удалены. Файлы, занятые программами, будут пропущены.", "Очистить")) return;
            Busy(true);
            _total.Text = "Очистка…";
            long freed = 0;
            foreach (var (t, box, size) in selected)
            {
                size.Text = "…";
                freed += await Task.Run(() => Cleaner.Clean(t));
                t.Size = await Task.Run(() => Cleaner.Measure(t));
                size.Text = Fmt.Size(t.Size);
            }
            AppSettings.TotalCleaned += freed;
            Log.Info("Очистка: освобождено " + Fmt.Size(freed));
            _total.Text = $"Освобождено: {Fmt.Size(freed)}";
            Ui.Toast($"Готово! Освобождено {Fmt.Size(freed)}", ToastKind.Success);
            Busy(false);
        }
    }
}
