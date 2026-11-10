using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace GdAntiVirus.Core
{
    public enum ThreatLevel { Danger, Suspicious, Warning }

    public sealed class Threat
    {
        public string Path { get; set; }
        public ThreatLevel Level { get; set; }
        public string Reason { get; set; }
        public long Size { get; set; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public string LevelText => Level == ThreatLevel.Danger ? "Опасно" : Level == ThreatLevel.Suspicious ? "Подозрительно" : "Внимание";
    }

    public sealed class ScanProgress
    {
        public int Files;
        public int Threats;
        public string Current;
    }

    /// <summary>
    /// Lightweight heuristic + signature scanner. It does not replace a full antivirus engine:
    /// it looks for known-bad hashes, the EICAR test file, disguised executables and malicious script patterns.
    /// </summary>
    public sealed class Scanner
    {
        private static readonly HashSet<string> RiskyExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".scr", ".com", ".pif", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
            ".ps1", ".hta", ".lnk", ".msi", ".jar", ".cpl", ".sys", ".txt", ".inf"
        };

        private static readonly HashSet<string> ScriptExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".ps1", ".hta" };

        private static readonly HashSet<string> ExecExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".scr", ".com", ".pif", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta", ".cpl", ".lnk", ".msi", ".jar", ".ps1" };

        private static readonly string[] DocExt =
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".jpg", ".jpeg", ".png", ".gif", ".txt", ".mp3", ".mp4", ".avi", ".zip", ".rar", ".rtf", ".bmp" };

        private static readonly string[] ScriptPatterns =
        {
            "-encodedcommand", "-enc ", "frombase64string", "downloadstring", "downloadfile", "invoke-expression", "iex(", "iex (",
            "net.webclient", "start-bitstransfer", "bitsadmin /transfer", "certutil -urlcache", "certutil -decode", "-windowstyle hidden",
            "-w hidden", "wscript.shell", "adodb.stream", "msxml2.xmlhttp", "createobject(\"shell.application\")", "regsvr32 /s /n /u /i:",
            "mshta http", "vssadmin delete shadows", "bcdedit /set {default} recoveryenabled no", "wbadmin delete catalog",
            "set-mppreference -disablerealtimemonitoring", "add-mppreference -exclusionpath", "schtasks /create", "reg add hklm\\software\\microsoft\\windows\\currentversion\\run",
            "reg add hkcu\\software\\microsoft\\windows\\currentversion\\run", "attrib +h +s", "cipher /w"
        };

        private const string EicarSha256 = "275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f";
        private const string EicarMarker = "EICAR-STANDARD-ANTIVIRUS-TEST-FILE";

        private readonly HashSet<string> _badHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EicarSha256 };
        private readonly string _skipDir = AppInfo.DataDir;
        private readonly string _windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private readonly string _startupUser = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        private readonly string _startupCommon = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

        public static string SignaturesFile => System.IO.Path.Combine(AppInfo.DataDir, "signatures.txt");

        public Scanner()
        {
            // Users can add their own SHA-256 hashes (one per line, "#" comments allowed).
            try
            {
                if (!File.Exists(SignaturesFile))
                    File.WriteAllText(SignaturesFile, "# GdAntiVirus: свои SHA-256 хэши вредоносных файлов, по одному в строке\n# " + EicarSha256 + "  (EICAR test)\n");
                foreach (var line in File.ReadAllLines(SignaturesFile))
                {
                    var h = line.Split('#')[0].Trim().Split(' ', '\t')[0];
                    if (h.Length == 64) _badHashes.Add(h);
                }
            }
            catch { }
        }

        public static List<string> QuickTargets()
        {
            var list = new List<string>();
            void Add(string p) { if (!string.IsNullOrEmpty(p) && (Directory.Exists(p) || File.Exists(p)) && !list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p); }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
            Add(System.IO.Path.GetTempPath());
            Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Add(System.IO.Path.Combine(home, "Downloads"));
            Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
            // Files launched at startup from the Run keys.
            foreach (var item in StartupManager.Load())
                if (item.FilePath != null && File.Exists(item.FilePath)) Add(item.FilePath);
            // Removable drives (autorun.inf, disguised shortcuts).
            foreach (var d in DriveInfo.GetDrives())
                try { if (d.DriveType == DriveType.Removable && d.IsReady) Add(d.RootDirectory.FullName); } catch { }
            return list;
        }

        public static List<string> FullTargets()
        {
            var list = new List<string>();
            foreach (var d in DriveInfo.GetDrives())
                try { if ((d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.IsReady) list.Add(d.RootDirectory.FullName); } catch { }
            return list;
        }

        public List<Threat> Scan(IEnumerable<string> targets, IProgress<ScanProgress> progress, CancellationToken ct, bool deepForQuick = true)
        {
            var threats = new List<Threat>();
            var state = new ScanProgress();
            int sinceReport = 0;

            foreach (var target in targets)
            {
                if (File.Exists(target)) { Check(target, threats, state); continue; }
                if (!Directory.Exists(target)) continue;

                bool isRoot = System.IO.Path.GetPathRoot(target)?.TrimEnd('\\') == target.TrimEnd('\\');
                if (isRoot)
                {
                    var autorun = System.IO.Path.Combine(target, "autorun.inf");
                    if (File.Exists(autorun)) CheckAutorun(autorun, threats);
                }

                var stack = new Stack<string>();
                stack.Push(target);
                while (stack.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var dir = stack.Pop();
                    if (dir.StartsWith(_skipDir, StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                        {
                            try
                            {
                                var attr = File.GetAttributes(sub);
                                if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                                var name = System.IO.Path.GetFileName(sub);
                                if (name.Equals("WinSxS", StringComparison.OrdinalIgnoreCase) || name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                                    name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;
                                stack.Push(sub);
                            }
                            catch { }
                        }
                    }
                    catch { continue; }

                    IEnumerable<string> files;
                    try { files = Directory.EnumerateFiles(dir); } catch { continue; }
                    try
                    {
                        foreach (var f in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            Check(f, threats, state);
                            if (++sinceReport >= 40)
                            {
                                sinceReport = 0;
                                state.Threats = threats.Count;
                                progress?.Report(new ScanProgress { Files = state.Files, Threats = threats.Count, Current = f });
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
            }

            progress?.Report(new ScanProgress { Files = state.Files, Threats = threats.Count, Current = "" });
            return threats;
        }

        private void Check(string path, List<Threat> threats, ScanProgress state)
        {
            state.Files++;
            var ext = System.IO.Path.GetExtension(path);
            if (!RiskyExt.Contains(ext)) return;

            FileInfo fi;
            try { fi = new FileInfo(path); } catch { return; }
            if (!fi.Exists) return;

            var name = fi.Name;
            void Report(ThreatLevel level, string reason) => threats.Add(new Threat { Path = path, Level = level, Reason = reason, Size = fi.Length });

            // 1) Hash / EICAR signatures (small files only to keep it fast).
            if (fi.Length <= 64 * 1024 * 1024 && (ExecExt.Contains(ext) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) || fi.Length < 4096))
            {
                if (fi.Length < 4096 && ContainsEicar(path)) { Report(ThreatLevel.Danger, "Тестовый вирус EICAR"); return; }
                if (_badHashes.Count > 1 || fi.Length < 4096)
                {
                    var h = Sha256(path);
                    if (h != null && _badHashes.Contains(h)) { Report(ThreatLevel.Danger, "Совпадение с базой сигнатур (SHA-256)"); return; }
                }
            }

            // 2) Right-to-left override: "invoice\u202Efdp.exe" is shown as "invoiceexe.pdf".
            if (name.IndexOf('\u202E') >= 0) { Report(ThreatLevel.Danger, "Скрытое расширение (символ RLO в имени)"); return; }

            // 3) Double extension: photo.jpg.exe
            if (ExecExt.Contains(ext))
            {
                var inner = System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(name));
                if (!string.IsNullOrEmpty(inner) && DocExt.Contains(inner.ToLowerInvariant()))
                { Report(ThreatLevel.Danger, $"Двойное расширение ({inner}{ext}) — маскировка под документ"); return; }
                if (name.Contains("     " + ".")) { Report(ThreatLevel.Suspicious, "Много пробелов перед расширением"); return; }
            }

            bool inWindows = path.StartsWith(_windir, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(System.IO.Path.Combine(_windir, "Temp"), StringComparison.OrdinalIgnoreCase);
            bool inStartup = path.StartsWith(_startupUser, StringComparison.OrdinalIgnoreCase) || path.StartsWith(_startupCommon, StringComparison.OrdinalIgnoreCase);
            bool inTemp = path.IndexOf(@"\Temp\", StringComparison.OrdinalIgnoreCase) >= 0;

            // 4) Screensaver / pif outside Windows — classic malware carriers.
            if (!inWindows && (ext.Equals(".scr", StringComparison.OrdinalIgnoreCase) || ext.Equals(".pif", StringComparison.OrdinalIgnoreCase)))
            { Report(ThreatLevel.Suspicious, $"Исполняемый файл {ext} вне папки Windows"); return; }

            // 5) Hidden + system executables in user folders.
            if (!inWindows && ExecExt.Contains(ext))
            {
                var a = fi.Attributes;
                if ((a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0)
                { Report(ThreatLevel.Suspicious, "Скрытый системный исполняемый файл в пользовательской папке"); return; }
            }

            // 6) Scripts: look for downloader / obfuscation / anti-recovery patterns.
            if (ScriptExt.Contains(ext) && fi.Length < 2 * 1024 * 1024)
            {
                var hits = ScriptHits(path);
                if (hits.Count >= 3 || (hits.Count >= 1 && hits.Any(h => h.Contains("shadows") || h.Contains("recoveryenabled") || h.Contains("wbadmin") || h.Contains("disablerealtime"))))
                { Report(ThreatLevel.Danger, "Вредоносные команды в скрипте: " + string.Join(", ", hits.Take(3))); return; }
                if (hits.Count >= 1 && (inStartup || inTemp))
                { Report(ThreatLevel.Suspicious, "Подозрительный скрипт в " + (inStartup ? "автозагрузке" : "Temp") + ": " + string.Join(", ", hits.Take(3))); return; }
                if (inStartup && !ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
                { Report(ThreatLevel.Warning, "Скрипт запускается при входе в систему"); return; }
            }

            // 7) Shortcuts in Startup / on USB that run interpreters.
            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && fi.Length < 64 * 1024)
            {
                var txt = ReadText(path, true).ToLowerInvariant();
                bool runsShell = txt.Contains("powershell") || txt.Contains("mshta") || txt.Contains("wscript") || txt.Contains("cscript") || txt.Contains("cmd.exe /c") || txt.Contains("rundll32");
                if (runsShell && (inStartup || IsRemovable(path)))
                { Report(ThreatLevel.Suspicious, "Ярлык запускает командный интерпретатор"); return; }
            }

            // 8) Executables dropped into Temp and launched at startup.
            if (inStartup && (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                Report(ThreatLevel.Warning, "Программа в папке автозагрузки");
        }

        private static void CheckAutorun(string path, List<Threat> threats)
        {
            var txt = ReadText(path, false).ToLowerInvariant();
            if (txt.Contains("open=") || txt.Contains("shellexecute=") || txt.Contains("shell\\"))
                threats.Add(new Threat { Path = path, Level = ThreatLevel.Suspicious, Reason = "autorun.inf запускает программу с диска", Size = new FileInfo(path).Length });
        }

        private static bool IsRemovable(string path)
        {
            try { return new DriveInfo(System.IO.Path.GetPathRoot(path)).DriveType == DriveType.Removable; } catch { return false; }
        }

        private static bool ContainsEicar(string path)
        {
            try { return File.ReadAllText(path, Encoding.ASCII).Contains(EicarMarker); } catch { return false; }
        }

        private static List<string> ScriptHits(string path)
        {
            var text = ReadText(path, false).ToLowerInvariant();
            return ScriptPatterns.Where(p => text.Contains(p)).ToList();
        }

        private static string ReadText(string path, bool binary)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var a = Encoding.ASCII.GetString(bytes);
                // .lnk targets and some scripts are UTF-16.
                return binary || (bytes.Length > 2 && bytes[1] == 0) ? a + "\n" + Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }

        public static string Sha256(string path)
        {
            try
            {
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
            }
            catch { return null; }
        }
    }

    public sealed class QuarantineItem
    {
        public string Id { get; set; }
        public string OriginalPath { get; set; }
        public DateTime Date { get; set; }
        public string Reason { get; set; }
        public string StoredPath => System.IO.Path.Combine(Quarantine.Dir, Id + ".qtn");
    }

    public static class Quarantine
    {
        public static string Dir => AppInfo.Ensure(System.IO.Path.Combine(AppInfo.DataDir, "Quarantine"));
        private static string Index => System.IO.Path.Combine(Dir, "index.txt");
        private static readonly object Lock = new object();

        public static List<QuarantineItem> Load()
        {
            lock (Lock)
            {
                var list = new List<QuarantineItem>();
                try
                {
                    if (!File.Exists(Index)) return list;
                    foreach (var line in File.ReadAllLines(Index, Encoding.UTF8))
                    {
                        var p = line.Split('\t');
                        if (p.Length < 4) continue;
                        DateTime.TryParse(p[2], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var d);
                        var item = new QuarantineItem { Id = p[0], OriginalPath = p[1], Date = d, Reason = p[3] };
                        if (File.Exists(item.StoredPath)) list.Add(item);
                    }
                }
                catch (Exception ex) { Log.Error("Карантин: чтение", ex); }
                return list;
            }
        }

        private static void Save(List<QuarantineItem> items)
        {
            File.WriteAllLines(Index, items.Select(i => string.Join("\t", i.Id, i.OriginalPath, i.Date.ToString("o"), (i.Reason ?? "").Replace('\t', ' '))), Encoding.UTF8);
        }

        public static void Add(string path, string reason)
        {
            lock (Lock)
            {
                var items = Load();
                var item = new QuarantineItem { Id = Guid.NewGuid().ToString("N"), OriginalPath = path, Date = DateTime.Now, Reason = reason };
                File.SetAttributes(path, FileAttributes.Normal);
                MoveFile(path, item.StoredPath);
                items.Add(item);
                Save(items);
                Log.Info("В карантин: " + path + " (" + reason + ")");
            }
        }

        public static void Restore(QuarantineItem item)
        {
            lock (Lock)
            {
                var items = Load();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(item.OriginalPath));
                var target = item.OriginalPath;
                if (File.Exists(target)) target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target), System.IO.Path.GetFileNameWithoutExtension(target) + " (восстановлен)" + System.IO.Path.GetExtension(target));
                MoveFile(item.StoredPath, target);
                items.RemoveAll(i => i.Id == item.Id);
                Save(items);
                Log.Info("Восстановлен из карантина: " + target);
            }
        }

        public static void Delete(QuarantineItem item)
        {
            lock (Lock)
            {
                var items = Load();
                File.Delete(item.StoredPath);
                items.RemoveAll(i => i.Id == item.Id);
                Save(items);
                Log.Info("Удалён из карантина: " + item.OriginalPath);
            }
        }

        private static void MoveFile(string from, string to)
        {
            try { File.Move(from, to); }
            catch (IOException)
            {
                File.Copy(from, to, true);
                File.Delete(from);
            }
        }
    }

    public static class Hosts
    {
        public static string FilePath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

        public const string Default =
            "# Copyright (c) 1993-2009 Microsoft Corp.\r\n#\r\n# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.\r\n#\r\n" +
            "# localhost name resolution is handled within DNS itself.\r\n#\t127.0.0.1       localhost\r\n#\t::1             localhost\r\n";

        public static List<string> Entries()
        {
            try
            {
                return File.ReadAllLines(FilePath).Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            }
            catch { return new List<string>(); }
        }

        public static string Reset()
        {
            var backup = FilePath + ".gdav-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, backup, true);
                File.SetAttributes(FilePath, FileAttributes.Normal);
            }
            File.WriteAllText(FilePath, Default, Encoding.ASCII);
            Log.Info("hosts сброшен, копия: " + backup);
            return backup;
        }
    }
}
