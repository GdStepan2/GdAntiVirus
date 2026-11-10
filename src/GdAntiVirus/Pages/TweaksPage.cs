using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GdAntiVirus.Core;

namespace GdAntiVirus.Pages
{
    /// <summary>Generic page of toggle tweaks grouped into sections.</summary>
    public sealed class TweaksPage : PageBase
    {
        private readonly string _header, _subtitle;
        private readonly TweakGroup[] _groups;
        private readonly System.Collections.Generic.List<(Tweak tweak, CheckBox toggle)> _toggles = new System.Collections.Generic.List<(Tweak, CheckBox)>();

        public TweaksPage(string header, string subtitle, params TweakGroup[] groups)
        {
            _header = header; _subtitle = subtitle; _groups = groups;
        }

        public override string Header => _header;
        public override string Subtitle => _subtitle;

        protected override void Build()
        {
            var root = new StackPanel();
            root.Children.Add(Ui.Wrap(
                Ui.Button("Перезапустить проводник", async () => { await Shell.RestartExplorerAsync(); Ui.Toast("Проводник перезапущен", ToastKind.Success); }, glyph: "\uE72C"),
                Ui.Button("Создать точку восстановления", () => SecurityPage.CreateRestorePoint(), glyph: "\uE777")));
            if (!AppInfo.IsAdmin)
                root.Children.Add(Ui.Row("\uE7BA", "Нет прав администратора", "Часть настроек не применится. Запустите GdAntiVirus от имени администратора.", null));

            foreach (var g in _groups) AddGroup(root, g);
            Content = Ui.Scroll(root);
        }

        internal void AddGroup(Panel root, TweakGroup g)
        {
            root.Children.Add(Ui.Section(g.Title));
            foreach (var t in g.Items)
            {
                var tweak = t;
                bool on = false;
                try { on = tweak.Get(); } catch { }
                var toggle = Ui.Toggle(on);
                bool unsupported = tweak.Win11Only && !AppInfo.IsWindows11;
                toggle.IsEnabled = !unsupported;
                var desc = tweak.Description + (unsupported ? "  (только Windows 11)" : "");
                toggle.Click += (s, e) => Apply(tweak, toggle);
                _toggles.Add((tweak, toggle));
                root.Children.Add(Ui.Row(tweak.Glyph, tweak.Title, desc, toggle));
            }
        }

        protected override void OnShown()
        {
            foreach (var (tweak, toggle) in _toggles)
                try { toggle.IsChecked = tweak.Get(); } catch { }
        }

        internal static void Apply(Tweak tweak, CheckBox toggle)
        {
            bool want = toggle.IsChecked == true;
            try
            {
                tweak.Set(want);
                bool now = tweak.Get();
                toggle.IsChecked = now;
                if (now != want) throw new InvalidOperationException("значение не сохранилось (возможно, блокируется политикой)");
                Log.Info($"Твик «{tweak.Title}» → {(want ? "вкл" : "выкл")}");
                var hint = tweak.NeedsReboot ? " Изменение вступит в силу после перезагрузки." :
                           tweak.RestartExplorer ? " Перезапустите проводник, чтобы увидеть результат." : "";
                Ui.Toast($"«{tweak.Title}» {(want ? "включено" : "выключено")}.{hint}", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("Твик " + tweak.Title, ex);
                try { toggle.IsChecked = tweak.Get(); } catch { toggle.IsChecked = !want; }
                Ui.Toast("Не удалось применить: " + ex.Message, ToastKind.Error);
            }
        }
    }

    public sealed class SecurityPage : PageBase
    {
        public override string Header => "Безопасность";
        public override string Subtitle => "Защитные настройки Windows и инструменты восстановления";

        private TextBlock _hostsInfo;
        private readonly TweaksPage _tweaks = new TweaksPage("", "", Tweaks.Protection);

        protected override void Build()
        {
            var root = new StackPanel();

            _tweaks.AddGroup(root, Tweaks.Protection);

            root.Children.Add(Ui.Section("Сеть и брандмауэр"));
            root.Children.Add(Ui.Row("\uE83D", "Включить брандмауэр Windows", "Включает брандмауэр для всех профилей сети",
                Ui.Button("Включить", () => Run("netsh.exe", "advfirewall set allprofiles state on", "Брандмауэр включён"), accent: true)));
            _hostsInfo = Ui.Caption("");
            var hostsRow = Ui.Row("\uE774", "Файл hosts", "Вирусы часто подменяют его, чтобы блокировать сайты антивирусов или перенаправлять на фишинг",
                Ui.HStack(
                    Ui.Button("Открыть", () => Shell.Open("notepad.exe", "\"" + Hosts.FilePath + "\"")),
                    Ui.Button("Сбросить", ResetHosts)));
            ((Panel)((Grid)hostsRow.Child).Children[1]).Children.Add(_hostsInfo);
            root.Children.Add(hostsRow);
            root.Children.Add(Ui.Row("\uE895", "Очистить DNS-кэш", "Помогает, если сайты открываются не туда",
                Ui.Button("Очистить", () => Run("ipconfig.exe", "/flushdns", "DNS-кэш очищен"))));
            root.Children.Add(Ui.Row("\uE968", "Сбросить сетевые настройки", "Сброс Winsock и TCP/IP — лечит интернет после вирусов и прокси. Нужна перезагрузка",
                Ui.Button("Сбросить", ResetNetwork)));
            root.Children.Add(Ui.Row("\uE774", "Отключить системный прокси", "Вредоносные программы иногда направляют трафик через свой прокси",
                Ui.Button("Отключить", DisableProxy)));

            root.Children.Add(Ui.Section("Восстановление системы"));
            root.Children.Add(Ui.Row("\uE777", "Создать точку восстановления", "Сохраните состояние системы перед изменениями",
                Ui.Button("Создать", () => CreateRestorePoint(), accent: true)));
            root.Children.Add(Ui.Row("\uE7A7", "Откат к точке восстановления", "Открыть мастер восстановления системы",
                Ui.Button("Открыть", () => Shell.Open("rstrui.exe"))));
            root.Children.Add(Ui.Row("\uE90F", "Проверка системных файлов (SFC)", "Находит и восстанавливает повреждённые файлы Windows",
                Ui.Button("Запустить", () => Shell.Console("sfc /scannow"))));
            root.Children.Add(Ui.Row("\uE90F", "Восстановление образа Windows (DISM)", "Чинит хранилище компонентов, если SFC не помог",
                Ui.Button("Запустить", () => Shell.Console("DISM /Online /Cleanup-Image /RestoreHealth"))));
            root.Children.Add(Ui.Row("\uEDA2", "Проверка диска", "Проверить системный диск на ошибки при следующей загрузке",
                Ui.Button("Запланировать", () => Shell.Console("chkdsk C: /f"))));

            root.Children.Add(Ui.Section("Windows"));
            root.Children.Add(Ui.Row("\uEA18", "Безопасность Windows", "Защитник, брандмауэр, защита от программ-шантажистов",
                Ui.Button("Открыть", () => Shell.Open("windowsdefender:"))));
            root.Children.Add(Ui.Row("\uE895", "Центр обновления Windows", "Установите обновления безопасности",
                Ui.Button("Открыть", () => Shell.Open("ms-settings:windowsupdate"))));

            Content = Ui.Scroll(root);
        }

        protected override void OnShown()
        {
            var entries = Hosts.Entries();
            _hostsInfo.Text = entries.Count == 0 ? "Файл в исходном состоянии ✔" : $"Записей: {entries.Count}. Проверьте их: " + string.Join("; ", entries.Take(3)) + (entries.Count > 3 ? "…" : "");
            _hostsInfo.Foreground = entries.Count == 0 ? Ui.B("SuccessBrush") : Ui.B("WarningBrush");
        }

        private static async void Run(string file, string args, string success)
        {
            var r = await Shell.RunAsync(file, args);
            Ui.Toast(r.Ok ? success : "Ошибка: " + r.Output.Trim(), r.Ok ? ToastKind.Success : ToastKind.Error);
            Ui.Main?.RefreshStatus();
        }

        private void ResetHosts()
        {
            if (!Ui.Confirm("Сбросить hosts?", "Файл hosts будет заменён стандартным. Резервная копия сохранится рядом.", "Сбросить")) return;
            try { var b = Hosts.Reset(); Ui.Toast("hosts сброшен. Копия: " + b, ToastKind.Success); }
            catch (Exception ex) { Ui.Toast("Ошибка: " + ex.Message, ToastKind.Error); }
            OnShown();
        }

        private async void ResetNetwork()
        {
            if (!Ui.Confirm("Сбросить сеть?", "Будут сброшены Winsock и TCP/IP. После этого нужно перезагрузить компьютер.", "Сбросить")) return;
            var a = await Shell.RunAsync("netsh.exe", "winsock reset");
            var b = await Shell.RunAsync("netsh.exe", "int ip reset");
            await Shell.RunAsync("ipconfig.exe", "/flushdns");
            Ui.Toast(a.Ok ? "Сеть сброшена. Перезагрузите компьютер." : "Ошибка: " + a.Output.Trim() + b.Output.Trim(), a.Ok ? ToastKind.Success : ToastKind.Error);
        }

        private static void DisableProxy()
        {
            try
            {
                const string k = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
                Reg.SetInt(Microsoft.Win32.RegistryHive.CurrentUser, k, "ProxyEnable", 0);
                Reg.Delete(Microsoft.Win32.RegistryHive.CurrentUser, k, "AutoConfigURL");
                Log.Info("Системный прокси отключён");
                Ui.Toast("Прокси отключён", ToastKind.Success);
            }
            catch (Exception ex) { Ui.Toast("Ошибка: " + ex.Message, ToastKind.Error); }
        }

        public static async void CreateRestorePoint()
        {
            if (App.IsSelfTest) return;
            Ui.Toast("Создаём точку восстановления… это может занять минуту", ToastKind.Info);
            try
            {
                // Windows allows one point per 24h by default; lift that limit.
                Reg.SetInt(Microsoft.Win32.RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0);
            }
            catch { }
            var drive = System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            var r = await Shell.PowerShellAsync($"Enable-ComputerRestore -Drive '{drive}' -ErrorAction SilentlyContinue; Checkpoint-Computer -Description 'GdAntiVirus' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop");
            if (r.Ok) { Log.Info("Точка восстановления создана"); Ui.Toast("Точка восстановления создана", ToastKind.Success); }
            else { Log.Error("Точка восстановления: " + r.Output); Ui.Toast("Не удалось создать точку: " + r.Output.Trim().Split('\n')[0], ToastKind.Error); }
        }
    }
}
