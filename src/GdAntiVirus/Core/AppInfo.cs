using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace GdAntiVirus.Core
{
    public static class AppInfo
    {
        public static readonly string Version = "2.1";
        public const string Repo = "GdStepan2/GdAntiVirus";
        public const string Email = "gdstepan2github@gmail.com";

        public static readonly bool IsAdmin = CheckAdmin();

        public static string ExePath => Assembly.GetExecutingAssembly().Location;

        /// <summary>%ProgramData%\GdAntiVirus — quarantine, signatures.</summary>
        public static string DataDir => Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GdAntiVirus"));

        /// <summary>%AppData%\GdAntiVirus — settings and logs.</summary>
        public static string UserDir => Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GdAntiVirus"));

        public static string Ensure(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        private static bool CheckAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        private static int? _build;
        public static int WindowsBuild
        {
            get
            {
                if (_build == null)
                {
                    int.TryParse(Reg.GetString(Microsoft.Win32.RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber"), out var b);
                    _build = b;
                }
                return _build.Value;
            }
        }

        public static bool IsWindows11 => WindowsBuild >= 22000;
    }

    public static class Log
    {
        private static readonly object Lock = new object();
        public static event Action<string> Added;

        public static string FilePath => Path.Combine(AppInfo.Ensure(Path.Combine(AppInfo.UserDir, "logs")), "gdantivirus.log");

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception ex = null) => Write("ERROR", ex == null ? message : message + ": " + ex.GetBaseException().Message);

        private static void Write(string level, string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level}  {message}";
            lock (Lock)
            {
                try
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
            try { Added?.Invoke(line); } catch { }
        }

        public static string ReadAll()
        {
            lock (Lock)
            {
                try { return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8) : ""; }
                catch (Exception ex) { return ex.Message; }
            }
        }

        public static void Clear()
        {
            lock (Lock) { try { File.Delete(FilePath); } catch { } }
        }
    }

    /// <summary>Tiny key=value settings file.</summary>
    public static class AppSettings
    {
        private static readonly string FilePath = Path.Combine(AppInfo.UserDir, "settings.ini");
        private static readonly Dictionary<string, string> Values = Load();

        private static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
            }
            catch { }
            return d;
        }

        private static void Save()
        {
            try { File.WriteAllLines(FilePath, Values.Select(kv => kv.Key + "=" + kv.Value), Encoding.UTF8); } catch { }
        }

        private static string Get(string key, string def) => Values.TryGetValue(key, out var v) ? v : def;
        private static void Set(string key, string value) { Values[key] = value; Save(); }

        public static string Accent { get => Get("Accent", "#4CC2FF"); set => Set("Accent", value); }
        public static bool ConfirmActions { get => Get("ConfirmActions", "1") == "1"; set => Set("ConfirmActions", value ? "1" : "0"); }
        public static bool CheckUpdates { get => Get("CheckUpdates", "1") == "1"; set => Set("CheckUpdates", value ? "1" : "0"); }

        public static DateTime? LastScan
        {
            get => DateTime.TryParse(Get("LastScan", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : (DateTime?)null;
            set => Set("LastScan", value?.ToString("o", CultureInfo.InvariantCulture) ?? "");
        }

        public static int LastScanThreats { get => int.TryParse(Get("LastScanThreats", "0"), out var v) ? v : 0; set => Set("LastScanThreats", value.ToString()); }
        public static long TotalCleaned { get => long.TryParse(Get("TotalCleaned", "0"), out var v) ? v : 0; set => Set("TotalCleaned", value.ToString()); }
    }

    public static class Theme
    {
        public static readonly string[] Accents =
        {
            "#4CC2FF", "#60CDFF", "#99EBFF", "#6CCB5F", "#FFB900", "#FF8C00", "#FF6B6B", "#E74856", "#C239B3", "#A78BFA", "#00B7C3", "#B4B4B4"
        };

        public static void ApplyAccent(string hex)
        {
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(hex); }
            catch { c = (Color)ColorConverter.ConvertFromString("#4CC2FF"); }

            var res = Application.Current.Resources;
            res["AccentBrush"] = Freeze(new SolidColorBrush(c));
            res["AccentSoftBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B)));
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            res["OnAccentBrush"] = Freeze(new SolidColorBrush(lum > 0.55 ? Colors.Black : Colors.White));
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    }

    public static class Fmt
    {
        public static string Size(long bytes)
        {
            string[] u = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? v.ToString("0") : v.ToString("0.#")) + " " + u[i];
        }

        public static string Ago(DateTime? t)
        {
            if (t == null) return "никогда";
            var d = DateTime.Now - t.Value;
            if (d.TotalMinutes < 1) return "только что";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} мин. назад";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours} ч. назад";
            return $"{(int)d.TotalDays} дн. назад";
        }
    }
}
