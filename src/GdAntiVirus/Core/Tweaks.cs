using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GdAntiVirus.Core
{
    /// <summary>A reversible system setting shown as a toggle.</summary>
    public sealed class Tweak
    {
        public string Category, Title, Description, Glyph;
        public Func<bool> Get;
        public Action<bool> Set;
        public bool RestartExplorer;
        public bool Win11Only;
        public bool NeedsReboot;
    }

    public sealed class TweakGroup
    {
        public string Title;
        public List<Tweak> Items = new List<Tweak>();
    }

    public static class Tweaks
    {
        private const RegistryHive CU = RegistryHive.CurrentUser;
        private const RegistryHive LM = RegistryHive.LocalMachine;
        private const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

        /// <summary>DWORD tweak: on writes <paramref name="on"/>, off writes <paramref name="off"/> (null = delete value).</summary>
        private static Tweak Dword(string glyph, string title, string desc, RegistryHive hive, string path, string name, int on, int? off, int? missing = null,
            bool explorer = false, bool win11 = false, bool reboot = false)
        {
            return new Tweak
            {
                Glyph = glyph, Title = title, Description = desc, RestartExplorer = explorer, Win11Only = win11, NeedsReboot = reboot,
                Get = () => (Reg.GetInt(hive, path, name) ?? missing) == on,
                Set = v =>
                {
                    if (v) Reg.SetInt(hive, path, name, on);
                    else if (off == null) Reg.Delete(hive, path, name);
                    else Reg.SetInt(hive, path, name, off.Value);
                }
            };
        }

        /// <summary>Several DWORDs that are switched together; state is read from the first.</summary>
        private static Tweak Multi(string glyph, string title, string desc, params (RegistryHive hive, string path, string name, int on, int? off)[] values)
        {
            return new Tweak
            {
                Glyph = glyph, Title = title, Description = desc,
                Get = () => Reg.GetInt(values[0].hive, values[0].path, values[0].name) == values[0].on,
                Set = v =>
                {
                    foreach (var x in values)
                    {
                        if (v) Reg.SetInt(x.hive, x.path, x.name, x.on);
                        else if (x.off == null) Reg.Delete(x.hive, x.path, x.name);
                        else Reg.SetInt(x.hive, x.path, x.name, x.off.Value);
                    }
                }
            };
        }

        public static TweakGroup Explorer => new TweakGroup
        {
            Title = "Проводник",
            Items =
            {
                Dword("\uE8A5", "Показывать расширения файлов", "Видно «фото.jpg.exe» — главный способ распознать вирус-подделку", CU, Adv, "HideFileExt", 0, 1, 1, explorer: true),
                Dword("\uE7B3", "Показывать скрытые файлы", "Отображать файлы и папки с атрибутом «скрытый»", CU, Adv, "Hidden", 1, 2, 2, explorer: true),
                Dword("\uE7BA", "Показывать защищённые системные файлы", "Только для опытных пользователей", CU, Adv, "ShowSuperHidden", 1, 0, 0, explorer: true),
                Dword("\uE7F8", "Открывать проводник на «Этот компьютер»", "Вместо «Главная» / «Быстрый доступ»", CU, Adv, "LaunchTo", 1, null, 2, explorer: true),
                new Tweak
                {
                    Glyph = "\uE8FD", Title = "Классическое контекстное меню", Description = "Полное меню правой кнопки без «Показать дополнительные параметры»",
                    Win11Only = true, RestartExplorer = true,
                    Get = () => Reg.KeyExists(CU, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32"),
                    Set = v =>
                    {
                        const string key = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
                        if (v) Reg.SetString(CU, key + @"\InprocServer32", "", "");
                        else Reg.DeleteTree(CU, key);
                    }
                },
                Dword("\uE7F4", "Значок «Этот компьютер» на рабочем столе", "Показать ярлык компьютера на рабочем столе", CU,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0, 1, 1, explorer: true),
                Dword("\uE8B7", "Отключить группировку «Недавние файлы»", "Не показывать недавно открытые файлы в проводнике", CU,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0, 1, 1, explorer: true),
                Dword("\uE8C8", "Полный путь в заголовке окна", "Показывать полный путь к папке", CU,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", 1, 0, 0, explorer: true),
            }
        };

        public static TweakGroup Personalization => new TweakGroup
        {
            Title = "Внешний вид",
            Items =
            {
                Dword("\uE708", "Тёмная тема приложений", "Тёмное оформление для приложений Windows", CU, Personalize, "AppsUseLightTheme", 0, 1, 1),
                Dword("\uE793", "Тёмная тема системы", "Тёмные Пуск, панель задач и центр уведомлений", CU, Personalize, "SystemUsesLightTheme", 0, 1, 1),
                Dword("\uE9A9", "Эффекты прозрачности", "Полупрозрачные окна и панель задач", CU, Personalize, "EnableTransparency", 1, 0, 1),
                new Tweak
                {
                    Glyph = "\uE916", Title = "Быстрые меню", Description = "Уменьшить задержку появления подменю (100 мс вместо 400 мс)", NeedsReboot = true,
                    Get = () => Reg.GetString(CU, @"Control Panel\Desktop", "MenuShowDelay") == "100",
                    Set = v => Reg.SetString(CU, @"Control Panel\Desktop", "MenuShowDelay", v ? "100" : "400")
                },
                new Tweak
                {
                    Glyph = "\uE765", Title = "Отключить залипание клавиш", Description = "Больше не будет окна после пятикратного нажатия Shift",
                    Get = () => Reg.GetString(CU, @"Control Panel\Accessibility\StickyKeys", "Flags") == "506",
                    Set = v => Reg.SetString(CU, @"Control Panel\Accessibility\StickyKeys", "Flags", v ? "506" : "510")
                },
            }
        };

        public static TweakGroup Taskbar => new TweakGroup
        {
            Title = "Панель задач и Пуск",
            Items =
            {
                Dword("\uE8E4", "Выравнивание по левому краю", "Кнопка «Пуск» слева, как в Windows 10", CU, Adv, "TaskbarAl", 0, 1, 1, explorer: true, win11: true),
                Dword("\uE9F9", "Скрыть виджеты", "Убрать новости и погоду с панели задач", LM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, null, explorer: true),
                Dword("\uE8BE", "Скрыть кнопку «Представление задач»", "Убрать кнопку виртуальных рабочих столов", CU, Adv, "ShowTaskViewButton", 0, 1, 1, explorer: true),
                Dword("\uE121", "Секунды в часах", "Показывать секунды на часах панели задач", CU, Adv, "ShowSecondsInSystemClock", 1, 0, 0, explorer: true),
                Dword("\uE711", "Пункт «Завершить задачу»", "Закрыть зависшую программу правым кликом по значку на панели задач", CU,
                    Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1, 0, 0, win11: true),
                Dword("\uE721", "Отключить Bing в поиске", "Поиск в меню «Пуск» только по компьютеру, без интернета", CU,
                    @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null, explorer: true),
            }
        };

        public static TweakGroup System => new TweakGroup
        {
            Title = "Система",
            Items =
            {
                Multi("\uE7FC", "Отключить Xbox Game DVR", "Фоновая запись игр снижает FPS",
                    (CU, @"System\GameConfigStore", "GameDVR_Enabled", 0, 1),
                    (LM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, null)),
                Dword("\uE7E8", "Отключить быстрый запуск", "Полное выключение ПК; помогает при проблемах с драйверами и двойной загрузкой", LM,
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, 1, 1),
                Dword("\uE946", "Подробные сообщения при загрузке", "Показывать, что делает Windows при входе и выключении", LM,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", 1, null),
                Dword("\uE895", "Без автоперезагрузки после обновлений", "Не перезагружать ПК, пока пользователь в системе", LM,
                    @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1, null),
                Dword("\uE7EF", "Не обновлять драйверы через Windows Update", "Предотвращает замену рабочих драйверов", LM,
                    @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, null),
                Dword("\uE8A7", "Отключить автозапуск Edge в фоне", "Edge не будет запускаться вместе с Windows", LM,
                    @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, null),
            }
        };

        public static TweakGroup Privacy => new TweakGroup
        {
            Title = "Конфиденциальность",
            Items =
            {
                Multi("\uE9D9", "Отключить телеметрию", "Минимальный сбор диагностических данных Microsoft",
                    (LM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, null),
                    (LM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1, null)),
                Dword("\uE8EC", "Отключить рекламный идентификатор", "Приложения не будут показывать персональную рекламу", CU,
                    @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1, 1),
                Multi("\uE789", "Отключить советы и рекламу", "Никаких «рекомендаций», рекламы в Пуске и тихой установки приложений",
                    (CU, Cdm, "SubscribedContent-338389Enabled", 0, 1),
                    (CU, Cdm, "SubscribedContent-338388Enabled", 0, 1),
                    (CU, Cdm, "SubscribedContent-353694Enabled", 0, 1),
                    (CU, Cdm, "SystemPaneSuggestionsEnabled", 0, 1),
                    (CU, Cdm, "SilentInstalledAppsEnabled", 0, 1),
                    (CU, Cdm, "SoftLandingEnabled", 0, 1)),
                Dword("\uE81C", "Отключить журнал действий", "Windows не будет хранить историю ваших действий", LM,
                    @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, null),
                Dword("\uE81D", "Отключить определение местоположения", "Запретить приложениям доступ к местоположению", LM,
                    @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1, null),
                Dword("\uE99A", "Отключить Copilot", "Убрать помощника Copilot из системы", CU,
                    @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null, explorer: true),
                Dword("\uE7B3", "Отключить Recall", "Запретить снимки экрана для функции «Воспоминания»", CU,
                    @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null, win11: true),
                Dword("\uE8BD", "Отключить запросы отзывов", "Windows не будет просить оценить систему", CU,
                    @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null),
                Dword("\uE720", "Отключить голосовую активацию Cortana", "Запрет Cortana", LM,
                    @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0, null),
            }
        };

        public static TweakGroup Protection => new TweakGroup
        {
            Title = "Защита",
            Items =
            {
                Multi("\uE88E", "Запретить автозапуск с флешек и дисков", "Блокирует autorun-вирусы на съёмных носителях",
                    (LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", 255, null),
                    (CU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", 255, null)),
                Dword("\uE943", "Отключить Windows Script Host", "Блокирует запуск .vbs/.js-вирусов. Некоторые старые скрипты перестанут работать", CU,
                    @"Software\Microsoft\Windows Script Host\Settings", "Enabled", 0, null),
                Dword("\uE8C9", "Блокировать нежелательные программы (PUA)", "Defender будет блокировать рекламное и потенциально опасное ПО", LM,
                    @"SOFTWARE\Policies\Microsoft\Windows Defender", "PUAProtection", 1, null),
                Dword("\uE83D", "Отключить устаревший протокол SMBv1", "Закрывает уязвимость, через которую распространялся WannaCry", LM,
                    @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1", 0, null, reboot: true),
                Dword("\uE7EF", "Расширения файлов всегда видны", "Помогает заметить подделки вида «документ.pdf.exe»", CU, Adv, "HideFileExt", 0, 1, 1, explorer: true),
                Dword("\uE72E", "Контроль учётных записей (UAC)", "Запрос подтверждения при запуске программ от администратора — не отключайте", LM,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1, 0, 1, reboot: true),
            }
        };
    }
}
