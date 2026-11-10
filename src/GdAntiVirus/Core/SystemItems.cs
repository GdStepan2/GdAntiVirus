using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace GdAntiVirus.Core
{
    public sealed class StartupItem
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string Location { get; set; }
        public string FilePath { get; set; }
        public bool Enabled { get; set; }
        public bool CanToggle => ApprovedPath != null;
        public bool FileMissing => FilePath != null && !File.Exists(FilePath);

        internal RegistryHive Hive;
        internal string KeyPath;          // null for Startup folder items
        internal string ShortcutPath;     // Startup folder file
        internal RegistryHive ApprovedHive;
        internal string ApprovedPath;     // StartupApproved key (same mechanism as Task Manager)
        internal string ApprovedName;
    }

    /// <summary>Startup entries from Run keys and Startup folders; toggled through StartupApproved like Task Manager does.</summary>
    public static class StartupManager
    {
        private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunOnce = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
        private const string Run32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupItem> Load()
        {
            var list = new List<StartupItem>();
            AddRegistry(list, RegistryHive.CurrentUser, Run, "Реестр (пользователь)", Approved + "Run");
            AddRegistry(list, RegistryHive.LocalMachine, Run, "Реестр (все пользователи)", Approved + "Run");
            AddRegistry(list, RegistryHive.LocalMachine, Run32, "Реестр (32-бит)", Approved + "Run32");
            AddRegistry(list, RegistryHive.CurrentUser, RunOnce, "Однократный запуск (пользователь)", null);
            AddRegistry(list, RegistryHive.LocalMachine, RunOnce, "Однократный запуск (все)", null);
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Папка автозагрузки", RegistryHive.CurrentUser);
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Папка автозагрузки (все)", RegistryHive.LocalMachine);
            return list;
        }

        private static void AddRegistry(List<StartupItem> list, RegistryHive hive, string path, string location, string approved)
        {
            try
            {
                using (var k = Reg.Root(hive).OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (var name in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        var cmd = k.GetValue(name)?.ToString() ?? "";
                        var item = new StartupItem
                        {
                            Name = name, Command = cmd, Location = location, FilePath = ExtractPath(cmd),
                            Hive = hive, KeyPath = path, ApprovedHive = hive, ApprovedPath = approved, ApprovedName = name
                        };
                        item.Enabled = approved == null || IsApproved(hive, approved, name);
                        list.Add(item);
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Автозагрузка " + path + ": " + ex.Message); }
        }

        private static void AddFolder(List<StartupItem> list, string dir, string location, RegistryHive hive)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir))
                {
                    var fileName = Path.GetFileName(f);
                    if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(f), Command = f, Location = location, FilePath = f, ShortcutPath = f,
                        ApprovedHive = hive, ApprovedPath = Approved + "StartupFolder", ApprovedName = fileName,
                        Enabled = IsApproved(hive, Approved + "StartupFolder", fileName)
                    });
                }
            }
            catch (Exception ex) { Log.Warn("Автозагрузка " + dir + ": " + ex.Message); }
        }

        private static bool IsApproved(RegistryHive hive, string path, string name)
        {
            // First byte: even = enabled (02/06), odd = disabled (03/07). Missing = enabled.
            return !(Reg.Get(hive, path, name) is byte[] b && b.Length > 0 && (b[0] & 1) == 1);
        }

        public static void SetEnabled(StartupItem item, bool enabled)
        {
            if (!item.CanToggle) return;
            var data = new byte[12];
            data[0] = (byte)(enabled ? 0x02 : 0x03);
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            Reg.SetBinary(item.ApprovedHive, item.ApprovedPath, item.ApprovedName, data);
            item.Enabled = enabled;
            Log.Info($"Автозагрузка: {item.Name} → {(enabled ? "включено" : "отключено")}");
        }

        public static void Delete(StartupItem item)
        {
            if (item.ShortcutPath != null)
            {
                // Keep a backup instead of deleting the shortcut outright.
                var backup = AppInfo.Ensure(Path.Combine(AppInfo.DataDir, "StartupBackup"));
                File.Move(item.ShortcutPath, Path.Combine(backup, DateTime.Now.ToString("yyyyMMddHHmmss_") + Path.GetFileName(item.ShortcutPath)));
            }
            else
            {
                var backupFile = Path.Combine(AppInfo.Ensure(Path.Combine(AppInfo.DataDir, "StartupBackup")), "registry.txt");
                File.AppendAllText(backupFile, $"{DateTime.Now:s}\t{item.Hive}\\{item.KeyPath}\t{item.Name}\t{item.Command}{Environment.NewLine}", Encoding.UTF8);
                Reg.Delete(item.Hive, item.KeyPath, item.Name);
            }
            if (item.ApprovedPath != null) try { Reg.Delete(item.ApprovedHive, item.ApprovedPath, item.ApprovedName); } catch { }
            Log.Info("Удалено из автозагрузки: " + item.Name + " — " + item.Command);
        }

        public static string ExtractPath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            var c = Environment.ExpandEnvironmentVariables(command.Trim());
            string path;
            if (c.StartsWith("\""))
            {
                int end = c.IndexOf('"', 1);
                path = end > 1 ? c.Substring(1, end - 1) : c.Trim('"');
            }
            else
            {
                int i = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                path = i > 0 ? c.Substring(0, i + 4) : c.Split(' ')[0];
            }
            return path.Length > 2 && path[1] == ':' ? path : null;
        }
    }

    public sealed class ProcessRow
    {
        public string Name { get; set; }
        public int Pid { get; set; }
        public long Memory { get; set; }
        public string MemoryText => Fmt.Size(Memory);
        public string Description { get; set; }
        public string Path { get; set; }
        public string Status { get; set; }
        public System.Windows.Media.Brush StatusBrush { get; set; }
    }

    public static class Processes
    {
        private static readonly Dictionary<string, string> DescCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // System process names that malware often imitates; they must live in %windir%.
        private static readonly HashSet<string> SystemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "svchost", "lsass", "csrss", "winlogon", "services", "smss", "wininit", "explorer", "dllhost", "rundll32", "taskhostw", "conhost", "spoolsv", "lsm", "dwm", "sihost", "ctfmon" };

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        public static string GetPath(int pid)
        {
            var h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        public static List<ProcessRow> List()
        {
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var rows = new List<ProcessRow>();
            var danger = (System.Windows.Media.Brush)System.Windows.Application.Current.Dispatcher.Invoke(() => Ui.B("DangerBrush"));
            var warn = (System.Windows.Media.Brush)System.Windows.Application.Current.Dispatcher.Invoke(() => Ui.B("WarningBrush"));
            var ok = (System.Windows.Media.Brush)System.Windows.Application.Current.Dispatcher.Invoke(() => Ui.B("TextTertiaryBrush"));

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var row = new ProcessRow { Name = p.ProcessName, Pid = p.Id, Status = "", StatusBrush = ok };
                    try { row.Memory = p.WorkingSet64; } catch { }
                    row.Path = p.Id <= 4 ? null : GetPath(p.Id);
                    if (row.Path != null)
                    {
                        row.Description = Describe(row.Path);
                        bool inWin = row.Path.StartsWith(windir, StringComparison.OrdinalIgnoreCase);
                        if (SystemNames.Contains(row.Name) && !inWin)
                        { row.Status = "Подмена системного процесса"; row.StatusBrush = danger; }
                        else if (row.Path.IndexOf(@"\AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 row.Path.IndexOf(@"\Windows\Temp\", StringComparison.OrdinalIgnoreCase) >= 0)
                        { row.Status = "Запущен из Temp"; row.StatusBrush = warn; }
                        else if (row.Path.IndexOf(@"\Downloads\", StringComparison.OrdinalIgnoreCase) >= 0)
                        { row.Status = "Запущен из Загрузок"; row.StatusBrush = warn; }
                        else if (!File.Exists(row.Path))
                        { row.Status = "Файл удалён"; row.StatusBrush = warn; }
                    }
                    if (string.IsNullOrEmpty(row.Description)) row.Description = p.Id <= 4 ? "Система" : "";
                    rows.Add(row);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return rows;
        }

        private static string Describe(string path)
        {
            lock (DescCache)
                if (DescCache.TryGetValue(path, out var d)) return d;
            string desc = "";
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                desc = vi.FileDescription;
                if (string.IsNullOrWhiteSpace(desc)) desc = vi.ProductName;
                if (!string.IsNullOrWhiteSpace(vi.CompanyName)) desc = (desc ?? "") + "  •  " + vi.CompanyName;
            }
            catch { }
            lock (DescCache) DescCache[path] = desc ?? "";
            return desc ?? "";
        }

        public static bool Kill(int pid, bool tree)
        {
            if (tree)
            {
                var r = Shell.RunAsync("taskkill.exe", $"/PID {pid} /T /F").Result;
                return r.Ok;
            }
            using (var p = Process.GetProcessById(pid))
            {
                p.Kill();
                p.WaitForExit(3000);
            }
            return true;
        }
    }
}
