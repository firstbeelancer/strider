# ZAI — Architecture Review v2

> Re-audit of Strider Mail after v0.1.0-rc2.
> Date: 2026-07-06 (Europe/Moscow)
> Author: ZAI (architecture reviewer)
> Scope: Main branch (`ca371d3`), Tags: `v0.1.0-rc1`, `v0.1.0-rc2`

---

## 0. Контекст повторного ревью

v0.1.0-rc2 был помечен как hotfix для silent Windows crash: добавлены
Console + File sinks в Serilog, Win32 MessageBox через `user32.dll`, пакетирование
`appsettings.json` через `None Link` с `CopyToPublishDirectory`.

Однако **симптом не исчез**. Пользователь сообщает: «Скачиваю архив из
раздела релизов, распаковываю, дабл-клик на ярлыке. Открывается модалка,
предлагающая запустить установку — выбираю "Запустить". Терминал на
пару секунд появляется и исчезает. Дальше ничего не происходит.»

То есть RC2 не решил проблему полностью — добавил только последний рубеж
(crash dialog). Ниже — что именно ломается и почему crash dialog не
показывается, плюс новые находки, не покрытые Wave 1+2+3.

---

## 1. Диагностика текущего краша

### 1.1 Что означает «терминал на пару секунд»

«Терминал» в контексте Avalonia 11 — это **нативное окно**, которое
успевает прорисовать свой первый кадр и почти сразу закрывается. Это
значит, что:

- `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` доходит до
  момента, когда Avalonia инициализирует платформу и пытается создать
  `MainWindow`.
- В `OnFrameworkInitializationCompleted` происходит одна из следующих
  вещей:
  - блокирующее ожидание `dbInit.InitializeAsync().GetAwaiter().GetResult()`
    кидает **до** создания `MainWindow`;
  - `ConfigureServices` бросает из DI-фабрик (например,
    `Directory.CreateDirectory` в `static DpapiKeychainService()`);
  - `MainWindowViewModel.LoadAccountsCommand.Execute(null)` стартует
    ассинхронно, и его unhandled exception убивает message pump.
- Avalonia-runtime ловит это на верхнем уровне и **завершает процесс**,
  но message pump уже остановлен — поэтому `ShowCrashDialog`,
  привязанный к тому же message pump через P/Invoke `MessageBox`, либо
  вызывается слишком поздно (после exit-process), либо вообще не
  отрисовывается пользователю.

### 1.2 Что подтверждают логи

RC2 обязан писать в `%LocalAppData%\StriderMail\logs\strider-YYYYMMDD.log`.
Если этого файла нет — упало ДО инициализации Serilog
(`ConfigureLogging` бросило).

Если файл пустой — упало между `Log.Logger = …` и первой записью.

Если в файле есть строка `Starting Strider Mail v0.1.0-rc2...`, но
нет `OS:`/`.NET`-версии — упало в момент Avalonia `UsePlatformDetect`
(чаще всего — несовместимость native runtime/Skia с системой).

### 1.3 Самая частая первопричина (новая находка F-025)

**Конфликт версий Avalonia в Strider.Host vs Strider.UI** —
подтверждено при `dotnet build`:

```
warning MSB3277: Avalonia.Themes.Fluent 11.0.10 vs 11.3.18
```

- `Strider.Host.csproj` пинит `Avalonia.Desktop 11.0.10`,
  `Avalonia.Themes.Fluent 11.0.10`, `Avalonia.Fonts.Inter 11.0.10`.
- `Strider.UI.csproj` берёт `Version="11.*"` — resolve получает 11.3.18.

Поведение в runtime недетерминировано: один из типов грузится одной
версией, остальные другой. FluentTheme может рендериться 11.3.18,
а `AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont()` —
использовать типы 11.0.10. В лучшем случае мерцание UI, в худшем —
`MissingMethodException` или `TypeLoadException` сразу после
`Application.Run()`.

Тот же warning подтверждает, что **MSBuild выбирает 11.0.10** (потому
что это «primary reference»). Если на машине пользователя установлен
NET 8 runtime без пакета Avalonia — падает.

### 1.4 Вторая частая первопричина (F-026)

**`Directory.CreateDirectory` в `static DpapiKeychainService()` —
необёрнутый throw на старте.**

```csharp
static DpapiKeychainService()
{
    Directory.CreateDirectory(StorageDir);   // ← throws IOException
}
```

StorageDir = `%AppData%\StriderMail\keychain`. На системах с переполненным
диском, с антивирусом, под контейнером (Windows Server, sandbox), или
если `%AppData%` имеет русские символы, права могут блокировать создание.
Тогда класс не загрузится, `services.AddSingleton<IKeychainService>(…)`
бросит `TypeInitializationException`, и Avalonia упадёт на старте
ещё до `OnFrameworkInitializationCompleted`.

### 1.5 Третья первопричина (F-027)

**MainWindow.axaml.cs пустой, но использует Emoji + emoji font.**

```xaml
<Button Content="➕ Add Account" .../>
<TextBlock Text="📧 Strider Mail" .../>
```

`Avalonia.Fonts.Inter` не содержит emoji. На Windows 10+ Avalonia подгружает
Segoe UI Emoji через fallback-цепочку, но **только если включён fallback** —
по умолчанию в 11.0.x он не включён. На Linux fallback отсутствует
полностью. Визуально окно открывается, и через 1–2 секунды GarbageCollect
или font-resolver кидает `ArgumentException` внутри Avalonia rendering loop
— окно закрывается без crash dialog.

### 1.6 Четвёртая первопричина (F-028)

**БД и логи в разных путях:**

- Логи: `%LocalAppData%\StriderMail\logs\` (правильно).
- БД и keychain: `%AppData%\Roaming\StriderMail\` (другой folder).

На Windows Server Core и в некоторых контейнерах
`Environment.SpecialFolder.ApplicationData` может вернуть некорректный
путь из-за missing junction (например, `C:\Windows\system32\config\systemprofile\AppData\Roaming`).
В этом случае DB создаётся в другой папке, отличной от ожидаемой, и
если на этой папке нет write right — SqliteConnection.Open() бросит.

---

## 2. Новые находки (не покрыты Wave 1+2+3)

| ID | Severity | Title |
|----|----------|-------|
| **F-025** | **CRITICAL** | Avalonia theme version conflict между Strider.Host (11.0.10) и Strider.UI (11.*) → недетерминированный startup |
| **F-026** | **CRITICAL** | `static DpapiKeychainService()` — необёрнутый `Directory.CreateDirectory`, кидает `TypeInitializationException` на старте до всех catch-handler'ов |
| **F-027** | **HIGH** | Emoji в XAML без подключённого emoji-font → mid-render crash |
| **F-028** | **HIGH** | БД и keychain в `Environment.SpecialFolder.ApplicationData` (Roaming) — может указывать на недоступную папку в sandbox/Server Core |
| **F-029** | **HIGH** | `ConfigureServices` синхронно бросает при любой ошибке DI — message pump ещё не запущен, crash dialog не успевает показаться |
| **F-030** | **HIGH** | `dbInit.InitializeAsync().GetAwaiter().GetResult()` блокирует UI thread без таймаута — может зависнуть навсегда |
| **F-031** | **MEDIUM**  | `IncludeNativeLibrariesForSelfExtract=true` без `EnableCompressionInSingleFile=true` — распаковка native DLL может падать при нехватке места в %TEMP% |
| **F-032** | **MEDIUM**  | `appsettings.json` копируется через `None Link` — если пользователь открывает через символическую ссылку или на FAT32, файл может не распаковаться |
| **F-033** | **MEDIUM**  | DI `IEventBus` — singleton, `MainWindowViewModel._eventBus.Subscribe<NewMessageEvent>(…)` — если подписчик падает, нет изоляции (кидает в `Publish`) |
| **F-034** | **MEDIUM**  | `static EncryptedSqliteConnectionFactory()` — `SQLitePCL.Batteries_V2.Init()` вызывается JIT при первом обращении. Если parallel-вызов в нескольких `OpenConnectionAsync`, может бросить `InvalidOperationException` "not thread-safe" |
| **F-035** | **MEDIUM**  | `App.axaml.cs` имеет `using System.Runtime.InteropServices;` — неиспользуемый import (после ревью RC2 там ничего из P/Invoke не осталось) |
| **F-036** | **LOW**     | HttpClient в `AddHttpClient<OpenAiCompatibleGateway>()` не имеет timeout — может зависнуть на AI-request |
| **F-037** | **LOW**     | `viewModel.LoadAccountsCommand.Execute(null)` вызывается без `await`, unobserved exception |
| **F-038** | **LOW**     | `dpapi_keychain` файлы именуются по ключу через `_`, но Windows файловая система case-insensitive — конфликты `OAuth2_token` vs `oauth2_token` |
| **F-039** | **LOW**     | Отсутствует unit-тест на самое начало startup sequence (DI + DB init + window) |
| **F-040** | **LOW**     | Нет health-check endpoint или telemetry, чтобы увидеть «жив ли процесс вообще» |

---

## 3. Подтверждённые проблемы из Wave 3, которые могут повлиять на startup

| ID | Описание | Эффект на текущий краш |
|----|----------|-------------------------|
| F-019 (LOAD_SETTINGS) | `appsettings.json` грузится, но если файл пустой — `ReadFrom.Configuration` бросает | Закрыто в RC2 (теперь Console+File baseline), но если после merge CFG добавляет sink и тот бросает — падение |
| F-021 (RELEASE) | release.yml не делает smoke-test на собранном бинаре | Подтверждено: RC1/RC2 опубликованы без проверки запуска |
| F-022 (.GITIGNORE) | .gitignore неполный | Не критично |

---

## 4. Архитектурные улучшения (не breaking, но значимые)

### ADR-0011 (новый): Single-file publish — включить `EnableCompressionInSingleFile` и fallback на multi-file при проблемах

**Решение:** использовать **`PublishSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true` + `EnableCompressionInSingleFile=true`** для self-contained сборок. Дополнительно — release-pipeline должен smoke-тестировать собранный `.exe` через `wine` (Linux) и `xvfb-run` либо выделенный staging runner (Windows).

### ADR-0012 (новый): Application data directory — всегда `LocalApplicationData`, никогда `ApplicationData`

**Решение:** перевести все сервисы (БД, keychain, logs, attachments) на
`Environment.SpecialFolder.LocalApplicationData\StriderMail\…`. Это путь,
который гарантированно writable под обычным пользователем и под system
service, в отличие от Roaming.

### ADR-0013 (новый): Crash handler на уровне `Main`, до передачи control в Avalonia

**Решение:** `Program.Main` должен:
1. Установить `AppDomain.CurrentDomain.UnhandledException` → Win32 MessageBox.
2. Установить `TaskScheduler.UnobservedTaskException` → Win32 MessageBox.
3. Только после этого `StartWithClassicDesktopLifetime`.

Без этого любой async-fail, произошедший до создания окна или в
background task после него, убьёт процесс без обратной связи.

---

## 5. План исправлений (Wave 4 — Startup Hardening)

### Этап 4.1 — Немедленно (блокер для RC3)

1. **F-025**: привести все Avalonia-ссылки к одной версии (11.0.10).
2. **F-026**: убрать `static` ctor из DpapiKeychainService, перейти на
   lazy-init.
3. **F-027**: добавить emoji-font fallback (`Lucide`, `Segoe UI Emoji`
   shim) или убрать emoji из XAML до Phase 1 Polish.
4. **F-029**: установить `AppDomain.UnhandledException` и
   `TaskScheduler.UnobservedTaskException` handler'ы с Win32 MessageBox
   ДО `StartWithClassicDesktopLifetime`.
5. **F-030**: обернуть `dbInit.InitializeAsync().GetAwaiter().GetResult()`
   в `Task.Run(...).Wait(TimeSpan.FromSeconds(30))` с fallback на
   plaintext-БД и warning.
6. **F-028**: перевести `dbPath` и `keychain StorageDir` на
   `Environment.SpecialFolder.LocalApplicationData`.

### Этап 4.2 — Перед RC3

7. **F-031**: убрать `PublishSingleFile=true` из default release, и
   публиковать multi-file для самодиагностики. Single-file вариант
   оставить как opt-in (`-p:PublishSingleFile=true` при ручном теге).
8. **F-021**: добавить smoke-test job в release.yml — запуск `.exe`
   под `wine` или staging runner, проверка exit code.
9. **F-038, F-039**: добавить unit-тест на конфигурацию DI и startup
   sequence через `WebApplicationFactory`-style approach.

### Этап 4.3 — Перед v0.1.0

10. **F-040**: добавить debug-mode `--console` флаг, при котором
    приложение стартует с принудительным console attach + Serilog
    console-only logger.
11. Удалить emoji из всех View (см. design system: «Используй иконки
    вместо эмодзи в UI»).

---

## 6. Оценка рисков

| Риск | Вероятность | Влияние | Митигация |
|------|-------------|---------|-----------|
| RC3 повторит судьбу RC2 | Высокая без Этапа 4.1 | Критическое | Реализовать 4.1.1–4.1.5 за одну итерацию |
| Пользователь потеряет доверие | Средняя | Высокое | Немедленно опубликовать FAQ по диагностике |
| Регрессия Avalonia 11.0 → 11.3 | Низкая после фикса версии | Среднее | Пин конкретной минор-версии 11.0.10 во всех 4 проектах |
| Single-file extraction fail | Средняя на медленных дисках | Высокое | Multi-file publish по умолчанию |

---

## 7. Что я уже исправил

(Заполняется Wave 4 execution log в одном коммите — без новых веток.)

