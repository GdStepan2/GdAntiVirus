using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GdAntiVirus.Core
{
    public sealed class AppEntry
    {
        public string Category, Name, Description, WingetId, Url, Glyph;
    }

    public static class AppCatalog
    {
        private static AppEntry W(string cat, string glyph, string name, string desc, string id) =>
            new AppEntry { Category = cat, Glyph = glyph, Name = name, Description = desc, WingetId = id };
        private static AppEntry U(string cat, string glyph, string name, string desc, string url) =>
            new AppEntry { Category = cat, Glyph = glyph, Name = name, Description = desc, Url = url };

        public static readonly List<AppEntry> All = new List<AppEntry>
        {
            // Second-opinion scanners: free tools that run alongside any antivirus.
            W("Антивирусы и защита", "\uEA18", "Malwarebytes", "Удаление рекламного и шпионского ПО", "Malwarebytes.Malwarebytes"),
            U("Антивирусы и защита", "\uEA18", "Dr.Web CureIt!", "Бесплатный сканер, работает без установки", "https://free.drweb.ru/cureit/"),
            U("Антивирусы и защита", "\uEA18", "Kaspersky Virus Removal Tool", "Бесплатная утилита лечения от Касперского", "https://www.kaspersky.ru/downloads/free-virus-removal-tool"),
            U("Антивирусы и защита", "\uEA18", "AdwCleaner", "Удаление рекламных расширений и тулбаров", "https://www.malwarebytes.com/adwcleaner"),
            U("Антивирусы и защита", "\uEA18", "ESET Online Scanner", "Разовая проверка от ESET", "https://www.eset.com/int/home/online-scanner/"),
            W("Антивирусы и защита", "\uE72E", "KeePassXC", "Безопасный менеджер паролей", "KeePassXCTeam.KeePassXC"),
            W("Антивирусы и защита", "\uE72E", "Bitwarden", "Менеджер паролей с синхронизацией", "Bitwarden.Bitwarden"),

            W("Браузеры", "\uE774", "Google Chrome", "Самый популярный браузер", "Google.Chrome"),
            W("Браузеры", "\uE774", "Mozilla Firefox", "Открытый браузер с защитой от слежки", "Mozilla.Firefox"),
            W("Браузеры", "\uE774", "Brave", "Браузер со встроенной блокировкой рекламы", "Brave.Brave"),
            W("Браузеры", "\uE774", "Opera", "Браузер со встроенным VPN", "Opera.Opera"),
            W("Браузеры", "\uE774", "Яндекс Браузер", "Браузер от Яндекса", "Yandex.Browser"),

            W("Утилиты", "\uE8B7", "7-Zip", "Архиватор 7z, zip, rar", "7zip.7zip"),
            W("Утилиты", "\uE8B7", "WinRAR", "Популярный архиватор", "RARLab.WinRAR"),
            W("Утилиты", "\uE721", "Everything", "Мгновенный поиск файлов", "voidtools.Everything"),
            W("Утилиты", "\uE90F", "PowerToys", "Набор утилит от Microsoft", "Microsoft.PowerToys"),
            W("Утилиты", "\uE70F", "Notepad++", "Продвинутый текстовый редактор", "Notepad++.Notepad++"),
            W("Утилиты", "\uE950", "CPU-Z", "Информация о процессоре и памяти", "CPUID.CPU-Z"),
            W("Утилиты", "\uE7F8", "HWiNFO", "Датчики и подробная информация о ПК", "REALiX.HWiNFO"),
            W("Утилиты", "\uEDA2", "CrystalDiskInfo", "Здоровье жёстких дисков и SSD", "CrystalDewWorld.CrystalDiskInfo"),
            W("Утилиты", "\uE74D", "BleachBit", "Глубокая очистка системы", "BleachBit.BleachBit"),
            W("Утилиты", "\uE8A7", "Revo Uninstaller", "Полное удаление программ с остатками", "RevoUninstaller.RevoUninstaller"),

            W("Медиа", "\uE8B2", "VLC", "Плеер для любых видео и аудио", "VideoLAN.VLC"),
            W("Медиа", "\uE714", "OBS Studio", "Запись экрана и стримы", "OBSProject.OBSStudio"),
            W("Медиа", "\uE8D6", "AIMP", "Лёгкий аудиоплеер", "AIMP.AIMP"),
            W("Медиа", "\uE7C5", "ShareX", "Скриншоты и запись экрана", "ShareX.ShareX"),
            W("Медиа", "\uE790", "GIMP", "Бесплатный графический редактор", "GIMP.GIMP.3"),
            W("Медиа", "\uE720", "Audacity", "Редактор звука", "Audacity.Audacity"),

            W("Общение", "\uE8BD", "Telegram", "Мессенджер", "Telegram.TelegramDesktop"),
            W("Общение", "\uE8BD", "Discord", "Голосовой чат для игр и сообществ", "Discord.Discord"),
            W("Общение", "\uE8BD", "Zoom", "Видеоконференции", "Zoom.Zoom"),

            W("Игры", "\uE7FC", "Steam", "Магазин и библиотека игр", "Valve.Steam"),
            W("Игры", "\uE7FC", "Epic Games Launcher", "Бесплатные игры каждую неделю", "EpicGames.EpicGamesLauncher"),

            W("Разработка и компоненты", "\uE943", "Visual Studio Code", "Редактор кода", "Microsoft.VisualStudioCode"),
            W("Разработка и компоненты", "\uE943", "Git", "Система контроля версий", "Git.Git"),
            W("Разработка и компоненты", "\uE943", "Python 3.12", "Язык программирования Python", "Python.Python.3.12"),
            W("Разработка и компоненты", "\uE74C", "Visual C++ 2015-2022 x64", "Нужен большинству программ и игр", "Microsoft.VCRedist.2015+.x64"),
            W("Разработка и компоненты", "\uE74C", ".NET Desktop Runtime 8", "Нужен современным .NET-программам", "Microsoft.DotNet.DesktopRuntime.8"),
            W("Разработка и компоненты", "\uE74C", "DirectX (End-User Runtime)", "Компоненты DirectX для старых игр", "Microsoft.DirectX"),
        };

        public static bool WingetAvailable()
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (System.IO.File.Exists(System.IO.Path.Combine(local, @"Microsoft\WindowsApps\winget.exe"))) return true;
                var r = Shell.RunAsync("where.exe", "winget").Result;
                return r.Ok;
            }
            catch { return false; }
        }

        public static Task<ProcResult> InstallAsync(AppEntry app) =>
            Shell.RunAsync("winget", $"install --id {app.WingetId} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", default, null, System.Text.Encoding.UTF8);
    }

    public static class Updater
    {
        /// <summary>Returns the latest release tag on GitHub, or null.</summary>
        public static async Task<(string tag, string url)> LatestAsync()
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("GdAntiVirus/" + AppInfo.Version);
                var json = await http.GetStringAsync($"https://api.github.com/repos/{AppInfo.Repo}/releases/latest");
                var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                var url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+/releases/tag/[^\"]+)\"").Groups[1].Value;
                return (tag, string.IsNullOrEmpty(url) ? $"https://github.com/{AppInfo.Repo}/releases/latest" : url);
            }
        }

        public static bool IsNewer(string tag)
        {
            Version Parse(string s)
            {
                var m = Regex.Match(s ?? "", @"\d+(\.\d+){0,3}");
                return m.Success && Version.TryParse(m.Value.Contains(".") ? m.Value : m.Value + ".0", out var v) ? v : new Version(0, 0);
            }
            return Parse(tag) > Parse(AppInfo.Version);
        }
    }
}
