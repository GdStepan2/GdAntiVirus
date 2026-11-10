using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GdAntiVirus.Core
{
    public static class SysInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();

        public static (ulong total, ulong free, uint load) Memory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref m) ? (m.ullTotalPhys, m.ullAvailPhys, m.dwMemoryLoad) : (0UL, 0UL, 0U);
        }

        private static long _lastIdle, _lastTotal;

        /// <summary>CPU load since the previous call (0-100). Locale-independent unlike PerformanceCounter.</summary>
        public static double CpuLoad()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            long total = kernel + user; // kernel time includes idle
            long dIdle = idle - _lastIdle, dTotal = total - _lastTotal;
            _lastIdle = idle; _lastTotal = total;
            if (dTotal <= 0) return 0;
            return Math.Max(0, Math.Min(100, 100.0 * (dTotal - dIdle) / dTotal));
        }

        public static TimeSpan Uptime => TimeSpan.FromMilliseconds(GetTickCount64());

        public static string OsName()
        {
            const string k = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var name = Reg.GetString(RegistryHive.LocalMachine, k, "ProductName") ?? "Windows";
            // Windows 11 still reports "Windows 10" in ProductName.
            if (AppInfo.IsWindows11) name = name.Replace("Windows 10", "Windows 11");
            var ver = Reg.GetString(RegistryHive.LocalMachine, k, "DisplayVersion") ?? Reg.GetString(RegistryHive.LocalMachine, k, "ReleaseId");
            var ubr = Reg.GetInt(RegistryHive.LocalMachine, k, "UBR");
            return $"{name} {ver} (сборка {AppInfo.WindowsBuild}{(ubr != null ? "." + ubr : "")})";
        }

        public static string Cpu() =>
            (Reg.GetString(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "?").Trim();

        public static List<string> Wmi(string query, params string[] props)
        {
            var list = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher(query))
                    foreach (ManagementObject o in s.Get())
                        list.Add(string.Join(" ", props.Select(p => o[p]?.ToString()?.Trim()).Where(v => !string.IsNullOrEmpty(v))));
            }
            catch (Exception ex) { Log.Warn("WMI " + query + ": " + ex.Message); }
            return list.Where(v => v.Length > 0).ToList();
        }

        public static List<KeyValuePair<string, List<KeyValuePair<string, string>>>> Collect()
        {
            var sections = new List<KeyValuePair<string, List<KeyValuePair<string, string>>>>();
            void Sec(string title, params (string k, string v)[] items) =>
                sections.Add(new KeyValuePair<string, List<KeyValuePair<string, string>>>(title,
                    items.Where(i => !string.IsNullOrEmpty(i.v)).Select(i => new KeyValuePair<string, string>(i.k, i.v)).ToList()));

            var mem = Memory();
            var up = Uptime;
            Sec("Система",
                ("Операционная система", OsName()),
                ("Разрядность", Environment.Is64BitOperatingSystem ? "64-бит" : "32-бит"),
                ("Имя компьютера", Environment.MachineName),
                ("Пользователь", Environment.UserDomainName + "\\" + Environment.UserName),
                ("Время работы", $"{(int)up.TotalDays} дн. {up.Hours} ч. {up.Minutes} мин."),
                ("Права GdAntiVirus", AppInfo.IsAdmin ? "Администратор" : "Обычный пользователь"));

            Sec("Оборудование",
                ("Процессор", Cpu()),
                ("Ядер / потоков", string.Join(", ", Wmi("SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor", "NumberOfCores", "NumberOfLogicalProcessors").Select(s => s.Replace(" ", " / ")))),
                ("Оперативная память", $"{Fmt.Size((long)mem.total)} (свободно {Fmt.Size((long)mem.free)})"),
                ("Видеокарта", string.Join("; ", Wmi("SELECT Name FROM Win32_VideoController", "Name"))),
                ("Материнская плата", string.Join("; ", Wmi("SELECT Manufacturer, Product FROM Win32_BaseBoard", "Manufacturer", "Product"))),
                ("BIOS", string.Join("; ", Wmi("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS", "Manufacturer", "SMBIOSBIOSVersion"))),
                ("Модель", string.Join("; ", Wmi("SELECT Manufacturer, Model FROM Win32_ComputerSystem", "Manufacturer", "Model"))));

            var disks = new List<(string, string)>();
            foreach (var d in DriveInfo.GetDrives())
                try
                {
                    if (!d.IsReady) continue;
                    disks.Add(($"Диск {d.Name.TrimEnd('\\')} {d.VolumeLabel}".Trim(),
                        $"{Fmt.Size(d.TotalSize - d.TotalFreeSpace)} из {Fmt.Size(d.TotalSize)} занято  •  {d.DriveFormat}  •  {DriveTypeName(d.DriveType)}"));
                }
                catch { }
            foreach (var m in Wmi("SELECT Model FROM Win32_DiskDrive", "Model")) disks.Add(("Накопитель", m));
            Sec("Диски", disks.ToArray());

            var net = new List<(string, string)>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var ips = ni.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString());
                    net.Add((ni.Name, $"{string.Join(", ", ips)}  •  {ni.Description}"));
                }
            }
            catch { }
            Sec("Сеть", net.ToArray());
            return sections;
        }

        private static string DriveTypeName(DriveType t)
        {
            switch (t)
            {
                case DriveType.Fixed: return "Локальный";
                case DriveType.Removable: return "Съёмный";
                case DriveType.Network: return "Сетевой";
                case DriveType.CDRom: return "Оптический";
                default: return t.ToString();
            }
        }
    }

    public sealed class CleanTarget
    {
        public string Title, Description;
        public Func<IEnumerable<string>> Folders;
        public Func<IEnumerable<string>> Files;
        public bool RecycleBin;
        public bool DefaultOn = true;
        public long Size;
    }

    public static class Cleaner
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, int flags);

        private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private static string ProgData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        private static IEnumerable<string> ChromiumCaches(string userData)
        {
            if (!Directory.Exists(userData)) yield break;
            foreach (var profile in SafeDirs(userData))
            {
                var n = Path.GetFileName(profile);
                if (n != "Default" && !n.StartsWith("Profile ")) continue;
                yield return Path.Combine(profile, "Cache");
                yield return Path.Combine(profile, "Code Cache");
                yield return Path.Combine(profile, "GPUCache");
            }
        }

        private static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch { return new string[0]; }
        }

        public static List<CleanTarget> Targets() => new List<CleanTarget>
        {
            new CleanTarget { Title = "Временные файлы пользователя", Description = "%TEMP% — остатки установщиков и программ", Folders = () => new[] { Path.GetTempPath() } },
            new CleanTarget { Title = "Временные файлы Windows", Description = @"C:\Windows\Temp", Folders = () => new[] { Path.Combine(Win, "Temp") } },
            new CleanTarget { Title = "Корзина", Description = "Файлы в корзине на всех дисках", RecycleBin = true },
            new CleanTarget { Title = "Кэш Центра обновлений", Description = "Уже установленные загрузки обновлений Windows", Folders = () => new[] { Path.Combine(Win, @"SoftwareDistribution\Download") } },
            new CleanTarget { Title = "Отчёты об ошибках и дампы", Description = "Windows Error Reporting и CrashDumps", Folders = () => new[] { Path.Combine(ProgData, @"Microsoft\Windows\WER"), Path.Combine(Local, "CrashDumps"), Path.Combine(Local, @"Microsoft\Windows\WER") } },
            new CleanTarget { Title = "Кэш эскизов", Description = "Миниатюры проводника (пересоздаются автоматически)", Files = () => SafeFiles(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "thumbcache_*.db") },
            new CleanTarget { Title = "Кэш Google Chrome", Description = "Пароли, история и вкладки не затрагиваются", Folders = () => ChromiumCaches(Path.Combine(Local, @"Google\Chrome\User Data")) },
            new CleanTarget { Title = "Кэш Microsoft Edge", Description = "Пароли, история и вкладки не затрагиваются", Folders = () => ChromiumCaches(Path.Combine(Local, @"Microsoft\Edge\User Data")) },
            new CleanTarget { Title = "Кэш Яндекс Браузера", Description = "Пароли, история и вкладки не затрагиваются", Folders = () => ChromiumCaches(Path.Combine(Local, @"Yandex\YandexBrowser\User Data")) },
            new CleanTarget { Title = "Кэш Firefox", Description = "Пароли, история и вкладки не затрагиваются", Folders = () => SafeDirs(Path.Combine(Local, @"Mozilla\Firefox\Profiles")).Select(p => Path.Combine(p, "cache2")) },
            new CleanTarget { Title = "Журналы Windows (*.log)", Description = @"Файлы *.log в C:\Windows\Logs", DefaultOn = false, Files = () => AllFiles(Path.Combine(Win, "Logs")).Where(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) },
        };

        private static IEnumerable<string> SafeFiles(string dir, string pattern)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : new string[0]; } catch { return new string[0]; }
        }

        private static IEnumerable<string> AllFiles(string dir)
        {
            var stack = new Stack<string>();
            if (Directory.Exists(dir)) stack.Push(dir);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                string[] files = null, dirs = null;
                try { files = Directory.GetFiles(d); dirs = Directory.GetDirectories(d); } catch { }
                if (files != null) foreach (var f in files) yield return f;
                if (dirs != null)
                    foreach (var s in dirs)
                        try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) == 0) stack.Push(s); } catch { }
            }
        }

        private static IEnumerable<string> FilesOf(CleanTarget t)
        {
            if (t.Files != null) foreach (var f in t.Files()) yield return f;
            if (t.Folders != null) foreach (var d in t.Folders()) foreach (var f in AllFiles(d)) yield return f;
        }

        public static long Measure(CleanTarget t)
        {
            if (t.RecycleBin)
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
            }
            long sum = 0;
            foreach (var f in FilesOf(t)) try { sum += new FileInfo(f).Length; } catch { }
            return sum;
        }

        /// <summary>Deletes what it can; locked files are skipped.</summary>
        public static long Clean(CleanTarget t)
        {
            if (t.RecycleBin)
            {
                long size = Measure(t);
                SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); // no confirmation, progress, sound
                return size;
            }
            long freed = 0;
            foreach (var f in FilesOf(t).ToList())
            {
                try
                {
                    var fi = new FileInfo(f);
                    long len = fi.Length;
                    if (fi.IsReadOnly) fi.IsReadOnly = false;
                    fi.Delete();
                    freed += len;
                }
                catch { }
            }
            if (t.Folders != null)
                foreach (var d in t.Folders())
                    foreach (var sub in SafeDirs(d))
                        try { Directory.Delete(sub, true); } catch { }
            return freed;
        }
    }
}
