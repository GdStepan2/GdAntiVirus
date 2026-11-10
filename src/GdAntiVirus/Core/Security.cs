using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace GdAntiVirus.Core
{
    public sealed class AvProduct
    {
        public string Name;
        public bool Enabled;
        public bool UpToDate;
    }

    public sealed class DefenderStatus
    {
        public bool Available;
        public bool AntivirusEnabled;
        public bool RealTime;
        public string SignatureVersion;
        public DateTime? SignatureUpdated;
        public DateTime? LastQuickScan;
        public DateTime? LastFullScan;
    }

    public static class Defender
    {
        public static string MpCmdRun
        {
            get
            {
                // The platform folder holds the newest engine; fall back to Program Files.
                try
                {
                    var platform = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Platform");
                    if (Directory.Exists(platform))
                    {
                        var exe = Directory.GetDirectories(platform).OrderByDescending(d => d)
                            .Select(d => Path.Combine(d, "MpCmdRun.exe")).FirstOrDefault(File.Exists);
                        if (exe != null) return exe;
                    }
                }
                catch { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
            }
        }

        public static bool HasMpCmdRun => File.Exists(MpCmdRun);

        public static DefenderStatus GetStatus()
        {
            var s = new DefenderStatus();
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus"))
                    foreach (ManagementObject o in searcher.Get())
                    {
                        s.Available = true;
                        s.AntivirusEnabled = o["AntivirusEnabled"] as bool? ?? false;
                        s.RealTime = o["RealTimeProtectionEnabled"] as bool? ?? false;
                        s.SignatureVersion = o["AntivirusSignatureVersion"] as string;
                        s.SignatureUpdated = WmiDate(o["AntivirusSignatureLastUpdated"]);
                        s.LastQuickScan = WmiDate(o["QuickScanEndTime"]);
                        s.LastFullScan = WmiDate(o["FullScanEndTime"]);
                    }
            }
            catch (Exception ex) { Log.Warn("Defender WMI недоступен: " + ex.Message); }
            return s;
        }

        private static DateTime? WmiDate(object v)
        {
            try { return v is string str && str.Length > 0 ? ManagementDateTimeConverter.ToDateTime(str) : (DateTime?)null; }
            catch { return null; }
        }

        /// <summary>Antivirus products registered in Windows Security Center (Defender, Kaspersky, ESET…).</summary>
        public static List<AvProduct> GetProducts()
        {
            var list = new List<AvProduct>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct"))
                    foreach (ManagementObject o in searcher.Get())
                    {
                        uint state = Convert.ToUInt32(o["productState"] ?? 0u);
                        list.Add(new AvProduct
                        {
                            Name = o["displayName"] as string ?? "?",
                            Enabled = ((state >> 12) & 0xF) == 1,
                            UpToDate = ((state >> 4) & 0xF) == 0,
                        });
                    }
            }
            catch (Exception ex) { Log.Warn("SecurityCenter2 недоступен: " + ex.Message); }
            return list;
        }
    }

    public static class Firewall
    {
        private const string Base = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";
        public static readonly string[] Profiles = { "DomainProfile", "StandardProfile", "PublicProfile" };

        /// <summary>True when every profile (domain, private, public) has the firewall on.</summary>
        public static bool? AllEnabled()
        {
            bool any = false;
            foreach (var p in Profiles)
            {
                var v = Reg.GetInt(RegistryHive.LocalMachine, Base + p, "EnableFirewall");
                if (v == null) continue;
                any = true;
                if (v == 0) return false;
            }
            return any ? true : (bool?)null;
        }
    }

    public sealed class SecuritySummary
    {
        public int Level; // 0 ok, 1 warning, 2 danger
        public string Title, Short;
        public List<string> Issues = new List<string>();
        public DefenderStatus Defender;
        public List<AvProduct> Products;
        public bool? Firewall;
        public int Quarantined;
    }

    public static class SecurityStatus
    {
        public static SecuritySummary Evaluate()
        {
            var s = new SecuritySummary
            {
                Defender = Defender.GetStatus(),
                Products = Defender.GetProducts(),
                Firewall = Firewall.AllEnabled(),
                Quarantined = Quarantine.Load().Count
            };

            bool anyAvOn = s.Products.Any(p => p.Enabled) || (s.Defender.Available && s.Defender.RealTime);
            if (!anyAvOn)
            {
                s.Level = 2;
                s.Issues.Add("Защита в реальном времени выключена — включите Microsoft Defender или другой антивирус.");
            }
            else if (s.Products.Any(p => p.Enabled && !p.UpToDate) ||
                     (s.Defender.Available && s.Defender.RealTime && s.Defender.SignatureUpdated < DateTime.Now.AddDays(-7)))
            {
                s.Level = Math.Max(s.Level, 1);
                s.Issues.Add("Антивирусные базы устарели — обновите их.");
            }

            if (s.Firewall == false)
            {
                s.Level = Math.Max(s.Level, 1);
                s.Issues.Add("Брандмауэр Windows выключен для одного из профилей сети.");
            }

            var last = AppSettings.LastScan;
            if (last == null || last < DateTime.Now.AddDays(-14))
            {
                s.Level = Math.Max(s.Level, 1);
                s.Issues.Add(last == null ? "Проверка GdAntiVirus ещё не выполнялась." : "Последняя проверка была больше 2 недель назад.");
            }
            else if (AppSettings.LastScanThreats > 0)
            {
                s.Level = Math.Max(s.Level, 1);
                s.Issues.Add($"При последней проверке найдено угроз: {AppSettings.LastScanThreats}.");
            }

            s.Title = s.Level == 0 ? "Компьютер защищён" : s.Level == 1 ? "Нужно внимание" : "Компьютер под угрозой";
            s.Short = s.Issues.Count == 0 ? "Проблем не найдено" : "Проблем: " + s.Issues.Count;
            return s;
        }
    }
}
