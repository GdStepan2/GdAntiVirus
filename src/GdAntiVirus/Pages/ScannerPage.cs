using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GdAntiVirus.Core;

namespace GdAntiVirus.Pages
{
    public sealed class ScannerPage : PageBase
    {
        public override string Header => "Сканер";
        public override string Subtitle => "Поиск вирусов, замаскированных программ и вредоносных скриптов";

        private Border _progressCard, _resultsCard;
        private TextBlock _progressTitle, _progressFiles, _progressCurrent, _resultsTitle;
        private StackPanel _results;
        private UniformGrid _modes;
        private CancellationTokenSource _cts;
        private readonly List<(Threat threat, CheckBox box, Border row)> _rows = new List<(Threat, CheckBox, Border)>();
        private TextBox _defenderLog;
        private CancellationTokenSource _defCts;

        protected override void Build()
        {
            var root = new StackPanel();

            _modes = new UniformGrid { Columns = 3 };
            _modes.Children.Add(Mode("\uE721", "Быстрая проверка", "Автозагрузка, Temp, Загрузки, Рабочий стол и флешки. 1–3 минуты.", StartQuick, true));
            _modes.Children.Add(Mode("\uEDA2", "Полная проверка", "Все локальные и съёмные диски. Может занять долгое время.", StartFull, false));
            _modes.Children.Add(Mode("\uE8B7", "Выборочная проверка", "Выберите папку или файл для проверки.", StartCustom, false));
            root.Children.Add(_modes);

            // Progress
            _progressTitle = Ui.Text("Идёт проверка…", 16, FontWeights.SemiBold);
            _progressFiles = Ui.Caption("");
            _progressCurrent = Ui.Text("", 12, null, "TextTertiaryBrush", wrap: false);
            var stop = Ui.Button("Остановить", () => _cts?.Cancel(), glyph: "\uE71A");
            var pg = new Grid();
            pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var spin = Ui.Spinner(28);
            spin.Margin = new Thickness(4, 0, 20, 0);
            pg.Children.Add(spin);
            var pinfo = new StackPanel();
            pinfo.Children.Add(_progressTitle);
            pinfo.Children.Add(_progressFiles);
            pinfo.Children.Add(_progressCurrent);
            Grid.SetColumn(pinfo, 1); pg.Children.Add(pinfo);
            Grid.SetColumn(stop, 2); pg.Children.Add(stop);
            _progressCard = Ui.Card(pg, new Thickness(20, 16, 20, 16));
            _progressCard.Visibility = Visibility.Collapsed;
            _progressCard.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(_progressCard);

            // Results
            _resultsTitle = Ui.Text("", 16, FontWeights.SemiBold);
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var actions = Ui.HStack(
                Ui.Button("Выбрать всё", () => _rows.ForEach(r => r.box.IsChecked = true)),
                Ui.Button("В карантин", QuarantineSelected, accent: true, glyph: "\uE72E"),
                Ui.Button("Удалить", DeleteSelected, glyph: "\uE74D"),
                Ui.Button("Проверить в Defender", DefenderSelected, glyph: "\uEA18"));
            DockPanel.SetDock(actions, Dock.Right);
            bar.Children.Add(actions);
            bar.Children.Add(_resultsTitle);
            _results = new StackPanel();
            var rp = new StackPanel();
            rp.Children.Add(bar);
            rp.Children.Add(_results);
            _resultsCard = Ui.Card(rp, new Thickness(20, 16, 20, 16));
            _resultsCard.Visibility = Visibility.Collapsed;
            _resultsCard.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(_resultsCard);

            // Defender
            root.Children.Add(Ui.Section("Microsoft Defender"));
            root.Children.Add(Ui.Row("\uEA18", "Проверка встроенным антивирусом Windows",
                "Глубокая проверка движком Defender. Результаты появятся ниже и в «Безопасность Windows».",
                Ui.HStack(
                    Ui.Button("Быстрая", () => RunDefender("-Scan -ScanType 1", "Быстрая проверка Defender")),
                    Ui.Button("Полная", () => RunDefender("-Scan -ScanType 2", "Полная проверка Defender")),
                    Ui.Button("Офлайн-проверка", () => OfflineScan()))));
            root.Children.Add(Ui.Row("\uE895", "Обновить антивирусные базы", "Загрузить свежие сигнатуры Defender",
                Ui.Button("Обновить", () => RunDefender("-SignatureUpdate", "Обновление баз Defender"), accent: true)));
            _defenderLog = Ui.Console(150);
            _defenderLog.Text = "Здесь будет вывод Defender…";
            var stopDef = Ui.Button("Остановить", () => _defCts?.Cancel());
            var logPanel = new StackPanel();
            logPanel.Children.Add(_defenderLog);
            stopDef.HorizontalAlignment = HorizontalAlignment.Left;
            stopDef.Margin = new Thickness(0, 8, 0, 0);
            logPanel.Children.Add(stopDef);
            root.Children.Add(Ui.Card(logPanel));

            root.Children.Add(Ui.Section("Своя база сигнатур"));
            root.Children.Add(Ui.Row("\uE8A5", "Список SHA-256 хэшей вредоносных файлов",
                "Добавьте свои хэши — сканер будет находить совпадающие файлы. Файл: " + Scanner.SignaturesFile,
                Ui.Button("Открыть", () => { new Scanner(); Shell.Open("notepad.exe", "\"" + Scanner.SignaturesFile + "\""); })));

            Content = Ui.Scroll(root);
        }

        private Button Mode(string glyph, string title, string desc, Action click, bool primary)
        {
            var sp = new StackPanel();
            var ic = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Child = Ui.Icon(glyph, 20) };
            ic.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            sp.Children.Add(ic);
            var t = Ui.Text(title, 15, FontWeights.SemiBold);
            t.Margin = new Thickness(0, 14, 0, 4);
            sp.Children.Add(t);
            sp.Children.Add(Ui.Caption(desc));
            var go = Ui.Text(primary ? "Рекомендуется  →" : "Запустить  →", 13, FontWeights.SemiBold);
            go.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            go.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(go);
            var b = new Button { Style = Ui.S("TileButton"), Content = sp, Padding = new Thickness(18, 18, 18, 16), VerticalContentAlignment = VerticalAlignment.Top };
            b.Click += (s, e) => click();
            return b;
        }

        public void StartQuick() => Start("Быстрая проверка", () => Scanner.QuickTargets());
        private void StartFull() => Start("Полная проверка", () => Scanner.FullTargets());

        private void StartCustom()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Выберите папку для проверки", ShowNewFolderButton = false })
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var path = dlg.SelectedPath;
                    Start("Проверка папки", () => new List<string> { path });
                }
        }

        public void ScanPaths(string title, IEnumerable<string> paths)
        {
            var list = paths.ToList();
            Start(title, () => list);
        }

        private async void Start(string title, Func<List<string>> targets)
        {
            if (_cts != null) { Ui.Toast("Проверка уже идёт", ToastKind.Warning); return; }
            _cts = new CancellationTokenSource();
            _modes.IsEnabled = false;
            _progressCard.Visibility = Visibility.Visible;
            _resultsCard.Visibility = Visibility.Collapsed;
            _progressTitle.Text = title + "…";
            _progressFiles.Text = "Подготовка списка папок";
            _progressCurrent.Text = "";
            Log.Info("Сканер: " + title);

            var progress = new Progress<ScanProgress>(p =>
            {
                _progressFiles.Text = $"Проверено файлов: {p.Files:N0}   •   Найдено: {p.Threats}";
                _progressCurrent.Text = p.Current;
            });

            List<Threat> threats = null;
            bool cancelled = false;
            var started = DateTime.Now;
            try
            {
                var token = _cts.Token;
                threats = await Task.Run(() => new Scanner().Scan(targets(), progress, token), token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { Log.Error("Сканер", ex); Ui.Toast("Ошибка проверки: " + ex.Message, ToastKind.Error); }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _modes.IsEnabled = true;
                _progressCard.Visibility = Visibility.Collapsed;
            }

            if (cancelled) { Ui.Toast("Проверка остановлена", ToastKind.Warning); return; }
            if (threats == null) return;

            AppSettings.LastScan = DateTime.Now;
            AppSettings.LastScanThreats = threats.Count(t => t.Level != ThreatLevel.Warning);
            Log.Info($"Сканер: завершено за {(DateTime.Now - started).TotalSeconds:0} с, найдено {threats.Count}");
            ShowResults(threats);
            Main.RefreshStatus();
        }

        private void ShowResults(List<Threat> threats)
        {
            _rows.Clear();
            _results.Children.Clear();
            _resultsCard.Visibility = Visibility.Visible;
            if (threats.Count == 0)
            {
                _resultsTitle.Text = "Угроз не найдено";
                var ok = Ui.HStack(Ui.Icon("\uE73E", 18, Ui.B("SuccessBrush")), Ui.Text("Подозрительных файлов не обнаружено. Для глубокой проверки запустите также Defender ниже.", 13, null, "TextSecondaryBrush"));
                ((FrameworkElement)ok.Children[1]).Margin = new Thickness(10, 0, 0, 0);
                _results.Children.Add(ok);
                Ui.Toast("Проверка завершена: угроз не найдено", ToastKind.Success);
                return;
            }

            _resultsTitle.Text = $"Найдено: {threats.Count}";
            foreach (var t in threats.OrderBy(t => t.Level).Take(500)) AddRow(t);
            Ui.Toast($"Найдено подозрительных объектов: {threats.Count}", threats.Any(t => t.Level == ThreatLevel.Danger) ? ToastKind.Error : ToastKind.Warning);
        }

        private void AddRow(Threat t)
        {
            var brush = t.Level == ThreatLevel.Danger ? Ui.B("DangerBrush") : t.Level == ThreatLevel.Suspicious ? Ui.B("WarningBrush") : Ui.B("TextSecondaryBrush");
            var box = new CheckBox { IsChecked = t.Level != ThreatLevel.Warning, VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(box);
            var info = new StackPanel { Margin = new Thickness(14, 0, 12, 0) };
            var title = Ui.HStack(Ui.Badge(t.LevelText, brush), Ui.Text(t.FileName, 14, FontWeights.SemiBold, wrap: false));
            info.Children.Add(title);
            var reason = Ui.Text(t.Reason, 12, null, "TextSecondaryBrush");
            reason.Margin = new Thickness(0, 4, 0, 0);
            info.Children.Add(reason);
            info.Children.Add(Ui.Text(t.Path + "  •  " + Fmt.Size(t.Size), 11, null, "TextTertiaryBrush", wrap: false));
            Grid.SetColumn(info, 1); g.Children.Add(info);
            var open = Ui.Button("", () => Shell.ShowInExplorer(t.Path), glyph: "\uE838", style: "GhostButton");
            open.ToolTip = "Показать в папке";
            Grid.SetColumn(open, 2); g.Children.Add(open);
            var row = new Border { Padding = new Thickness(8, 10, 8, 10), BorderBrush = Ui.B("DividerBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
            _rows.Add((t, box, row));
            _results.Children.Add(row);
        }

        private List<(Threat threat, CheckBox box, Border row)> Selected() => _rows.Where(r => r.box.IsChecked == true).ToList();

        private void QuarantineSelected()
        {
            var sel = Selected();
            if (sel.Count == 0) { Ui.Toast("Выберите файлы", ToastKind.Warning); return; }
            int ok = 0;
            foreach (var r in sel)
            {
                try { Quarantine.Add(r.threat.Path, r.threat.Reason); Remove(r); ok++; }
                catch (Exception ex) { Log.Error("Карантин " + r.threat.Path, ex); }
            }
            Ui.Toast(ok == sel.Count ? $"Перемещено в карантин: {ok}" : $"В карантин: {ok} из {sel.Count}. Остальные заняты или нет доступа.",
                ok == sel.Count ? ToastKind.Success : ToastKind.Warning);
            Main.RefreshStatus();
        }

        private void DeleteSelected()
        {
            var sel = Selected();
            if (sel.Count == 0) { Ui.Toast("Выберите файлы", ToastKind.Warning); return; }
            if (!Ui.Confirm("Удалить файлы?", $"Будет безвозвратно удалено файлов: {sel.Count}. Безопаснее отправить их в карантин.", "Удалить", danger: true)) return;
            int ok = 0;
            foreach (var r in sel)
            {
                try { File.SetAttributes(r.threat.Path, FileAttributes.Normal); File.Delete(r.threat.Path); Log.Info("Удалён: " + r.threat.Path); Remove(r); ok++; }
                catch (Exception ex) { Log.Error("Удаление " + r.threat.Path, ex); }
            }
            Ui.Toast($"Удалено: {ok} из {sel.Count}", ok == sel.Count ? ToastKind.Success : ToastKind.Warning);
        }

        private void DefenderSelected()
        {
            var sel = Selected();
            if (sel.Count == 0) { Ui.Toast("Выберите файлы", ToastKind.Warning); return; }
            var first = sel[0].threat.Path;
            RunDefender($"-Scan -ScanType 3 -File \"{first}\" -DisableRemediation", "Defender: " + Path.GetFileName(first));
        }

        private void Remove((Threat threat, CheckBox box, Border row) r)
        {
            _rows.Remove(r);
            _results.Children.Remove(r.row);
            _resultsTitle.Text = _rows.Count == 0 ? "Готово — угрозы обработаны" : $"Осталось: {_rows.Count}";
        }

        private async void RunDefender(string args, string title)
        {
            if (!Defender.HasMpCmdRun) { Ui.Toast("Microsoft Defender не найден на этом компьютере", ToastKind.Error); return; }
            if (_defCts != null) { Ui.Toast("Defender уже выполняет задачу", ToastKind.Warning); return; }
            _defCts = new CancellationTokenSource();
            _defenderLog.Text = $"▶ {title}\n";
            Ui.Toast(title + " запущена", ToastKind.Info);
            void Append(string line) => Dispatcher.BeginInvoke(new Action(() => { _defenderLog.AppendText(line + "\n"); _defenderLog.ScrollToEnd(); }));
            var r = await Shell.RunAsync(Defender.MpCmdRun, args, _defCts.Token, Append);
            _defCts.Dispose();
            _defCts = null;
            _defenderLog.AppendText(r.Ok ? "✔ Готово\n" : $"Код завершения: {r.ExitCode}\n");
            if (args.StartsWith("-Scan") && r.ExitCode == 2)
                Ui.Toast("Defender обнаружил угрозы! Откройте «Безопасность Windows».", ToastKind.Error);
            else
                Ui.Toast(title + (r.Ok ? ": готово" : ": завершено с кодом " + r.ExitCode), r.Ok ? ToastKind.Success : ToastKind.Warning);
            Main.RefreshStatus();
        }

        private async void OfflineScan()
        {
            if (!Ui.Confirm("Офлайн-проверка", "Компьютер перезагрузится и Defender проверит систему до запуска Windows (около 15 минут). Сохраните открытые документы.", "Перезагрузить и проверить", danger: true)) return;
            var r = await Shell.PowerShellAsync("Start-MpWDOScan");
            if (!r.Ok) Ui.Toast("Не удалось запустить офлайн-проверку: " + r.Output.Trim(), ToastKind.Error);
        }
    }

    public sealed class QuarantinePage : PageBase
    {
        public override string Header => "Карантин";
        public override string Subtitle => "Изолированные файлы не могут запуститься. Их можно восстановить или удалить навсегда";

        private StackPanel _list;
        private TextBlock _count;

        protected override void Build()
        {
            var root = new StackPanel();
            _count = Ui.Text("", 14, FontWeights.SemiBold);
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var buttons = Ui.HStack(
                Ui.Button("Обновить", Reload, glyph: "\uE72C"),
                Ui.Button("Открыть папку", () => Shell.ShowInExplorer(Quarantine.Dir), glyph: "\uE838"),
                Ui.Button("Очистить карантин", ClearAll, glyph: "\uE74D"));
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
            var items = Quarantine.Load().OrderByDescending(i => i.Date).ToList();
            _count.Text = items.Count == 0 ? "Карантин пуст" : $"Файлов в карантине: {items.Count}";
            if (items.Count == 0)
            {
                _list.Children.Add(Ui.Row("\uE73E", "Здесь пусто", "Найденные сканером угрозы, отправленные в карантин, появятся здесь.", null));
                return;
            }
            foreach (var item in items)
            {
                var it = item;
                _list.Children.Add(Ui.Row("\uE72E", Path.GetFileName(it.OriginalPath),
                    $"{it.Reason}\n{it.OriginalPath}  •  {it.Date:dd.MM.yyyy HH:mm}",
                    Ui.HStack(
                        Ui.Button("Восстановить", () => Restore(it)),
                        Ui.Button("Удалить", () => Delete(it), glyph: "\uE74D"))));
            }
        }

        private void Restore(QuarantineItem it)
        {
            if (!Ui.Confirm("Восстановить файл?", "Восстанавливайте только если уверены, что файл безопасен:\n" + it.OriginalPath, "Восстановить")) return;
            try { Quarantine.Restore(it); Ui.Toast("Файл восстановлен", ToastKind.Success); }
            catch (Exception ex) { Ui.Toast("Ошибка: " + ex.Message, ToastKind.Error); }
            Reload();
        }

        private void Delete(QuarantineItem it)
        {
            try { Quarantine.Delete(it); Ui.Toast("Файл удалён навсегда", ToastKind.Success); }
            catch (Exception ex) { Ui.Toast("Ошибка: " + ex.Message, ToastKind.Error); }
            Reload();
            Main.RefreshStatus();
        }

        private void ClearAll()
        {
            var items = Quarantine.Load();
            if (items.Count == 0) return;
            if (!Ui.Confirm("Очистить карантин?", $"Будет безвозвратно удалено файлов: {items.Count}.", "Удалить всё", danger: true)) return;
            foreach (var it in items) try { Quarantine.Delete(it); } catch { }
            Reload();
            Main.RefreshStatus();
            Ui.Toast("Карантин очищен", ToastKind.Success);
        }
    }
}
