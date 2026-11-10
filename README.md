# GdAntiVirus

!ENG

**Version 2.1 — the source code is now open.** Download `GdAntiVirus.exe` from [Releases](https://github.com/GdStepan2/GdAntiVirus/releases/latest).

GdAntiVirus helps fight viruses and set up Windows. Dark Windows 11 style interface with a sidebar (like MakuTweaker).

- **Scanner** — quick / full / custom scan: EICAR and SHA-256 signatures (you can add your own), disguised files (`photo.jpg.exe`, RLO trick), hidden system executables, malicious scripts (downloaders, ransomware commands), autorun.inf and suspicious shortcuts on USB drives. Built-in Microsoft Defender quick / full / offline scan and signature update.
- **Quarantine** — isolate, restore or delete files.
- **Security** — block USB autorun, Windows Script Host, PUA, SMBv1; firewall, hosts file check/reset, DNS/network/proxy reset, restore points, SFC / DISM.
- **Processes** — own task manager: detects fake system processes and programs running from Temp/Downloads.
- **Startup** — enable/disable/remove startup entries (same mechanism as Task Manager).
- **Cleaner** — temp files, recycle bin, update cache, browser caches.
- **Tweaks** and **Privacy** — Explorer, appearance, taskbar, system, telemetry, ads, Copilot, Recall…
- **Programs** — install 35+ popular apps via winget, update all apps.
- **Utilities**, **System information**, **Settings** (accent colour, log, update check).

Requires Windows 10/11 (uses the built-in .NET Framework 4.8) and runs as administrator. GdAntiVirus does not replace a full antivirus — keep Microsoft Defender or another antivirus enabled.

Bugs and ideas: gdstepan2github@gmail.com

### Build

```
dotnet build src/GdAntiVirus/GdAntiVirus.csproj -c Release
```

The exe is `src/GdAntiVirus/bin/Release/net48/GdAntiVirus.exe`. CI (`.github/workflows/build.yml`) builds it on Windows and runs `GdAntiVirus.exe --selftest <file>`, which opens every page and writes `OK` or the error.

!RU

**Версия 2.1 — исходный код открыт.** Скачать `GdAntiVirus.exe` можно в [Releases](https://github.com/GdStepan2/GdAntiVirus/releases/latest).

GdAntiVirus помогает бороться с вирусами и настраивать Windows. Тёмный интерфейс в стиле Windows 11 с боковым меню (как у MakuTweaker).

- **Сканер** — быстрая / полная / выборочная проверка: сигнатуры EICAR и SHA-256 (можно добавить свои), замаскированные файлы (`фото.jpg.exe`, трюк с RLO), скрытые системные exe, вредоносные скрипты (загрузчики, команды шифровальщиков), autorun.inf и подозрительные ярлыки на флешках. Встроенный запуск Microsoft Defender: быстрая, полная, офлайн-проверка и обновление баз.
- **Карантин** — изоляция, восстановление и удаление файлов.
- **Безопасность** — запрет автозапуска с флешек, Windows Script Host, PUA, SMBv1; брандмауэр, проверка и сброс hosts, сброс DNS/сети/прокси, точки восстановления, SFC / DISM.
- **Процессы** — свой диспетчер задач: находит подмену системных процессов и программы из Temp/Загрузок.
- **Автозагрузка** — включение, отключение и удаление (тот же механизм, что в диспетчере задач).
- **Очистка** — временные файлы, корзина, кэш обновлений и браузеров.
- **Твики** и **Конфиденциальность** — проводник, внешний вид, панель задач, система, телеметрия, реклама, Copilot, Recall…
- **Программы** — установка 35+ популярных программ через winget, обновление всех программ.
- **Утилиты**, **О системе**, **Настройки** (цвет акцента, журнал, проверка обновлений).

Нужна Windows 10/11 (используется встроенный .NET Framework 4.8), программа запускается от администратора. GdAntiVirus не заменяет полноценный антивирус — держите Microsoft Defender или другой антивирус включённым.

Ошибки и идеи: gdstepan2github@gmail.com
